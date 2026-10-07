using System.ComponentModel.DataAnnotations;
using System.Text;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;

namespace AcxiomCRM.Tests;

public class CatalogAndDisplayTests
{
    [Fact]
    public void Lead_statuses_match_the_assignment()
    {
        Assert.Equal(["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"], CrmCatalog.StatusesFor("leads"));
        Assert.Equal(["Planned", "Completed", "Missed", "Cancelled"], CrmCatalog.StatusesFor("followups"));
        Assert.Equal(["Qualification", "Proposal", "Negotiation", "Won", "Lost"], CrmCatalog.Stages);
        Assert.Equal(["Call", "Meeting", "Email", "Task"], CrmCatalog.ActivityTypes);
    }

    [Theory]
    [InlineData("New", new[] { "Contacted", "Lost" })]
    [InlineData("Contacted", new[] { "Qualified", "Unqualified", "Lost" })]
    [InlineData("Qualified", new[] { "Lost" })]
    [InlineData("Converted", new string[0])]
    public void Lead_workflow_steps(string currentStatus, string[] expectedNextStatuses)
    {
        Assert.Equal(expectedNextStatuses, CrmCatalog.NextLeadStatuses(currentStatus));
    }

    [Fact]
    public void Unknown_module_gives_404()
    {
        Assert.Equal(404, Assert.Throws<BusinessRuleException>(() => CrmCatalog.StatusesFor("payments")).StatusCode);
        Assert.False(CrmCatalog.IsValidModule("../users"));
    }

    [Theory]
    [InlineData(450000, "₹4,50,000")]
    [InlineData(12500000, "₹1,25,00,000")]
    [InlineData(0, "₹0")]
    public void Rupees_use_indian_grouping(decimal amount, string expectedText)
    {
        Assert.Equal(expectedText, DisplayHelpers.Rupees(amount));
    }

    [Theory]
    [InlineData("Rahul Verma", "RV")]
    [InlineData("neha", "N")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initials_are_safe_for_any_name(string? fullName, string expectedInitials)
    {
        Assert.Equal(expectedInitials, DisplayHelpers.Initials(fullName));
    }

    [Fact]
    public void Due_label_marks_overdue_today_and_tomorrow()
    {
        var today = CrmCatalog.TodayInIndia;
        Assert.StartsWith("Overdue", DisplayHelpers.DueLabel(today.AddDays(-1)));
        Assert.Equal("Today", DisplayHelpers.DueLabel(today));
        Assert.Equal("Tomorrow", DisplayHelpers.DueLabel(today.AddDays(1)));
    }

    [Fact]
    public void Pagination_clamps_to_valid_pages()
    {
        var numbers = Enumerable.Range(1, 25).ToList();

        Assert.Equal([21, 22, 23, 24, 25], CrmService.Paginate(numbers, 99).Items);
        Assert.Equal(1, CrmService.Paginate(numbers, -4).Page);
        Assert.Equal(3, CrmService.Paginate(numbers, 1).TotalPages);
        Assert.Equal(1, CrmService.Paginate(new List<int>(), 1).TotalPages);
    }
}

public class FormValidationTests
{
    static List<ValidationResult> Validate(object form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("5876543210")]
    [InlineData("98765abcde")]
    [InlineData("987654321012")]
    public void Phone_must_be_a_10_digit_indian_mobile(string phoneNumber)
    {
        var form = TestWorkspace.CustomerForm();
        form.Phone = phoneNumber;

        Assert.Contains(Validate(form), result => result.ErrorMessage == "Enter a valid 10-digit Indian mobile number.");
    }

    [Fact]
    public void Email_format_message_matches_the_assignment()
    {
        var form = TestWorkspace.CustomerForm();
        form.Email = "not-an-email";

        Assert.Contains(Validate(form), result => result.ErrorMessage == "Enter a valid email address.");
    }

    [Fact]
    public void Required_and_length_limits_are_enforced()
    {
        var form = TestWorkspace.CustomerForm();
        form.Name = "";
        form.Notes = new string('x', 2001);

        var errorFields = Validate(form).SelectMany(result => result.MemberNames).ToList();

        Assert.Contains("Name", errorFields);
        Assert.Contains("Notes", errorFields);
    }

    [Fact]
    public void Valid_customer_form_has_no_errors()
    {
        Assert.Empty(Validate(TestWorkspace.CustomerForm()));
    }

    [Fact]
    public void Probability_range_message_matches_the_assignment()
    {
        var form = TestWorkspace.OpportunityForm(Guid.NewGuid());
        form.Probability = 101;

        Assert.Contains(Validate(form), result => result.ErrorMessage == "Probability must be between 0 and 100.");
    }

