using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ReportsController(ReportService reportService, CrmService crmService) : Controller
{
    public async Task<IActionResult> Index(string report = "pipeline", [FromQuery] RecordFilter? filter = null, bool export = false)
    {
        filter ??= new RecordFilter();
        if (!ReportService.ReportNames.Contains(report))
            return NotFound();

        var isAdmin = User.IsInRole(CrmCatalog.AdminRole);
        if (!ReportService.CanOpen(report, isAdmin, crmService.IsSalesExecutive))
            return Forbid();

        ViewBag.Report = report;
        ViewBag.Filter = filter;
        ViewBag.Owners = await crmService.GetReportUsersAsync();

        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            if (export)
                return ValidationProblem(ModelState);
            ViewBag.Headings = Array.Empty<string>();
            return View(CrmService.Paginate(new List<string[]>(), 1));
        }

        var reportTable = await reportService.BuildAsync(report, filter, isAdmin);
        // Export always contains every filtered row, not only the visible page.
        if (export)
            return File(ReportService.ToCsv(reportTable), "text/csv", $"acxiom-{report}.csv");

        ViewBag.Headings = reportTable.Headings;
        return View(CrmService.Paginate(reportTable.Rows, filter.Page));
    }
}
