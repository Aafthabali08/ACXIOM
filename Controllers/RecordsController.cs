using AcxiomCRM.Data;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

// One controller serves all five CRM modules: /crm/customers, /crm/leads, /crm/opportunities,
// /crm/followups and /crm/activities. The module name is checked against a fixed list.
[Authorize, Route("crm/{module}")]
public class RecordsController(CrmService crmService, ApplicationDbContext dbContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string module, [FromQuery] RecordFilter filter)
    {
        if (!CrmCatalog.IsValidModule(module))
            return NotFound();
        await LoadDropdownsAsync(module);
        ViewBag.Filter = filter;
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return View(CrmService.Paginate(new List<RecordDto>(), 1));
        }
        var records = await crmService.GetRecordsAsync(module, filter);
        return View(CrmService.Paginate(records, filter.Page));
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(string module, Guid? customerId, Guid? leadId, Guid? opportunityId)
    {
        if (!CrmCatalog.IsValidModule(module))
            return NotFound();
        await LoadDropdownsAsync(module);
        var today = CrmCatalog.TodayInIndia;
        // customerId / leadId / opportunityId come from "Schedule follow-up" links on a details page.
        return View("Edit", new RecordForm
        {
            Module = module,
            Status = CrmCatalog.StatusesFor(module)[0],
            OwnerId = crmService.CurrentUserId,
            CloseDate = today.AddDays(30),
            DueDate = today,
            CustomerId = customerId,
            LeadId = customerId.HasValue ? null : leadId,
            OpportunityId = customerId.HasValue || leadId.HasValue ? null : opportunityId
        });
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(string module, Guid id)
    {
        try
        {
            var record = await crmService.FindRecordAsync(module, id);
            if (module == "leads" && record.Status == "Converted")
            {
                TempData["Error"] = "Converted leads are read-only. Open the linked customer to continue.";
                return RedirectToAction(nameof(Details), new { module, id });
            }
            await LoadDropdownsAsync(module);
            ViewBag.SavedStatus = record.Status;
            return View(crmService.ToRecordForm(record));
        }
        catch (BusinessRuleException error)
        {
            return StatusCode(error.StatusCode, error.Message);
        }
    }

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string module, RecordForm form)
    {
        if (!CrmCatalog.IsValidModule(module))
            return NotFound();
        form.Module = module;

        if (ModelState.IsValid)
        {
            try
            {
                var savedRecord = await crmService.SaveRecordAsync(module, form, form.Id == Guid.Empty ? null : form.Id);
                TempData["Success"] = "Record saved successfully.";
                return RedirectToAction(nameof(Details), new { module, id = savedRecord.Id });
            }
            catch (BusinessRuleException error)
            {
                ModelState.AddModelError(error.FieldName, error.Message);
                Response.StatusCode = error.StatusCode;
            }
        }
        else
        {
            Response.StatusCode = 400;
        }
        await LoadDropdownsAsync(module);
        if (form.Id != Guid.Empty)
        {
            // Lead status choices depend on the status stored in the database, not the one just submitted.
            var savedRecord = await crmService.GetScopedQuery(module).AsNoTracking().FirstOrDefaultAsync(record => record.Id == form.Id);
            ViewBag.SavedStatus = savedRecord?.Status;
        }
        return View("Edit", form);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(string module, Guid id)
    {
        try
        {
            var record = await crmService.FindRecordAsync(module, id);
            var recordKey = id.ToString();
            ViewBag.History = await dbContext.AuditLogs.AsNoTracking()
                .Where(log => log.Entity == module && log.RecordId == recordKey)
                .OrderByDescending(log => log.CreatedAt)
                .Take(30)
                .ToListAsync();
            ViewBag.FollowUps = (await crmService.GetRecordsAsync("followups", new RecordFilter { Sort = "date" }))
                .Where(row => row.CustomerId == id || row.LeadId == id || row.OpportunityId == id).ToList();
            ViewBag.Activities = (await crmService.GetRecordsAsync("activities", new RecordFilter()))
                .Where(row => row.CustomerId == id || row.LeadId == id).ToList();
            ViewBag.Opportunities = module is "customers" or "leads"
                ? (await crmService.GetRecordsAsync("opportunities", new RecordFilter())).Where(row => row.CustomerId == id || row.LeadId == id).ToList()
                : new List<RecordDto>();
            return View(crmService.ToRecordDto(record));
        }
        catch (BusinessRuleException error)
        {
            return StatusCode(error.StatusCode, error.Message);
        }
    }

    [HttpPost("{id:guid}/delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string module, Guid id)
    {
        try
        {
            await crmService.DeleteRecordAsync(module, id);
            TempData["Success"] = "Record deleted successfully.";
            return RedirectToAction(nameof(Index), new { module });
        }
        catch (BusinessRuleException error) when (error.StatusCode != 404)
        {
            TempData["Error"] = error.Message;
            return RedirectToAction(nameof(Details), new { module, id });
        }
        catch (BusinessRuleException error)
        {
            return StatusCode(error.StatusCode, error.Message);
        }
    }

    [HttpPost("{id:guid}/convert"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(string module, Guid id)
    {
        if (module != "leads")
            return NotFound();
        try
        {
            var customerId = await crmService.ConvertLeadAsync(id);
            TempData["Success"] = "Lead converted. An opportunity is also created when the expected value is above zero.";
            return RedirectToAction(nameof(Details), new { module = "customers", id = customerId });
        }
        catch (BusinessRuleException error) when (error.StatusCode != 404)
        {
            TempData["Error"] = error.Message;
            return RedirectToAction(nameof(Details), new { module, id });
        }
        catch (BusinessRuleException error)
        {
            return StatusCode(error.StatusCode, error.Message);
        }
    }

    // "Mark done" button on the dashboard, list and details pages.
    [HttpPost("{id:guid}/complete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(string module, Guid id, string? returnUrl)
    {
        try
        {
            await crmService.MarkCompletedAsync(module, id);
            TempData["Success"] = "Marked as done.";
        }
        catch (BusinessRuleException error) when (error.StatusCode != 404)
        {
            TempData["Error"] = error.Message;
        }
        catch (BusinessRuleException error)
        {
            return StatusCode(error.StatusCode, error.Message);
        }
        // Only local URLs, so the button cannot be used as an open redirect.
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Details), new { module, id });
    }

    async Task LoadDropdownsAsync(string module)
    {
        ViewBag.Module = module;
        ViewBag.Title = CrmCatalog.ModuleTitle(module);
        ViewBag.Owners = new SelectList(await crmService.GetAssignableUsersAsync(), "Id", "Name");
        ViewBag.Statuses = CrmCatalog.StatusesFor(module);
        var customers = await crmService.GetScopedQuery("customers").AsNoTracking().OrderBy(record => record.Name).ToListAsync();
        var leads = await crmService.GetScopedQuery("leads").AsNoTracking().OrderBy(record => record.Name).ToListAsync();
        var opportunities = await crmService.GetScopedQuery("opportunities").AsNoTracking().OrderBy(record => record.Name).ToListAsync();
        ViewBag.Customers = new SelectList(customers, "Id", "Name");
        ViewBag.Leads = new SelectList(leads, "Id", "Name");
        ViewBag.Opportunities = new SelectList(opportunities, "Id", "Name");
        // Owner of every related record, so the form can pre-select "Assigned to" when one is picked.
        ViewBag.RecordOwners = customers.Concat(leads).Concat(opportunities).ToDictionary(record => record.Id.ToString(), record => record.OwnerId);
    }
}
