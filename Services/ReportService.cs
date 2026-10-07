using System.Globalization;
using System.Text;
using AcxiomCRM.Data;
using AcxiomCRM.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

public record ReportTable(string ReportName, string Title, string[] Headings, List<string[]> Rows);

public class ReportService(ApplicationDbContext dbContext, CrmService crmService)
{
    public static readonly string[] ReportNames = ["pipeline", "conversion", "customers", "leads", "opportunities", "followups", "user-activity", "audit"];

    static readonly CultureInfo IndianCulture = new("en-IN");

    public static string ReportTitle(string reportName) => reportName switch
    {
        "pipeline" => "Pipeline",
        "conversion" => "Conversion",
        "customers" => "Customers",
        "leads" => "Leads",
        "opportunities" => "Opportunities",
        "followups" => "Follow-ups",
        "user-activity" => "User activity",
        "audit" => "Audit log",
        _ => reportName
    };

    // Which reports each role may open. The controller uses the same check before building anything.
    public static bool CanOpen(string reportName, bool isAdmin, bool isSalesExecutive) => reportName switch
    {
        "audit" => isAdmin,
        "user-activity" => !isSalesExecutive,
        _ => ReportNames.Contains(reportName)
    };

    public async Task<ReportTable> BuildAsync(string reportName, RecordFilter filter, bool isAdmin)
    {
        var report = reportName switch
        {
            "audit" => await BuildAuditReportAsync(filter, isAdmin),
            "user-activity" => await BuildUserActivityReportAsync(filter, isAdmin),
            "pipeline" => await BuildPipelineReportAsync(filter),
            "conversion" => await BuildConversionReportAsync(filter),
            _ => await BuildModuleReportAsync(reportName, filter)
        };

        var rows = report.Rows;
        // Module reports are already searched and sorted by CrmService; the summary reports are sorted here.
        if (reportName is "audit" or "user-activity" && !string.IsNullOrWhiteSpace(filter.Search))
            rows = rows.Where(row => string.Join(" ", row).Contains(filter.Search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (filter.Sort == "name" && reportName is "audit" or "user-activity" or "conversion")
            rows = rows.OrderBy(row => row[0]).ToList();
        if (filter.Sort == "oldest" && reportName is "audit" or "user-activity")
            rows.Reverse();
        return report with { Rows = rows };
    }

    async Task<ReportTable> BuildAuditReportAsync(RecordFilter filter, bool isAdmin)
    {
        var logs = await FilteredAuditLogsAsync(filter, isAdmin);
        var nameByUserId = await dbContext.Users.ToDictionaryAsync(user => user.Id, user => user.Name);
        var rows = logs.Select(log => new[]
        {
            log.CreatedAt.ToString("dd MMM yyyy HH:mm"), nameByUserId.GetValueOrDefault(log.UserId ?? "", "Unknown"), log.Entity, log.Action,
            log.RecordId, log.Result, log.OldValue ?? "", log.NewValue ?? "", log.Ip
        }).ToList();
        return new("audit", "Audit log", ["Date (UTC)", "User", "Module", "Action", "Record", "Result", "Old value", "New value", "IP address"], rows);
    }

    async Task<ReportTable> BuildUserActivityReportAsync(RecordFilter filter, bool isAdmin)
    {
        var logs = await FilteredAuditLogsAsync(filter, isAdmin);
        var nameByUserId = await dbContext.Users.ToDictionaryAsync(user => user.Id, user => user.Name);
        var rows = logs.GroupBy(log => log.UserId).Select(group => new[]
        {
            nameByUserId.GetValueOrDefault(group.Key ?? "", "Unknown"),
            group.Count().ToString(),
            group.Count(log => log.Action == "Create").ToString(),
            group.Count(log => log.Action == "Update").ToString(),
            group.Max(log => log.CreatedAt).ToString("dd MMM yyyy HH:mm")
        }).ToList();
        return new("user-activity", "User activity", ["User", "Total actions", "Created", "Updated", "Last activity (UTC)"], rows);
    }

    // Managers see only CRM module events; security events (logins, user changes) are admin only.
    async Task<List<Models.AuditLog>> FilteredAuditLogsAsync(RecordFilter filter, bool isAdmin)
    {
        var query = dbContext.AuditLogs.AsNoTracking();
        if (!isAdmin)
            query = query.Where(log => CrmCatalog.Modules.Contains(log.Entity));
        if (!string.IsNullOrEmpty(filter.Result))
            query = query.Where(log => log.Result == filter.Result);
        if (filter.From.HasValue)
        {
            var fromTime = filter.From.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(log => log.CreatedAt >= fromTime);
        }
        if (filter.To.HasValue)
        {
            var toTime = filter.To.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(log => log.CreatedAt <= toTime);
        }
        if (!string.IsNullOrEmpty(filter.Owner))
            query = query.Where(log => log.UserId == filter.Owner);
        if (!string.IsNullOrEmpty(filter.Type))
            query = query.Where(log => log.Entity == filter.Type);
        if (!string.IsNullOrEmpty(filter.Status))
            query = query.Where(log => log.Action == filter.Status);
        return await query.OrderByDescending(log => log.CreatedAt).ToListAsync();
    }

    // Open pipeline three ways (assignment PDF section 11): per stage, per owner, and stage × owner.
    async Task<ReportTable> BuildPipelineReportAsync(RecordFilter filter)
    {
        var openDeals = (await crmService.GetRecordsAsync("opportunities", filter)).Where(row => row.Status == "Open").ToList();
        static string[] PipelineRow(string view, string stage, string owner, List<RecordDto> deals) =>
            [view, stage, owner, deals.Count.ToString(), Rupees(deals.Sum(row => row.Amount)), Rupees(deals.Sum(row => row.WeightedAmount))];

        var stageRows = CrmCatalog.Stages.Where(stage => stage is not ("Won" or "Lost"))
            .Select(stage => PipelineRow("Stage-wise", stage, "All owners", openDeals.Where(row => row.Stage == stage).ToList()));
        var ownerRows = openDeals.GroupBy(row => row.Owner).OrderBy(group => group.Key)
            .Select(group => PipelineRow("Owner-wise", "All open stages", group.Key, group.ToList()));
        var detailRows = openDeals.GroupBy(row => new { row.Stage, row.Owner })
            .OrderBy(group => Array.IndexOf(CrmCatalog.Stages, group.Key.Stage)).ThenBy(group => group.Key.Owner)
            .Select(group => PipelineRow("Stage × owner", group.Key.Stage!, group.Key.Owner, group.ToList()));
        var totalRow = PipelineRow("Total", "All open stages", "All owners", openDeals);

        var rows = stageRows.Concat(ownerRows).Concat(detailRows).Append(totalRow).ToList();
        return new("pipeline", "Pipeline", ["View", "Stage", "Owner", "Open deals", "Amount", "Weighted value"], rows);
    }

    async Task<ReportTable> BuildConversionReportAsync(RecordFilter filter)
    {
        var leads = await crmService.GetRecordsAsync("leads", filter);
        var opportunities = await crmService.GetRecordsAsync("opportunities", filter);
        var convertedLeads = leads.Count(row => row.Status == "Converted");
        var wonDeals = opportunities.Count(row => row.Status == "Won");
        var lostDeals = opportunities.Count(row => row.Status == "Lost");
        List<string[]> rows =
        [
            ["Leads converted", convertedLeads.ToString(), Percentage(convertedLeads, leads.Count)],
            ["Leads not converted", (leads.Count - convertedLeads).ToString(), Percentage(leads.Count - convertedLeads, leads.Count)],
            ["Opportunities won", wonDeals.ToString(), Percentage(wonDeals, opportunities.Count)],
            ["Opportunities lost", lostDeals.ToString(), Percentage(lostDeals, opportunities.Count)]
        ];
        return new("conversion", "Conversion", ["Measure", "Count", "Rate"], rows);
    }

    async Task<ReportTable> BuildModuleReportAsync(string moduleName, RecordFilter filter)
    {
        var records = await crmService.GetRecordsAsync(moduleName, filter);
        return moduleName switch
        {
            "customers" => new("customers", "Customers", ["Customer", "Code", "Status", "Owner", "Created", "Company", "Email", "Phone"],
                records.Select(row => new[] { row.Name, row.Code, row.Status, row.Owner, row.CreatedAt.ToString("dd MMM yyyy"), row.Company ?? "", row.Email ?? "", row.Phone ?? "" }).ToList()),
            "leads" => new("leads", "Leads", ["Lead", "Code", "Source", "Status", "Priority", "Owner", "Expected value", "Converted", "Converted to customer"],
                records.Select(row => new[] { row.Name, row.Code, row.Source ?? "", row.Status, row.Priority ?? "", row.Owner, Rupees(row.ExpectedValue),
                    row.Status == "Converted" ? "Yes" : "No", row.RelatedTo ?? "" }).ToList()),
            "followups" => new("followups", "Follow-ups", ["Subject", "Date", "Status", "Owner", "Type", "Related record"],
                records.Select(row => new[] { row.Name, row.DueDate?.ToString("dd MMM yyyy") ?? "", row.IsOverdue ? "Overdue" : row.Status, row.Owner, row.Type ?? "", row.RelatedTo ?? "" }).ToList()),
            _ => new("opportunities", "Opportunities", ["Opportunity", "Customer", "Stage", "Status", "Amount", "Probability", "Close date", "Owner"],
                records.Select(row => new[] { row.Name, row.RelatedTo ?? "", row.Stage ?? "", row.Status, Rupees(row.Amount), row.Probability + "%", row.CloseDate?.ToString("dd MMM yyyy") ?? "", row.Owner }).ToList())
        };
    }

    // Excel treats cells starting with = + - @ as formulas, so those get a leading quote.
    public static byte[] ToCsv(ReportTable report)
    {
        static string SafeCell(string cellValue)
        {
            if (cellValue.Length > 0 && "=+-@\t\r".Contains(cellValue[0]))
                cellValue = "'" + cellValue;
            return "\"" + cellValue.Replace("\"", "\"\"") + "\"";
        }
        var lines = new[] { report.Headings }.Concat(report.Rows).Select(row => string.Join(",", row.Select(SafeCell)));
        var csvText = string.Join("\r\n", lines);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvText)).ToArray();
    }

    static string Rupees(decimal amount) => "₹" + amount.ToString("N0", IndianCulture);

    static string Percentage(int part, int total) => total == 0 ? "0%" : (100m * part / total).ToString("0.0") + "%";
}