    [Fact]
    public void Dashboard_custom_range_needs_both_dates_in_order()
    {
        var today = CrmCatalog.TodayInIndia;
        Assert.NotEmpty(Validate(new DashboardFilter { Period = "custom" }));
        Assert.NotEmpty(Validate(new DashboardFilter { Period = "custom", From = today, To = today.AddDays(-1) }));
        Assert.NotEmpty(Validate(new DashboardFilter { Period = "yearly" }));
        Assert.Empty(Validate(new DashboardFilter { Period = "custom", From = today.AddDays(-7), To = today }));
    }
}

public class DashboardFilterTests
{
    [Fact]
    public void All_time_has_no_date_limits()
    {
        var filter = new DashboardFilter();
        filter.ResolveDates();

        Assert.Equal("all", filter.Period);
        Assert.Null(filter.From);
        Assert.Null(filter.To);
    }

    [Fact]
    public void Week_starts_on_monday_and_month_on_the_first()
    {
        var today = CrmCatalog.TodayInIndia;
        var weekFilter = new DashboardFilter { Period = "week" };
        var monthFilter = new DashboardFilter { Period = "month" };
        weekFilter.ResolveDates();
        monthFilter.ResolveDates();

        Assert.Equal(DayOfWeek.Monday, weekFilter.From!.Value.DayOfWeek);
        Assert.True(today.DayNumber - weekFilter.From.Value.DayNumber is >= 0 and < 7);
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), monthFilter.From);
        Assert.Equal(today, monthFilter.To);
    }
}

public class ReportTests : IDisposable
{
    readonly TestWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void Csv_export_neutralises_spreadsheet_formulas_and_quotes()
    {
        var report = new ReportTable("customers", "Customers", ["Name", "Notes"], [["=HYPERLINK(\"x\")", "He said \"hi\""]]);

        var csvText = Encoding.UTF8.GetString(ReportService.ToCsv(report)).TrimStart('﻿');

        Assert.Equal("\"Name\",\"Notes\"\r\n\"'=HYPERLINK(\"\"x\"\")\",\"He said \"\"hi\"\"\"", csvText);
    }

    [Fact]
    public void Report_access_follows_the_role_matrix()
    {
        Assert.True(ReportService.CanOpen("audit", isAdmin: true, isSalesExecutive: false));
        Assert.False(ReportService.CanOpen("audit", isAdmin: false, isSalesExecutive: false));
        Assert.True(ReportService.CanOpen("user-activity", isAdmin: false, isSalesExecutive: false));
        Assert.False(ReportService.CanOpen("user-activity", isAdmin: false, isSalesExecutive: true));
        Assert.True(ReportService.CanOpen("pipeline", isAdmin: false, isSalesExecutive: true));
    }

    [Fact]
    public async Task Pipeline_report_is_stage_wise_owner_wise_and_weighted()
    {
        var rahul = workspace.Rahul();
        var customerId = (await rahul.SaveRecordAsync("customers", TestWorkspace.CustomerForm())).Id;
        await rahul.SaveRecordAsync("opportunities", TestWorkspace.OpportunityForm(customerId));
        var (managerCrm, managerDb) = workspace.SignInAs(TestWorkspace.ManagerId, CrmCatalog.ManagerRole);

        var report = await new ReportService(managerDb, managerCrm).BuildAsync("pipeline", new RecordFilter(), isAdmin: false);

        var proposalRow = report.Rows.Single(row => row[0] == "Stage-wise" && row[1] == "Proposal");
        var rahulRow = report.Rows.Single(row => row[0] == "Owner-wise" && row[2] == "Rahul Verma");
        Assert.Equal(["Stage-wise", "Proposal", "All owners", "1", "₹2,80,000", "₹1,40,000"], proposalRow);
        Assert.Equal("₹1,40,000", rahulRow[5]);
        Assert.Equal("Total", report.Rows.Last()[0]);
    }

    [Fact]
    public async Task Lead_report_shows_conversion_information()
    {
        var rahul = workspace.Rahul();
        var form = TestWorkspace.LeadForm();
        var leadId = (await rahul.SaveRecordAsync("leads", form)).Id;
        foreach (var nextStatus in new[] { "Contacted", "Qualified" })
        {
            form.Status = nextStatus;
            await rahul.SaveRecordAsync("leads", form, leadId);
        }
        await rahul.ConvertLeadAsync(leadId);
        var (rahulCrm, rahulDb) = workspace.SignInAs(TestWorkspace.RahulId, CrmCatalog.SalesExecutiveRole);

        var report = await new ReportService(rahulDb, rahulCrm).BuildAsync("leads", new RecordFilter(), isAdmin: false);

        Assert.Contains("Converted to customer", report.Headings);
        Assert.Equal("Yes", report.Rows.Single()[7]);
        Assert.Equal("Sneha Joshi", report.Rows.Single()[8]);
    }
}
