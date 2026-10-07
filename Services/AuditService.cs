using System.Security.Claims;
using System.Text.Json;
using AcxiomCRM.Data;
using AcxiomCRM.Models;

namespace AcxiomCRM.Services;

public class AuditService(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor)
{
    // Adds an entry to the current unit of work. It is saved together with the business change,
    // so a record is never changed without its history (and the other way round).
    public void AddEntry(string action, string entityName, string recordId, object? oldValue = null, object? newValue = null,
        string result = "Success", string? userId = null)
    {
        var httpContext = httpContextAccessor.HttpContext;
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId ?? httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Action = action,
            Entity = entityName,
            RecordId = recordId,
            Result = result,
            OldValue = oldValue == null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = newValue == null ? null : JsonSerializer.Serialize(newValue),
            Ip = httpContext?.Connection.RemoteIpAddress?.ToString() ?? ""
        });
    }

    // For events that have no other change to save with them (login, logout, unlock...).
    public async Task WriteEntryAsync(string action, string entityName, string recordId, string result = "Success", string? userId = null)
    {
        AddEntry(action, entityName, recordId, result: result, userId: userId);
        await dbContext.SaveChangesAsync();
    }
}
