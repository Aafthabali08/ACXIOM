using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Services;

namespace AcxiomCRM.ViewModels;

public class DashboardFilter : IValidatableObject
{
    // "all" is the default so the cards show true totals (assignment PDF 17.11);
    // today / week / month / custom are the date filters from PDF 4.3.
    [RegularExpression("^(all|today|week|month|custom)$", ErrorMessage = "Select a valid period.")]
    public string Period { get; set; } = "all";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Period == "custom" && (!From.HasValue || !To.HasValue))
            yield return new ValidationResult("Select both dates for a custom range.");
        if (Period == "custom" && From > To)
            yield return new ValidationResult("From date cannot be after To date.");
    }

    // Converts today / week / month into real From and To dates (India date).
    public void ResolveDates()
    {
        var today = CrmCatalog.TodayInIndia;
        if (Period == "custom")
            return;
        if (Period == "all")
        {
            From = null;
            To = null;
            return;
        }
        From = Period switch
        {
            "today" => today,
            "week" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            _ => new DateOnly(today.Year, today.Month, 1)
        };
        To = today;
    }

    public string PeriodLabel => Period switch
    {
        "all" => "All time",
        "today" => "Today",
        "week" => "This week",
        "month" => "This month",
        _ => $"{From:dd MMM} – {To:dd MMM yyyy}"
    };
}

public record TeamMemberPerformance(string UserId, string Name, int LeadCount, int ConvertedLeads, int OpenDeals,
    decimal OpenPipeline, decimal WeightedPipeline, int WonDeals, decimal WonValue, int LostDeals, int OverdueFollowUps, int ActivitiesLogged)
{
    public string WinRate => WonDeals + LostDeals == 0 ? "–" : (100m * WonDeals / (WonDeals + LostDeals)).ToString("0") + "%";
}

public record AuditEventRow(DateTime CreatedAt, string UserName, string Action, string Entity, string Result);

public record RecentUserRow(string Name, string Email, string Role, bool Active, DateTime CreatedAt);

public class DashboardViewModel
{
    public string RoleName { get; set; } = CrmCatalog.SalesExecutiveRole;
    public string RoleTitle => CrmCatalog.RoleTitle(RoleName);
    public string UserName { get; set; } = "";
    public DashboardFilter Filter { get; set; } = new();

    public bool IsAdmin => RoleName == CrmCatalog.AdminRole;
    public bool IsManager => RoleName == CrmCatalog.ManagerRole;
    public bool IsSalesExecutive => RoleName == CrmCatalog.SalesExecutiveRole;
    // Wording prefix: a sales executive sees "My", a manager "Team", an admin "Company".
    public string ScopeWord => IsSalesExecutive ? "My" : IsManager ? "Team" : "Company";

    // Records created in the selected period.
    public List<RecordDto> Customers { get; set; } = [];
    public List<RecordDto> Leads { get; set; } = [];
    public List<RecordDto> Opportunities { get; set; } = [];

    // Current work, independent of the period filter.
    public List<RecordDto> PendingFollowUps { get; set; } = [];
    public List<RecordDto> RecentActivities { get; set; } = [];
    public List<RecordDto> DealsClosingSoon { get; set; } = [];

    public List<RecordDto> OpenOpportunities => Opportunities.Where(row => row.Status == "Open").ToList();
    public decimal OpenPipelineValue => OpenOpportunities.Sum(row => row.Amount);
    public decimal WeightedPipelineValue => OpenOpportunities.Sum(row => row.WeightedAmount);
    public int WonCount => Opportunities.Count(row => row.Status == "Won");
    public int LostCount => Opportunities.Count(row => row.Status == "Lost");
    public decimal WonValue => Opportunities.Where(row => row.Status == "Won").Sum(row => row.Amount);
    public int OpenLeadCount => Leads.Count(row => CrmCatalog.IsOpenLead(row.Status));
    public string WinRate => WonCount + LostCount == 0 ? "–" : (100m * WonCount / (WonCount + LostCount)).ToString("0") + "%";
    public List<RecordDto> OverdueFollowUps => PendingFollowUps.Where(row => row.DueDate < CrmCatalog.TodayInIndia).ToList();
    public List<RecordDto> TodayFollowUps => PendingFollowUps.Where(row => row.DueDate == CrmCatalog.TodayInIndia).ToList();

    // Admin only.
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int LockedUsers { get; set; }
    public int FailedLoginsLast24Hours { get; set; }
    public Dictionary<string, int> UsersPerRole { get; set; } = [];
    public List<AuditEventRow> RecentAuditEvents { get; set; } = [];
    public List<RecentUserRow> RecentUsers { get; set; } = [];

    // Manager and admin.
    public List<TeamMemberPerformance> TeamPerformance { get; set; } = [];

    // Monthly won sales chart (last six months, by outcome date).
    public string[] MonthLabels { get; set; } = [];
    public decimal[] MonthlyWonSales { get; set; } = [];
}

public class TeamPageViewModel
{
    public DashboardFilter Filter { get; set; } = new();
    public List<TeamMemberPerformance> Members { get; set; } = [];
}
