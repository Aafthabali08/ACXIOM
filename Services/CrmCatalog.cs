namespace AcxiomCRM.Services;

// Fixed lists used by forms, validation, menus and reports. Keeping them in one place
// means the dropdowns and the server-side checks can never disagree.
public static class CrmCatalog
{
    public const string AdminRole = "Admin";
    public const string ManagerRole = "Manager";
    public const string SalesExecutiveRole = "SalesExecutive";

    public static readonly string[] Roles = [AdminRole, ManagerRole, SalesExecutiveRole];
    public static readonly string[] Modules = ["customers", "leads", "opportunities", "followups", "activities"];
    public static readonly string[] Stages = ["Qualification", "Proposal", "Negotiation", "Won", "Lost"];
    public static readonly string[] ActivityTypes = ["Call", "Meeting", "Email", "Task"];
    public static readonly string[] Priorities = ["Low", "Normal", "High"];

    static readonly TimeZoneInfo IndiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    // All business date rules (past close date, overdue follow-up) follow Indian date, not server date.
    public static DateOnly TodayInIndia => ToIndiaDate(DateTime.UtcNow);

    public static DateOnly ToIndiaDate(DateTime utcTime) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), IndiaTimeZone));

    public static bool IsValidModule(string? moduleName) => moduleName != null && Modules.Contains(moduleName);

    public static string ModuleTitle(string moduleName) => moduleName switch
    {
        "customers" => "Customers",
        "leads" => "Leads",
        "opportunities" => "Opportunities",
        "followups" => "Follow-ups",
        "activities" => "Activities",
        _ => moduleName
    };

    public static string RoleTitle(string roleName) => roleName switch
    {
        AdminRole => "Administrator",
        ManagerRole => "Sales Manager",
        SalesExecutiveRole => "Sales Executive",
        _ => "No access"
    };

    public static string[] StatusesFor(string moduleName) => moduleName switch
    {
        "customers" => ["Active", "Inactive"],
        "leads" => ["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"],
        "opportunities" => ["Open", "Won", "Lost"],
        "followups" => ["Planned", "Completed", "Missed", "Cancelled"],
        "activities" => ["Pending", "Completed", "Cancelled"],
        _ => throw new BusinessRuleException("", "Page not found.", 404)
    };

    // Allowed manual lead moves. "Converted" is reached only through the Convert action.
    public static string[] NextLeadStatuses(string currentStatus) => currentStatus switch
    {
        "New" => ["Contacted", "Lost"],
        "Contacted" => ["Qualified", "Unqualified", "Lost"],
        "Qualified" => ["Lost"],
        _ => []
    };

    // Lead statuses that still need work from the sales team.
    public static bool IsOpenLead(string status) => status is "New" or "Contacted" or "Qualified";
}

// Thrown by the service layer when a business rule fails. Controllers turn it into
// a form error (MVC) or a JSON error with the matching HTTP status code (API).
public class BusinessRuleException(string fieldName, string message, int statusCode = 400) : Exception(message)
{
    public string FieldName { get; } = fieldName;
    public int StatusCode { get; } = statusCode;
}
