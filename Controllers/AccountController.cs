using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

public class AccountController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    LoginService loginService,
    AuditService auditService,
    ApplicationDbContext dbContext) : Controller
{
    // returnUrl is the page the user tried to open before being sent to login.
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View(new LoginForm());
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("auth"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form, string? returnUrl)
    {
        // Only local addresses, so a crafted login link cannot send users to another site.
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        ViewBag.ReturnUrl = safeReturnUrl;
        if (!ModelState.IsValid)
            return View(form);

        var outcome = await loginService.SignInAsync(form.Email, form.Password);
        if (outcome.Succeeded)
            return safeReturnUrl != null ? LocalRedirect(safeReturnUrl) : RedirectToAction("Index", "Home");

        ModelState.AddModelError("", outcome.IsLockedOut
            ? "Account is temporarily locked. Please try again after the lockout period."
            : "Invalid login details or inactive account.");
        return View(form);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View(new RegisterForm());

    // Self registration always gives the lowest role. Only an admin can promote someone.
    [HttpPost, AllowAnonymous, EnableRateLimiting("auth"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterForm form)
    {
        if (!ModelState.IsValid)
            return View(form);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var newUser = new AppUser { Name = form.Name.Trim(), Email = form.Email.Trim(), UserName = form.Email.Trim() };
        var identityResult = await userManager.CreateAsync(newUser, form.Password);
        if (identityResult.Succeeded)
            identityResult = await userManager.AddToRoleAsync(newUser, CrmCatalog.SalesExecutiveRole);
        if (!identityResult.Succeeded)
        {
            foreach (var identityError in identityResult.Errors)
                ModelState.AddModelError("", identityError.Description);
            return View(form);
        }

        auditService.AddEntry("Register", "Users", newUser.Id, userId: newUser.Id);
        auditService.AddEntry("Login", "Authentication", newUser.Id, userId: newUser.Id);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        await signInManager.SignInAsync(newUser, isPersistent: false);
        TempData["Success"] = "Welcome aboard! Your Sales Executive workspace is ready.";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await loginService.SignOutAsync(userManager.GetUserId(User)!);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Denied()
    {
        Response.StatusCode = 403;
        return View();
    }

    // ---------- My profile (every role) ----------

    [HttpGet, Authorize]
    public async Task<IActionResult> Profile()
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null)
            return Challenge();
        return View(await BuildProfilePageAsync(currentUser));
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([Bind(Prefix = "Details")] ProfileForm details)
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null)
            return Challenge();
        if (!ModelState.IsValid)
            return ProfileWithErrors(await BuildProfilePageAsync(currentUser, details: details));

        var oldName = currentUser.Name;
        currentUser.Name = details.Name.Trim();
        var updateResult = await userManager.UpdateAsync(currentUser);
        if (!updateResult.Succeeded)
        {
            foreach (var identityError in updateResult.Errors)
                ModelState.AddModelError("", identityError.Description);
            return ProfileWithErrors(await BuildProfilePageAsync(currentUser, details: details));
        }

        auditService.AddEntry("Profile Update", "Users", currentUser.Id, new { Name = oldName }, new { currentUser.Name });
        await dbContext.SaveChangesAsync();
        TempData["Success"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, Authorize, EnableRateLimiting("auth"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordForm password)
    {
        var currentUser = await userManager.GetUserAsync(User);
        if (currentUser == null)
            return Challenge();
        if (!ModelState.IsValid)
            return ProfileWithErrors(await BuildProfilePageAsync(currentUser, password: password));

        // Identity checks the current password, applies the password policy and changes the security stamp.
        var changeResult = await userManager.ChangePasswordAsync(currentUser, password.CurrentPassword, password.NewPassword);
        if (!changeResult.Succeeded)
        {
            foreach (var identityError in changeResult.Errors)
            {
                var fieldName = identityError.Code == "PasswordMismatch" ? "Password.CurrentPassword" : "Password.NewPassword";
                ModelState.AddModelError(fieldName, identityError.Code == "PasswordMismatch" ? "Current password is not correct." : identityError.Description);
            }
            await auditService.WriteEntryAsync("Password Change", "Users", currentUser.Id, "Failed");
            return ProfileWithErrors(await BuildProfilePageAsync(currentUser, password: password));
        }

        await auditService.WriteEntryAsync("Password Change", "Users", currentUser.Id);
        // New stamp means the old cookie is no longer valid, so issue a fresh one for this browser.
        // Sessions on other devices are signed out by the stamp check in Program.cs.
        await signInManager.RefreshSignInAsync(currentUser);
        TempData["Success"] = "Password changed. Other sessions have been signed out.";
        return RedirectToAction(nameof(Profile));
    }

    IActionResult ProfileWithErrors(ProfilePageViewModel page)
    {
        Response.StatusCode = 400;
        return View(nameof(Profile), page);
    }

    async Task<ProfilePageViewModel> BuildProfilePageAsync(AppUser user, ProfileForm? details = null, ChangePasswordForm? password = null)
    {
        var roleName = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? "";
        // The newest login is the current session, so the one before it is the "previous sign-in".
        var previousLogin = await dbContext.AuditLogs.AsNoTracking()
            .Where(log => log.UserId == user.Id && log.Action == "Login")
            .OrderByDescending(log => log.CreatedAt)
            .Skip(1)
            .Select(log => (DateTime?)log.CreatedAt)
            .FirstOrDefaultAsync();

        return new ProfilePageViewModel
        {
            Details = details ?? new ProfileForm { Name = user.Name },
            Password = password ?? new ChangePasswordForm(),
            SavedName = user.Name,
            Email = user.Email ?? "",
            RoleTitle = CrmCatalog.RoleTitle(roleName),
            MemberSince = user.CreatedAt,
            LastLogin = previousLogin
        };
    }
}
