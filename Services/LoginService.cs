using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;

namespace AcxiomCRM.Services;

public record LoginOutcome(AppUser? User, bool Succeeded, bool IsLockedOut);

// Shared by the web login page and the JSON login API, so both lock out and audit the same way.
public class LoginService(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, AuditService auditService)
{
    public async Task<LoginOutcome> SignInAsync(string emailOrUserName, string password)
    {
        var typedLogin = emailOrUserName.Trim();
        var user = await userManager.FindByEmailAsync(typedLogin) ?? await userManager.FindByNameAsync(typedLogin);

        // Inactive accounts get the same generic failure as a wrong password.
        var signInResult = user != null && user.Active
            ? await signInManager.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true)
            : SignInResult.Failed;

        var auditAction = signInResult.Succeeded ? "Login" : signInResult.IsLockedOut ? "Lockout" : "Failed Login";
        await auditService.WriteEntryAsync(auditAction, "Authentication", user?.Id ?? "unknown",
            signInResult.Succeeded ? "Success" : "Failed", user?.Id);

        return new LoginOutcome(user, signInResult.Succeeded, signInResult.IsLockedOut);
    }

    public async Task SignOutAsync(string userId)
    {
        await auditService.WriteEntryAsync("Logout", "Authentication", userId);
        await signInManager.SignOutAsync();
    }
}
