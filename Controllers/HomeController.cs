using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers;

[Authorize]
public class HomeController(DashboardService dashboardService, UserManager<AppUser> userManager) : Controller
{
    // One URL, three dashboards. The signed-in role decides which sections are built and shown.
    public async Task<IActionResult> Index([FromQuery] DashboardFilter filter)
    {
        var roleName = User.IsInRole(CrmCatalog.AdminRole) ? CrmCatalog.AdminRole
            : User.IsInRole(CrmCatalog.ManagerRole) ? CrmCatalog.ManagerRole
            : CrmCatalog.SalesExecutiveRole;
        var currentUser = await userManager.GetUserAsync(User);
        var userName = currentUser?.Name ?? "";

        if (!ModelState.IsValid)
        {
            // Bad custom range: show the default month view with the error message on top.
            Response.StatusCode = 400;
            var fallbackDashboard = await dashboardService.BuildAsync(roleName, userName, new DashboardFilter());
            fallbackDashboard.Filter = filter;
            return View(fallbackDashboard);
        }
        return View(await dashboardService.BuildAsync(roleName, userName, filter));
    }
}

// Global search from the header (assignment PDF 4.1). Each module is searched through
// CrmService, so a Sales Executive only finds records assigned to them.
[Authorize]
public class SearchController(CrmService crmService) : Controller
{
    public async Task<IActionResult> Index(string? q)
    {
        var searchText = q?.Trim() ?? "";
        var resultsPerModule = new Dictionary<string, List<RecordDto>>();
        if (searchText.Length >= 2)
        {
            foreach (var moduleName in CrmCatalog.Modules)
                resultsPerModule[moduleName] = await crmService.GetRecordsAsync(moduleName, new RecordFilter { Search = searchText, Sort = "name" });
        }
        ViewBag.Search = searchText;
        return View(resultsPerModule);
    }
}

[Authorize(Roles = CrmCatalog.AdminRole + "," + CrmCatalog.ManagerRole)]
public class TeamController(DashboardService dashboardService) : Controller
{
    public async Task<IActionResult> Index([FromQuery] DashboardFilter filter)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return View(new TeamPageViewModel { Filter = filter });
        }
        var members = await dashboardService.BuildTeamPerformanceAsync(filter);
        return View(new TeamPageViewModel { Filter = filter, Members = members });
    }
}
