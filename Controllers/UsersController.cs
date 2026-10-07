using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = CrmCatalog.AdminRole)]
public class UsersController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ApplicationDbContext dbContext,
    AuditService auditService) : Controller
{
    public async Task<IActionResult> Index(string? q, string? role, string? state, string? sort, int page = 1)
    {
        var userQuery = dbContext.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var searchText = q.Trim().ToLower();
            userQuery = userQuery.Where(user => user.Name.ToLower().Contains(searchText) || user.Email!.ToLower().Contains(searchText));
        }
        if (!string.IsNullOrEmpty(role))
        {
            var userIdsInRole = from userRole in dbContext.UserRoles
                                join identityRole in dbContext.Roles on userRole.RoleId equals identityRole.Id
                                where identityRole.Name == role
                                select userRole.UserId;
            userQuery = userQuery.Where(user => userIdsInRole.Contains(user.Id));
        }

        userQuery = sort switch
        {
            "email" => userQuery.OrderBy(user => user.Email),
            "newest" => userQuery.OrderByDescending(user => user.CreatedAt),
            "oldest" => userQuery.OrderBy(user => user.CreatedAt),
            _ => userQuery.OrderBy(user => user.Name)
        };
        var matchingUsers = await userQuery.ToListAsync();
        // Lockout end is a DateTimeOffset, which SQLite cannot compare, so this filter runs in memory.
        matchingUsers = state switch
        {
            "active" => matchingUsers.Where(user => user.Active).ToList(),
            "inactive" => matchingUsers.Where(user => !user.Active).ToList(),
            "locked" => matchingUsers.Where(user => user.LockoutEnd > DateTimeOffset.UtcNow).ToList(),
            _ => matchingUsers
        };

        var pageOfUsers = CrmService.Paginate(matchingUsers, page);
        var roleByUserId = new Dictionary<string, string>();
        foreach (var user in pageOfUsers.Items)
            roleByUserId[user.Id] = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? "";

        ViewBag.Roles = roleByUserId;
        ViewBag.Search = q;
        ViewBag.Role = role;
        ViewBag.State = state;
        ViewBag.Sort = sort ?? "name";
        ViewBag.CurrentUserId = userManager.GetUserId(User);
        return View(pageOfUsers);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string? id)
    {
        if (id == null)
            return View(new UserForm());
        var existingUser = await userManager.FindByIdAsync(id);
        if (existingUser == null)
            return NotFound();
        var roleName = (await userManager.GetRolesAsync(existingUser)).FirstOrDefault() ?? CrmCatalog.SalesExecutiveRole;
        return View(new UserForm { Id = existingUser.Id, Name = existingUser.Name, Email = existingUser.Email!, Active = existingUser.Active, Role = roleName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserForm form)
    {
        var isNewUser = string.IsNullOrEmpty(form.Id);
        var isEditingSelf = form.Id == userManager.GetUserId(User);

        if (!CrmCatalog.Roles.Contains(form.Role))
            ModelState.AddModelError(nameof(form.Role), "Select a valid role.");
        if (isNewUser && string.IsNullOrWhiteSpace(form.Password))
            ModelState.AddModelError(nameof(form.Password), "Password is required for a new user.");
        if (isEditingSelf && (!form.Active || form.Role != CrmCatalog.AdminRole))
            ModelState.AddModelError("", "You cannot remove your own administrator access.");
        if (!ModelState.IsValid)
            return View(form);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var user = isNewUser ? new AppUser() : await userManager.FindByIdAsync(form.Id!);
        if (user == null)
            return NotFound();

        var oldRoles = isNewUser ? [] : (await userManager.GetRolesAsync(user)).ToArray();
        var valueBeforeChange = new { user.Name, user.Email, user.Active, Roles = oldRoles };
        var emailChanged = !isNewUser && !string.Equals(user.Email, form.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        var activeChanged = !isNewUser && user.Active != form.Active;

        user.Name = form.Name.Trim();
        user.Email = form.Email.Trim();
        user.UserName = form.Email.Trim();
        user.Active = form.Active;

        var identityResult = isNewUser ? await userManager.CreateAsync(user, form.Password!) : await userManager.UpdateAsync(user);

        var passwordReset = false;
        if (identityResult.Succeeded && !isNewUser && !string.IsNullOrEmpty(form.Password))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            identityResult = await userManager.ResetPasswordAsync(user, resetToken, form.Password);
            passwordReset = identityResult.Succeeded;
        }

        var roleChanged = false;
        if (identityResult.Succeeded && !oldRoles.SequenceEqual([form.Role]))
        {
            if (oldRoles.Length > 0)
                identityResult = await userManager.RemoveFromRolesAsync(user, oldRoles);
            if (identityResult.Succeeded)
                identityResult = await userManager.AddToRoleAsync(user, form.Role);
            roleChanged = identityResult.Succeeded && !isNewUser;
        }

        if (!identityResult.Succeeded)
        {
            foreach (var identityError in identityResult.Errors)
                ModelState.AddModelError("", identityError.Description);
            return View(form);   // transaction is disposed without commit, so nothing is saved
        }

        // A new stamp signs the user out everywhere. Only do it when their access really changed.
        if (roleChanged || activeChanged || emailChanged)
            await userManager.UpdateSecurityStampAsync(user);

        if (passwordReset)
            auditService.AddEntry("Password Reset", "Users", user.Id);
        if (roleChanged)
            auditService.AddEntry("Role Change", "Users", user.Id, oldRoles, new[] { form.Role });
        auditService.AddEntry(isNewUser ? "Create" : "Update", "Users", user.Id, valueBeforeChange,
            new { user.Name, user.Email, user.Active, Role = form.Role });
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        if (isEditingSelf && (passwordReset || emailChanged))
            await signInManager.RefreshSignInAsync(user);

        TempData["Success"] = isNewUser ? $"User {user.Name} created." : $"Details for {user.Name} saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(string id)
    {
        var lockedUser = await userManager.FindByIdAsync(id);
        if (lockedUser == null)
            return NotFound();
        await userManager.SetLockoutEndDateAsync(lockedUser, null);
        await userManager.ResetAccessFailedCountAsync(lockedUser);
        await auditService.WriteEntryAsync("Unlock", "Users", id);
        TempData["Success"] = $"{lockedUser.Name}'s account has been unlocked.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Roles()
    {
        var userCountPerRole = new Dictionary<string, int>();
        foreach (var roleName in CrmCatalog.Roles)
            userCountPerRole[roleName] = (await userManager.GetUsersInRoleAsync(roleName)).Count;
        return View(userCountPerRole);
    }
}
