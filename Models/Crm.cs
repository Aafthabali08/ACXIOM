using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AcxiomCRM.Models;

// Login user. Password hashing, lockout and security stamp come from ASP.NET Core Identity.
public class AppUser : IdentityUser
{
    [MaxLength(80)] public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Common columns shared by every CRM module (customer, lead, opportunity, follow-up, activity).
// Each module still gets its own table because of the TPC mapping in ApplicationDbContext.
public abstract class CrmRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "";
    [MaxLength(2000)] public string Notes { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public AppUser? Owner { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool Deleted { get; set; }
}

public class Customer : CrmRecord
{
    public string Code { get; set; } = "";
    [MaxLength(160)] public string Email { get; set; } = "";
    [MaxLength(10)] public string Phone { get; set; } = "";
    [MaxLength(120)] public string Company { get; set; } = "";
    [MaxLength(300)] public string Address { get; set; } = "";
    [MaxLength(80)] public string City { get; set; } = "";
    [MaxLength(80)] public string State { get; set; } = "";
}

public class Lead : CrmRecord
{
    public string Code { get; set; } = "";
    [MaxLength(160)] public string Email { get; set; } = "";
    [MaxLength(10)] public string Phone { get; set; } = "";
    [MaxLength(120)] public string Company { get; set; } = "";
    [MaxLength(80)] public string Source { get; set; } = "";
    [MaxLength(20)] public string Priority { get; set; } = "Normal";
    public decimal ExpectedValue { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}

public class Opportunity : CrmRecord
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? LeadId { get; set; }
    public Lead? Lead { get; set; }
    [MaxLength(30)] public string Stage { get; set; } = "Qualification";
    public decimal Amount { get; set; }
    public int Probability { get; set; }
    public DateOnly CloseDate { get; set; }
    public DateOnly? OutcomeDate { get; set; }
    [MaxLength(80)] public string Source { get; set; } = "";
}

public class FollowUp : CrmRecord
{
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public Guid? OpportunityId { get; set; }
    public Opportunity? Opportunity { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(20)] public string Type { get; set; } = "Call";
}

public class Activity : CrmRecord
{
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(20)] public string Type { get; set; } = "Call";
}

// Append-only history. ApplicationDbContext refuses updates or deletes on this table.
public class AuditLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Result { get; set; } = "Success";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Ip { get; set; } = "";
}
