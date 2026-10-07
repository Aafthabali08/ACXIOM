using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.ViewModels;

public class LoginForm
{
    [Required, Display(Name = "Email or username")] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}

public class RegisterForm
{
    [Required, StringLength(80), Display(Name = "Full name")] public string Name { get; set; } = "";
    [Required, EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(160)] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password), ErrorMessage = "Both passwords must match."), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string Confirm { get; set; } = "";
}

public class ProfileForm
{
    [Required, StringLength(80), Display(Name = "Full name")] public string Name { get; set; } = "";
}

public class ChangePasswordForm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword), ErrorMessage = "Both passwords must match."), DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public class ProfilePageViewModel
{
    public ProfileForm Details { get; set; } = new();
    public ChangePasswordForm Password { get; set; } = new();
    public string SavedName { get; set; } = "";
    public string Email { get; set; } = "";
    public string RoleTitle { get; set; } = "";
    public DateTime MemberSince { get; set; }
    public DateTime? LastLogin { get; set; }
}

// One form model for all five CRM modules. Only the fields relevant to the module are shown,
// and CrmService.SaveRecordAsync checks the module specific rules on the server.
public class RecordForm
{
    public Guid Id { get; set; }
    public string Module { get; set; } = "customers";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(160)] public string? Email { get; set; }
    [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Enter a valid 10-digit Indian mobile number.")]
    public string? Phone { get; set; }
    [StringLength(120)] public string? Company { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [StringLength(80)] public string? State { get; set; }
    [Required, StringLength(30)] public string Status { get; set; } = "";
    [Required, Display(Name = "Assigned to")] public string OwnerId { get; set; } = "";
    [StringLength(2000)] public string? Notes { get; set; }
    [StringLength(80)] public string? Source { get; set; }
    public string Priority { get; set; } = "Normal";
    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Expected value must be between 0 and 99,99,99,999.")]
    [Display(Name = "Expected value")] public decimal ExpectedValue { get; set; }
    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Opportunity Amount must be greater than 0 and at most 99,99,99,999.")]
    public decimal Amount { get; set; }
    [Range(0, 100, ErrorMessage = "Probability must be between 0 and 100.")] public int Probability { get; set; }
    public string Stage { get; set; } = "Qualification";
    [DataType(DataType.Date), Display(Name = "Expected close date")] public DateOnly? CloseDate { get; set; }
    [DataType(DataType.Date), Display(Name = "Date")] public DateOnly? DueDate { get; set; }
    public string Type { get; set; } = "Call";
    public Guid? CustomerId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? OpportunityId { get; set; }
}

public class UserForm
{
    public string? Id { get; set; }
    [Required, StringLength(80), Display(Name = "Full name")] public string Name { get; set; } = "";
    [Required, EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(160)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = "SalesExecutive";
    public bool Active { get; set; } = true;
    [StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string? Password { get; set; }
}

// Query string filter used by lists, reports and the API. "q" is the search box.
public class RecordFilter : IValidatableObject
{
    [ModelBinder(Name = "q")] public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Owner { get; set; }
    public string? Type { get; set; }
    public string? Stage { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string Sort { get; set; } = "newest";
    public int Page { get; set; } = 1;
    public string? Result { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From > To)
            yield return new ValidationResult("From date cannot be after To date.");
    }
}

// Safe output shape for screens and the API. It never carries Identity or password fields.
public record RecordDto(Guid Id, string Module, string Name, string Status, string OwnerId, string Owner,
    DateTime CreatedAt, DateTime UpdatedAt, string Code, string? Email, string? Phone, string? Company,
    string? Address, string? City, string? State, string Notes, string? Source, string? Priority,
    decimal ExpectedValue, decimal Amount, int Probability, string? Stage, DateOnly? CloseDate,
    DateOnly? OutcomeDate, DateOnly? DueDate, string? Type, Guid? CustomerId, Guid? LeadId, Guid? OpportunityId, string? RelatedTo)
{
    public decimal WeightedAmount => Amount * Probability / 100;
    public bool IsOverdue => Module == "followups" && Status == "Planned" && DueDate < Services.CrmCatalog.TodayInIndia;
}

public record PagedResult<T>(List<T> Items, int Total, int Page, int Size)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)Size));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
