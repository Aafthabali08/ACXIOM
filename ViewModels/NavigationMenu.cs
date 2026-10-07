using AcxiomCRM.Services;

namespace AcxiomCRM.ViewModels;

public record MenuLink(string Label, string Url, string Icon, string MatchKey);

public record MenuSection(string Title, List<MenuLink> Links);

// Sidebar sections for each role. Hiding a link is only for convenience;
// the controllers enforce the same rules with [Authorize] and ownership checks.
public static class NavigationMenu
{
    public static List<MenuSection> ForRole(string roleName) => roleName switch
    {
        CrmCatalog.AdminRole => AdminMenu(),
        CrmCatalog.ManagerRole => ManagerMenu(),
        _ => SalesExecutiveMenu()
    };

    static List<MenuSection> AdminMenu() =>
    [
        new("Control Centre", [
            new("Admin dashboard", "/", "grid", "home"),
            new("Team performance", "/Team", "team", "team")
        ]),
        new("User Administration", [
            new("Users & access", "/Users", "users", "users"),
            new("Add new user", "/Users/Edit", "user-plus", "users-edit"),
            new("Roles & permissions", "/Users/Roles", "shield", "roles")
        ]),
        new("Security & Compliance", [
            new("Audit log", "/Reports?report=audit", "log", "report-audit"),
            new("User activity", "/Reports?report=user-activity", "pulse", "report-user-activity")
        ]),
        new("Business Records", [
            new("Customers", "/crm/customers", "building", "crm-customers"),
            new("Leads", "/crm/leads", "target", "crm-leads"),
            new("Opportunities", "/crm/opportunities", "chart", "crm-opportunities"),
            new("Follow-ups", "/crm/followups", "clock", "crm-followups"),
            new("Activities", "/crm/activities", "list", "crm-activities")
        ]),
        new("Insights", [
            new("Sales reports", "/Reports?report=pipeline", "report", "reports")
        ]),
        new("Account", [
            new("My profile", "/Account/Profile", "person", "profile")
        ])
    ];

    static List<MenuSection> ManagerMenu() =>
    [
        new("Overview", [
            new("Manager dashboard", "/", "grid", "home")
        ]),
        new("Team Management", [
            new("Team performance", "/Team", "team", "team"),
            new("Team activity", "/Reports?report=user-activity", "pulse", "report-user-activity")
        ]),
        new("Sales Pipeline", [
            new("Leads", "/crm/leads", "target", "crm-leads"),
            new("Opportunities", "/crm/opportunities", "chart", "crm-opportunities"),
            new("Pipeline report", "/Reports?report=pipeline", "report", "reports")
        ]),
        new("Customer Relations", [
            new("Customers", "/crm/customers", "building", "crm-customers"),
            new("Follow-ups", "/crm/followups", "clock", "crm-followups"),
            new("Activities", "/crm/activities", "list", "crm-activities")
        ]),
        new("Account", [
            new("My profile", "/Account/Profile", "person", "profile")
        ])
    ];

    static List<MenuSection> SalesExecutiveMenu() =>
    [
        new("My Workspace", [
            new("My dashboard", "/", "grid", "home"),
            new("My follow-ups", "/crm/followups", "clock", "crm-followups"),
            new("My activities", "/crm/activities", "list", "crm-activities")
        ]),
        new("My Sales", [
            new("My leads", "/crm/leads", "target", "crm-leads"),
            new("My opportunities", "/crm/opportunities", "chart", "crm-opportunities"),
            new("My customers", "/crm/customers", "building", "crm-customers")
        ]),
        new("My Insights", [
            new("My reports", "/Reports?report=pipeline", "report", "reports")
        ]),
        new("Account", [
            new("My profile", "/Account/Profile", "person", "profile")
        ])
    ];

    // Works out which menu link belongs to the current page so it can be highlighted.
    public static string CurrentMatchKey(string path, string reportName)
    {
        var lowerPath = path.ToLowerInvariant();
        if (lowerPath == "/" || lowerPath.StartsWith("/home")) return "home";
        if (lowerPath.StartsWith("/team")) return "team";
        if (lowerPath.StartsWith("/users/roles")) return "roles";
        if (lowerPath == "/users/edit") return "users-edit";
        if (lowerPath.StartsWith("/users")) return "users";
        if (lowerPath.StartsWith("/account/profile")) return "profile";
        if (lowerPath.StartsWith("/reports"))
            return reportName is "audit" or "user-activity" ? "report-" + reportName : "reports";
        foreach (var moduleName in CrmCatalog.Modules)
            if (lowerPath.StartsWith("/crm/" + moduleName))
                return "crm-" + moduleName;
        return "";
    }
}
