using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public static class Seed
{
    const string DemoPassword = "Acxiom@123";

    public static async Task Run(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        foreach (var roleName in CrmCatalog.Roles)
            if (!await roleManager.RoleExistsAsync(roleName))
                await roleManager.CreateAsync(new IdentityRole(roleName));

        var loadDemoData = environment.IsDevelopment() && configuration.GetValue<bool>("Seed:Demo");
        if (loadDemoData)
            await CreateDemoWorkspace(dbContext, userManager);
        else
            await CreateFirstAdmin(userManager, configuration);
    }

    // Production: the first admin comes from environment variables, nothing else is seeded.
    static async Task CreateFirstAdmin(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        var adminEmail = configuration["Seed:AdminEmail"];
        var adminPassword = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            return;
        if (await userManager.FindByEmailAsync(adminEmail) != null)
            return;

        var adminUser = new AppUser { Name = "Administrator", Email = adminEmail, UserName = adminEmail, EmailConfirmed = true };
        var createResult = await userManager.CreateAsync(adminUser, adminPassword);
        if (!createResult.Succeeded)
            throw new InvalidOperationException("Admin account could not be created. Check the password policy.");
        await userManager.AddToRoleAsync(adminUser, CrmCatalog.AdminRole);
    }

    static async Task CreateDemoWorkspace(ApplicationDbContext dbContext, UserManager<AppUser> userManager)
    {
        var demoUsers = new[]
        {
            (Email: "admin@acxiom.test", Name: "Aarav Sharma", Role: CrmCatalog.AdminRole),
            (Email: "manager@acxiom.test", Name: "Priya Nair", Role: CrmCatalog.ManagerRole),
            (Email: "sales@acxiom.test", Name: "Rahul Verma", Role: CrmCatalog.SalesExecutiveRole),
            (Email: "sales2@acxiom.test", Name: "Neha Iyer", Role: CrmCatalog.SalesExecutiveRole)
        };
        foreach (var demoUser in demoUsers)
        {
            if (await userManager.FindByEmailAsync(demoUser.Email) != null)
                continue;
            var newUser = new AppUser { Name = demoUser.Name, Email = demoUser.Email, UserName = demoUser.Email, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(newUser, DemoPassword);
            if (!createResult.Succeeded)
                throw new InvalidOperationException("Demo account setup failed.");
            await userManager.AddToRoleAsync(newUser, demoUser.Role);
        }

        if (await dbContext.Customers.AnyAsync())
            return;

        var rahul = (await userManager.FindByEmailAsync("sales@acxiom.test"))!;
        var neha = (await userManager.FindByEmailAsync("sales2@acxiom.test"))!;
        var today = CrmCatalog.TodayInIndia;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Customer name, company, city, deal name, deal amount, stage, probability, months back the deal was closed.
        var sampleAccounts = new[]
        {
            ("Karthik Rao", "Nirmaan Industries", "Bengaluru", "Annual service agreement", 450000m, "Qualification", 25, 0),
            ("Ananya Desai", "Bluepeak Technologies", "Pune", "CRM implementation", 280000m, "Proposal", 50, 0),
            ("Vikram Shah", "Shah Logistics", "Ahmedabad", "Fleet support renewal", 650000m, "Negotiation", 75, 0),
            ("Meera Menon", "Malabar Retail", "Kochi", "Retail expansion", 320000m, "Won", 100, 0),
            ("Arjun Kapoor", "Kapoor Textiles", "Jaipur", "Textile supply contract", 180000m, "Lost", 0, 1),
            ("Divya Reddy", "Deccan Foods", "Hyderabad", "Distribution software", 210000m, "Proposal", 50, 0),
            ("Suresh Pillai", "Coastal Marine Exports", "Chennai", "Port tracking system", 390000m, "Won", 100, 2),
            ("Lakshmi Iyer", "Saraswati Publications", "Mysuru", "Print workflow upgrade", 240000m, "Won", 100, 4)
        };
        var followUpSubjects = new[]
        {
            "Discuss service requirements", "Share revised proposal", "Confirm pricing with Vikram", "Check onboarding progress",
            "Review next quarter plan", "Arrange product demonstration", "Collect renewal feedback", "Plan annual review"
        };

        for (var index = 0; index < sampleAccounts.Length; index++)
        {
            var (contactName, companyName, cityName, dealName, dealAmount, stageName, probability, monthsBack) = sampleAccounts[index];
            var ownerId = index is 5 or 7 ? neha.Id : rahul.Id;
            var isClosed = stageName is "Won" or "Lost";
            DateOnly? closedOn = isClosed ? today.AddMonths(-monthsBack) : null;
            var createdOn = monthStart.AddMonths(-monthsBack);

            var customer = new Customer
            {
                Name = contactName, Company = companyName, Code = $"CUS-{1001 + index}",
                Email = $"contact{index + 1}@example.test", Phone = $"98765432{index + 10}",
                City = cityName, State = "India", Address = "Business district", Status = "Active",
                OwnerId = ownerId, CreatedBy = ownerId, CreatedAt = createdOn, Notes = "Demo customer for the assignment."
            };
            dbContext.Customers.Add(customer);

            dbContext.Opportunities.Add(new Opportunity
            {
                Name = dealName, CustomerId = customer.Id, OwnerId = ownerId, CreatedBy = ownerId,
                Amount = dealAmount, Stage = stageName, Status = isClosed ? stageName : "Open", Probability = probability,
                CloseDate = closedOn ?? today.AddDays(14 + index * 4), OutcomeDate = closedOn,
                Source = "Referral", CreatedAt = createdOn
            });

            // Due dates from yesterday onwards, so the demo shows overdue, today and upcoming work.
            dbContext.FollowUps.Add(new FollowUp
            {
                Name = followUpSubjects[index], CustomerId = customer.Id, OwnerId = ownerId, CreatedBy = ownerId,
                DueDate = today.AddDays(index - 1), Status = "Planned", Type = CrmCatalog.ActivityTypes[index % 4], CreatedAt = monthStart
            });

            dbContext.Activities.Add(new Activity
            {
                Name = "Introduction call with " + contactName.Split(' ')[0], CustomerId = customer.Id,
                OwnerId = ownerId, CreatedBy = ownerId, DueDate = today, Type = "Call", Status = "Completed",
                Notes = "Discussed business requirements and the next follow-up.", CreatedAt = monthStart
            });
        }

        var sampleLeads = new[]
        {
            ("Rohit Kumar", "Orbit Systems", "New", "Website", "High"),
            ("Sneha Joshi", "Joshi Enterprises", "Contacted", "Referral", "Normal"),
            ("Aditya Patel", "Patel Exports", "Qualified", "Website", "Normal"),
            ("Pooja Singh", "Northstar Services", "New", "Referral", "High"),
            ("Sanjay Das", "Eastern Traders", "Lost", "Website", "Normal"),
            ("Nisha Thomas", "Thomas Ventures", "Unqualified", "Referral", "Normal"),
            ("Farhan Qureshi", "Qureshi Hardware", "Contacted", "Exhibition", "High")
        };
        for (var index = 0; index < sampleLeads.Length; index++)
        {
            var (leadName, companyName, statusName, sourceName, priorityName) = sampleLeads[index];
            var ownerId = index is 5 or 6 ? neha.Id : rahul.Id;
            dbContext.Leads.Add(new Lead
            {
                Name = leadName, Code = $"LD-{2001 + index}", Company = companyName,
                Email = $"lead{index}@example.test", Phone = $"91234567{index + 10}",
                Status = statusName, Source = sourceName, Priority = priorityName,
                ExpectedValue = 150000 + index * 45000, OwnerId = ownerId, CreatedBy = ownerId, CreatedAt = monthStart
            });
        }

        dbContext.AuditLogs.Add(new AuditLog { UserId = rahul.Id, Action = "Setup", Entity = "Application", RecordId = "demo", NewValue = "Sample CRM records created" });
        await dbContext.SaveChangesAsync();
    }
}
