using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcxiomCRM.Controllers;

// JSON version of the CRM screens. It calls the same CrmService, so the same rules apply.
[ApiController, Authorize, Route("api")]
public class ApiController(CrmService crmService) : ControllerBase
{
    const string ModuleRoute = "{module:regex(^(customers|leads|opportunities|followups|activities)$)}";

    [HttpGet(ModuleRoute)]
    public async Task<IActionResult> List(string module, [FromQuery] RecordFilter filter) =>
        Ok(CrmService.Paginate(await crmService.GetRecordsAsync(module, filter), filter.Page));

    [HttpGet(ModuleRoute + "/{id:guid}")]
    public Task<IActionResult> Get(string module, Guid id) =>
        RunAsync(async () => Ok(crmService.ToRecordDto(await crmService.FindRecordAsync(module, id))));

    [HttpPost(ModuleRoute)]
    public Task<IActionResult> Create(string module, RecordForm form) => RunAsync(async () =>
    {
        form.Id = Guid.Empty;
        var createdRecord = await crmService.SaveRecordAsync(module, form);
        return Created($"/api/{module}/{createdRecord.Id}", crmService.ToRecordDto(createdRecord));
    });

    [HttpPut(ModuleRoute + "/{id:guid}")]
    public Task<IActionResult> Update(string module, Guid id, RecordForm form) => RunAsync(async () =>
    {
        form.Id = id;
        return Ok(crmService.ToRecordDto(await crmService.SaveRecordAsync(module, form, id)));
    });

    [HttpDelete(ModuleRoute + "/{id:guid}")]
    public Task<IActionResult> Delete(string module, Guid id) => RunAsync(async () =>
    {
        await crmService.DeleteRecordAsync(module, id);
        return Ok(new { message = "Record deleted." });
    });

    [HttpPost("{module:regex(^(followups|activities)$)}/{id:guid}/complete")]
    public Task<IActionResult> Complete(string module, Guid id) => RunAsync(async () =>
    {
        await crmService.MarkCompletedAsync(module, id);
        return Ok(crmService.ToRecordDto(await crmService.FindRecordAsync(module, id)));
    });

    [HttpPost("leads/{id:guid}/convert")]
    public Task<IActionResult> Convert(Guid id) =>
        RunAsync(async () => Ok(new { customerId = await crmService.ConvertLeadAsync(id) }));

    [HttpGet("reports/pipeline")]
    public async Task<IActionResult> Pipeline([FromQuery] RecordFilter filter)
    {
        var opportunities = await crmService.GetRecordsAsync("opportunities", filter);
        return Ok(new
        {
            stages = opportunities.GroupBy(row => row.Stage).Select(group => new
            {
                stage = group.Key,
                count = group.Count(),
                amount = group.Sum(row => row.Amount),
                weighted = group.Sum(row => row.WeightedAmount)
            }),
            owners = opportunities.Where(row => row.Status == "Open").GroupBy(row => new { row.OwnerId, row.Owner }).Select(group => new
            {
                ownerId = group.Key.OwnerId,
                owner = group.Key.Owner,
                amount = group.Sum(row => row.Amount),
                weighted = group.Sum(row => row.WeightedAmount)
            })
        });
    }

    // Business rule failures become { error, errors: { Field: [message] } } with the right status code.
    async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BusinessRuleException error)
        {
            return StatusCode(error.StatusCode, new
            {
                error = error.Message,
                errors = new Dictionary<string, string[]> { [error.FieldName] = [error.Message] }
            });
        }
    }
}

[ApiController, Route("api/auth")]
public class AuthApiController(IAntiforgery antiforgery, UserManager<AppUser> userManager, LoginService loginService) : ControllerBase
{
    // Gives a script the anti-forgery token it must send in X-CSRF-TOKEN on write requests.
    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokenSet = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokenSet.RequestToken });
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginForm form)
    {
        var outcome = await loginService.SignInAsync(form.Email, form.Password);
        if (!outcome.Succeeded)
            return Unauthorized(new { error = outcome.IsLockedOut ? "Account temporarily locked." : "Invalid credentials or inactive account." });

        var user = outcome.User!;
        return Ok(new { id = user.Id, name = user.Name, roles = await userManager.GetRolesAsync(user) });
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await loginService.SignOutAsync(userManager.GetUserId(User)!);
        return Ok(new { message = "Logged out." });
    }
}
