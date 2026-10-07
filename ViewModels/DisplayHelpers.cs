using System.Globalization;
using AcxiomCRM.Services;

namespace AcxiomCRM.ViewModels;

// Small formatting helpers shared by the Razor views.
public static class DisplayHelpers
{
    static readonly CultureInfo IndianCulture = new("en-IN");

    // ₹4,50,000 style (lakh grouping).
    public static string Rupees(decimal amount) => "₹" + amount.ToString("N0", IndianCulture);

    public static string StatusBadgeClass(string? status) => status switch
    {
        "Won" or "Completed" or "Active" or "Converted" or "Qualified" => "positive",
        "Lost" or "Missed" or "Inactive" or "Cancelled" or "Unqualified" or "Overdue" or "Failed" => "negative",
        "Open" or "Planned" or "Pending" or "New" => "strong",
        _ => ""
    };

    public static string DueLabel(DateOnly? dueDate)
    {
        if (dueDate == null)
            return "";
        var today = CrmCatalog.TodayInIndia;
        if (dueDate < today)
            return "Overdue · " + dueDate.Value.ToString("dd MMM");
        if (dueDate == today)
            return "Today";
        if (dueDate == today.AddDays(1))
            return "Tomorrow";
        return dueDate.Value.ToString("dd MMM");
    }

    public static string TypeIcon(string? activityType) => activityType switch
    {
        "Call" => "i-phone",
        "Email" => "i-mail",
        "Meeting" => "i-calendar",
        _ => "i-check"
    };

    public static string Greeting()
    {
        var indiaHour = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Kolkata").Hour;
        return indiaHour < 12 ? "Good morning" : indiaHour < 17 ? "Good afternoon" : "Good evening";
    }

    // "Rahul Verma" → "RV" for the round avatar. Safe for empty names.
    public static string Initials(string? fullName)
    {
        var nameParts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return nameParts.Length == 0 ? "?" : string.Concat(nameParts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    public static string FirstName(string fullName) => fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
}
