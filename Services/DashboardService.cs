using AcxiomCRM.Data;
using AcxiomCRM.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

// Builds the three role dashboards and the team performance table.
// Every number comes from CrmService queries, so the role scope is already applied.
public class DashboardService(ApplicationDbContext dbContext, CrmService crmService)
{
    public async Task<DashboardViewModel> BuildAsync(string roleName, string userName, DashboardFilter filter)
    {
        filter.ResolveDates();
        var today = CrmCatalog.TodayInIndia;
        var periodFilter = new RecordFilter { From = filter.From, To = filter.To };

        var dashboard = new DashboardViewModel { RoleName = roleName, UserName = userName, Filter = filter };
        dashboard.Customers = await crmService.GetRecordsAsync("customers", periodFilter);
        dashboard.Leads = await crmService.GetRecordsAsync("leads", periodFilter);
        dashboard.Opportunities = await crmService.GetRecordsAsync("opportunities", periodFilter);

        dashboard.PendingFollowUps = (await crmService.GetRecordsAsync("followups", new RecordFilter { Status = "Planned" }))
            .OrderBy(row => row.DueDate).ToList();
        dashboard.RecentActivities = (await crmService.GetRecordsAsync("activities", new RecordFilter()))
            .OrderByDescending(row => row.DueDate).ThenByDescending(row => row.CreatedAt).Take(5).ToList();

        var allOpportunities = await crmService.GetRecordsAsync("opportunities", new RecordFilter());
        dashboard.DealsClosingSoon = allOpportunities
            .Where(row => row.Status == "Open" && row.CloseDate <= today.AddDays(30))
            .OrderBy(row => row.CloseDate).Take(5).ToList();

        // Last six months of won business, grouped by the date the deal was actually won.
        var lastSixMonths = Enumerable.Range(0, 6).Select(offset => today.AddMonths(offset - 5)).ToList();
        dashboard.MonthLabels = lastSixMonths.Select(month => month.ToString("MMM yy")).ToArray();
        dashboard.MonthlyWonSales = lastSixMonths.Select(month => allOpportunities
            .Where(row => row.Status == "Won" && row.OutcomeDate?.Year == month.Year && row.OutcomeDate?.Month == month.Month)
            .Sum(row => row.Amount)).ToArray();

        if (dashboard.IsAdmin)
            await FillAdminSectionsAsync(dashboard);
        if (dashboard.IsAdmin || dashboard.IsManager)
            dashboard.TeamPerformance = await BuildTeamPerformanceAsync(filter);
        return dashboard;
    }

    async Task FillAdminSectionsAsync(DashboardViewModel dashboard)
    {
        var allUsers = await dbContext.Users.AsNoTracking().ToListAsync();
        var roleByUserId = await GetRoleByUserIdAsync();

        dashboard.TotalUsers = allUsers.Count;
        dashboard.ActiveUsers = allUsers.Count(user => user.Active);
        dashboard.LockedUsers = allUsers.Count(user => user.LockoutEnd > DateTimeOffset.UtcNow);
        dashboard.UsersPerRole = CrmCatalog.Roles.ToDictionary(roleName => roleName, roleName => roleByUserId.Values.Count(value => value == roleName));

        var oneDayAgo = DateTime.UtcNow.AddHours(-24);
        dashboard.FailedLoginsLast24Hours = await dbContext.AuditLogs
            .CountAsync(log => log.Entity == "Authentication" && log.Result == "Failed" && log.CreatedAt >= oneDayAgo);

        var nameByUserId = allUsers.ToDictionary(user => user.Id, user => user.Name);
        var latestLogs = await dbContext.AuditLogs.AsNoTracking().OrderByDescending(log => log.CreatedAt).Take(8).ToListAsync();
        dashboard.RecentAuditEvents = latestLogs
            .Select(log => new AuditEventRow(log.CreatedAt, nameByUserId.GetValueOrDefault(log.UserId ?? "", "Unknown"), log.Action, log.Entity, log.Result))
            .ToList();

        dashboard.RecentUsers = allUsers.OrderByDescending(user => user.CreatedAt).Take(5)
            .Select(user => new RecentUserRow(user.Name, user.Email ?? "", CrmCatalog.RoleTitle(roleByUserId.GetValueOrDefault(user.Id, "")), user.Active, user.CreatedAt))
            .ToList();
    }

    // One row per sales executive (plus anyone else who owns records) for the selected period.
    public async Task<List<TeamMemberPerformance>> BuildTeamPerformanceAsync(DashboardFilter filter)
    {
        filter.ResolveDates();
        var periodFilter = new RecordFilter { From = filter.From, To = filter.To };
        var leads = await crmService.GetRecordsAsync("leads", periodFilter);
        var opportunities = await crmService.GetRecordsAsync("opportunities", periodFilter);
        var activities = await crmService.GetRecordsAsync("activities", periodFilter);
        var pendingFollowUps = await crmService.GetRecordsAsync("followups", new RecordFilter { Status = "Overdue" });

        var roleByUserId = await GetRoleByUserIdAsync();
        // Inactive sales executives are shown only while they still own records in the period.
        var recordOwnerIds = leads.Select(row => row.OwnerId)
            .Concat(opportunities.Select(row => row.OwnerId))
            .Concat(activities.Select(row => row.OwnerId))
            .Concat(pendingFollowUps.Select(row => row.OwnerId))
            .ToHashSet();
        var salesExecutiveIds = roleByUserId.Where(pair => pair.Value == CrmCatalog.SalesExecutiveRole).Select(pair => pair.Key).ToHashSet();
        var allUsers = await dbContext.Users.AsNoTracking().OrderBy(user => user.Name).ToListAsync();
        var teamMembers = allUsers
            .Where(user => recordOwnerIds.Contains(user.Id) || (user.Active && salesExecutiveIds.Contains(user.Id)))
            .ToList();

        return teamMembers.Select(member =>
        {
            var memberDeals = opportunities.Where(row => row.OwnerId == member.Id).ToList();
            var openDeals = memberDeals.Where(row => row.Status == "Open").ToList();
            return new TeamMemberPerformance(member.Id, member.Name,
                LeadCount: leads.Count(row => row.OwnerId == member.Id),
                ConvertedLeads: leads.Count(row => row.OwnerId == member.Id && row.Status == "Converted"),
                OpenDeals: openDeals.Count,
                OpenPipeline: openDeals.Sum(row => row.Amount),
                WeightedPipeline: openDeals.Sum(row => row.WeightedAmount),
                WonDeals: memberDeals.Count(row => row.Status == "Won"),
                WonValue: memberDeals.Where(row => row.Status == "Won").Sum(row => row.Amount),
                LostDeals: memberDeals.Count(row => row.Status == "Lost"),
                OverdueFollowUps: pendingFollowUps.Count(row => row.OwnerId == member.Id),
                ActivitiesLogged: activities.Count(row => row.OwnerId == member.Id));
        }).ToList();
    }

    async Task<Dictionary<string, string>> GetRoleByUserIdAsync()
    {
        var userRoles = await (from userRole in dbContext.UserRoles
                               join role in dbContext.Roles on userRole.RoleId equals role.Id
                               select new { userRole.UserId, RoleName = role.Name }).ToListAsync();
        return userRoles.GroupBy(row => row.UserId).ToDictionary(group => group.Key, group => group.First().RoleName ?? "");
    }
}
