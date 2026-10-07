using System.Security.Claims;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Tests;

// A throw-away CRM for one test: an in-memory SQLite database with the real schema,
// one admin, one manager and two sales executives (Rahul and Neha).
public sealed class TestWorkspace : IDisposable
{
    readonly SqliteConnection connection;

    public const string AdminId = "admin-user";
    public const string ManagerId = "manager-user";
    public const string RahulId = "rahul-user";
    public const string NehaId = "neha-user";

    public static DateOnly Today => CrmCatalog.TodayInIndia;

    public TestWorkspace()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var dbContext = NewDbContext();
        dbContext.Database.EnsureCreated();

        foreach (var roleName in CrmCatalog.Roles)
            dbContext.Roles.Add(new IdentityRole(roleName) { Id = roleName, NormalizedName = roleName.ToUpperInvariant() });
        AddUser(dbContext, AdminId, "Aarav Sharma", CrmCatalog.AdminRole);
        AddUser(dbContext, ManagerId, "Priya Nair", CrmCatalog.ManagerRole);
        AddUser(dbContext, RahulId, "Rahul Verma", CrmCatalog.SalesExecutiveRole);
        AddUser(dbContext, NehaId, "Neha Iyer", CrmCatalog.SalesExecutiveRole);
        dbContext.SaveChanges();
    }

    static void AddUser(ApplicationDbContext dbContext, string userId, string fullName, string roleName)
    {
        var email = userId + "@example.test";
        dbContext.Users.Add(new AppUser { Id = userId, Name = fullName, Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() });
        dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleName });
    }

    public ApplicationDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    // Services exactly as the web app builds them, but signed in as the given user.
    public (CrmService CrmService, ApplicationDbContext DbContext) SignInAs(string userId, string roleName)
    {
        var signedInUser = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, roleName)], "Test"));
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = signedInUser } };
        var dbContext = NewDbContext();
        var auditService = new AuditService(dbContext, httpContextAccessor);
        return (new CrmService(dbContext, httpContextAccessor, auditService), dbContext);
    }

    public CrmService Rahul() => SignInAs(RahulId, CrmCatalog.SalesExecutiveRole).CrmService;
    public CrmService Neha() => SignInAs(NehaId, CrmCatalog.SalesExecutiveRole).CrmService;
    public CrmService Manager() => SignInAs(ManagerId, CrmCatalog.ManagerRole).CrmService;

    static int phoneCounter;

    // Valid forms; each test changes only the field it is checking.
    public static RecordForm CustomerForm(string ownerId = RahulId) => new()
    {
        Module = "customers", Name = "Karthik Rao", Status = "Active", OwnerId = ownerId,
        Email = $"karthik{Interlocked.Increment(ref phoneCounter)}@example.test",
        Phone = "98" + (10000000 + Interlocked.Increment(ref phoneCounter)).ToString()
    };

    public static RecordForm LeadForm(string ownerId = RahulId) => new()
    {
        Module = "leads", Name = "Sneha Joshi", Status = "New", OwnerId = ownerId, Priority = "High",
        Email = $"sneha{Interlocked.Increment(ref phoneCounter)}@example.test",
        Phone = "97" + (10000000 + Interlocked.Increment(ref phoneCounter)).ToString(),
        Company = "Joshi Enterprises", Source = "Website", ExpectedValue = 250000
    };

    public static RecordForm OpportunityForm(Guid customerId, string ownerId = RahulId) => new()
    {
        Module = "opportunities", Name = "CRM implementation", Status = "Open", OwnerId = ownerId, CustomerId = customerId,
        Stage = "Proposal", Amount = 280000, Probability = 50, CloseDate = Today.AddDays(10)
    };

    public static RecordForm FollowUpForm(Guid? customerId = null, Guid? leadId = null, string ownerId = RahulId) => new()
    {
        Module = "followups", Name = "Share revised proposal", Status = "Planned", OwnerId = ownerId,
        CustomerId = customerId, LeadId = leadId, Type = "Call", DueDate = Today.AddDays(1)
    };

    public void Dispose() => connection.Dispose();
}
