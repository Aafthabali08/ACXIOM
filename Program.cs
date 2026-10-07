using System.Threading.RateLimiting;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite database and Data Protection keys live in App_Data (excluded from Git).
Directory.CreateDirectory("App_Data");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("App_Data/keys"))
    .SetApplicationName("AcxiomCRM");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = builder.Configuration.GetValue("Security:MaxAttempts", 5);
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(builder.Configuration.GetValue("Security:LockMinutes", 15));
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;

    // The API should answer with status codes, not redirect to the login page.
    options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, 401, "Please log in.");
    options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, 403, "Access is not permitted.");

    // On every request: the account must still be active and the security stamp must match.
    // Role change, password reset or deactivation by the admin changes the stamp, which ends old sessions.
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var signedInUser = await userManager.GetUserAsync(context.Principal);
        var cookieStamp = context.Principal.FindFirst("AspNet.Identity.SecurityStamp")?.Value;
        if (signedInUser == null || !signedInUser.Active || cookieStamp != signedInUser.SecurityStamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});

builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAuthorization(options =>
{
    // [Authorize] alone means "signed in AND holding one of the CRM roles".
    options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(CrmCatalog.Roles).Build();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CrmService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<LoginService>();

// Brute force protection on login/register, on top of Identity's account lockout.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = 500;
    if (context.Request.Path.StartsWithSegments("/api"))
        await context.Response.WriteAsJsonAsync(new { error = "Unable to complete the request. Please try again." });
    else
        await context.Response.WriteAsync("Unable to complete the request. Please return to the previous page and try again.");
}));
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "same-origin";
    headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    var isStaticFile = context.Request.Path.StartsWithSegments("/lib") || context.Request.Path.StartsWithSegments("/css") || context.Request.Path.StartsWithSegments("/js");
    if (!isStaticFile)
        headers.CacheControl = "no-cache, no-store";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

using (var startupScope = app.Services.CreateScope())
    await Seed.Run(startupScope.ServiceProvider, app.Configuration, app.Environment);

app.Run();

static Task ApiAwareRedirect(RedirectContext<CookieAuthenticationOptions> context, int statusCode, string message)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
    context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}
