using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Tests;

// Business rules from the assignment PDF (sections 4, 5.3, 5.4, 8 and 17.7), tested directly on CrmService.
public class CrmServiceTests : IDisposable
{
    readonly TestWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    async Task<Guid> CreateCustomerForRahulAsync() =>
        (await workspace.Rahul().SaveRecordAsync("customers", TestWorkspace.CustomerForm())).Id;

    static async Task<BusinessRuleException> ExpectRuleError(Func<Task> action) =>
        await Assert.ThrowsAsync<BusinessRuleException>(action);

    // ---------- Opportunity (PDF 4.8, 5.3, 5.4) ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Opportunity_amount_must_be_greater_than_zero(decimal amount)
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.OpportunityForm(customerId);
        form.Amount = amount;

        var error = await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("opportunities", form));

        Assert.Equal("Amount", error.FieldName);
        Assert.Equal("Opportunity Amount must be greater than 0.", error.Message);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task Probability_must_be_between_0_and_100(int probability)
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.OpportunityForm(customerId);
        form.Probability = probability;

        var error = await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("opportunities", form));

        Assert.Equal("Probability must be between 0 and 100.", error.Message);
    }

    [Fact]
    public async Task Open_opportunity_cannot_close_in_the_past()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.OpportunityForm(customerId);
        form.CloseDate = TestWorkspace.Today.AddDays(-1);

        var error = await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("opportunities", form));

        Assert.Equal("Expected Close Date cannot be in the past.", error.Message);
    }

    [Fact]
    public async Task Won_opportunity_may_have_a_past_close_date_and_records_outcome_date()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.OpportunityForm(customerId);
        form.Stage = "Won";
        form.Status = "Won";
        form.Probability = 100;
        form.CloseDate = TestWorkspace.Today.AddDays(-5);

        var savedDeal = (Opportunity)await workspace.Rahul().SaveRecordAsync("opportunities", form);

        Assert.Equal(TestWorkspace.Today, savedDeal.OutcomeDate);
    }

    [Theory]
    [InlineData("Won", "Won", 80)]
    [InlineData("Lost", "Lost", 10)]
    [InlineData("Won", "Open", 100)]
    public async Task Stage_status_and_probability_must_agree(string stage, string status, int probability)
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.OpportunityForm(customerId);
        form.Stage = stage;
        form.Status = status;
        form.Probability = probability;

        await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("opportunities", form));
    }

    [Fact]
    public void Weighted_amount_is_amount_times_probability_over_100()
    {
        var deal = new ViewModels.RecordDto(Guid.NewGuid(), "opportunities", "Deal", "Open", "", "", DateTime.UtcNow, DateTime.UtcNow, "",
            null, null, null, null, null, null, "", null, null, 0, 450000, 25, "Qualification", null, null, null, null, null, null, null, null);

        Assert.Equal(112500, deal.WeightedAmount);
    }

    // ---------- Follow-ups (PDF 4.6, 5.3, 8) ----------

    [Fact]
    public async Task New_follow_up_cannot_be_dated_before_today()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.FollowUpForm(customerId);
        form.DueDate = TestWorkspace.Today.AddDays(-1);

        var error = await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("followups", form));

        Assert.Equal("Follow-up date cannot be earlier than today.", error.Message);
    }

    [Fact]
    public async Task Follow_up_needs_exactly_one_related_record()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var leadId = (await workspace.Rahul().SaveRecordAsync("leads", TestWorkspace.LeadForm())).Id;

        await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("followups", TestWorkspace.FollowUpForm()));
        await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("followups", TestWorkspace.FollowUpForm(customerId, leadId)));
    }

    [Fact]
    public async Task Missed_is_only_allowed_for_an_overdue_follow_up()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.FollowUpForm(customerId);
        form.Status = "Missed";

        var error = await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("followups", form));

        Assert.Equal("Status", error.FieldName);
    }

    [Fact]
    public async Task Completing_a_follow_up_moves_a_new_lead_to_contacted_and_is_audited()
    {
        var leadId = (await workspace.Rahul().SaveRecordAsync("leads", TestWorkspace.LeadForm())).Id;
        var followUpId = (await workspace.Rahul().SaveRecordAsync("followups", TestWorkspace.FollowUpForm(leadId: leadId))).Id;

        await workspace.Rahul().MarkCompletedAsync("followups", followUpId);

        using var dbContext = workspace.NewDbContext();
        Assert.Equal("Completed", (await dbContext.FollowUps.SingleAsync(followUp => followUp.Id == followUpId)).Status);
        Assert.Equal("Contacted", (await dbContext.Leads.SingleAsync(lead => lead.Id == leadId)).Status);
        Assert.True(await dbContext.AuditLogs.AnyAsync(log => log.Action == "Status Change" && log.RecordId == followUpId.ToString()));
        Assert.True(await dbContext.AuditLogs.AnyAsync(log => log.Action == "Status Change" && log.RecordId == leadId.ToString()));
    }

    [Fact]
    public async Task A_completed_follow_up_cannot_be_completed_again()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var followUpId = (await workspace.Rahul().SaveRecordAsync("followups", TestWorkspace.FollowUpForm(customerId))).Id;
        await workspace.Rahul().MarkCompletedAsync("followups", followUpId);

        var error = await ExpectRuleError(() => workspace.Rahul().MarkCompletedAsync("followups", followUpId));

        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Rescheduling_a_follow_up_is_audited()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var form = TestWorkspace.FollowUpForm(customerId);
        var followUpId = (await workspace.Rahul().SaveRecordAsync("followups", form)).Id;

        form.DueDate = TestWorkspace.Today.AddDays(7);
        await workspace.Rahul().SaveRecordAsync("followups", form, followUpId);

        using var dbContext = workspace.NewDbContext();
        Assert.True(await dbContext.AuditLogs.AnyAsync(log => log.Action == "Reschedule" && log.RecordId == followUpId.ToString()));
    }

    // ---------- Customers (PDF 4.4, 17.5) ----------

    [Fact]
    public async Task Customer_email_and_phone_must_be_unique()
    {
        var firstForm = TestWorkspace.CustomerForm();
        await workspace.Rahul().SaveRecordAsync("customers", firstForm);

        var sameEmail = TestWorkspace.CustomerForm();
        sameEmail.Email = firstForm.Email!.ToUpperInvariant();
        var samePhone = TestWorkspace.CustomerForm();
        samePhone.Phone = firstForm.Phone;

        Assert.Equal(409, (await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("customers", sameEmail))).StatusCode);
        Assert.Equal(409, (await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("customers", samePhone))).StatusCode);
    }

    [Fact]
    public async Task Customer_requires_email_and_phone_and_gets_a_code()
    {
        var withoutEmail = TestWorkspace.CustomerForm();
        withoutEmail.Email = " ";
        await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("customers", withoutEmail));

        var savedCustomer = (Customer)await workspace.Rahul().SaveRecordAsync("customers", TestWorkspace.CustomerForm());
        Assert.StartsWith("CUS-", savedCustomer.Code);
    }

    // ---------- Leads (PDF 4.5, 17.6) ----------

    [Theory]
    [InlineData("New", "Qualified", false)]
    [InlineData("New", "Converted", false)]
    [InlineData("New", "Contacted", true)]
    [InlineData("New", "Lost", true)]
    public async Task Lead_status_follows_the_workflow(string startStatus, string nextStatus, bool allowed)
    {
        var form = TestWorkspace.LeadForm();
        form.Status = startStatus;
        var leadId = (await workspace.Rahul().SaveRecordAsync("leads", form)).Id;

        form.Status = nextStatus;
        var saveAttempt = () => workspace.Rahul().SaveRecordAsync("leads", form, leadId);

        if (allowed)
            Assert.Equal(nextStatus, (await saveAttempt()).Status);
        else
            await ExpectRuleError(saveAttempt);
    }

    [Fact]
    public async Task New_lead_must_start_as_new_and_status_must_be_valid()
    {
        var qualifiedForm = TestWorkspace.LeadForm();
        qualifiedForm.Status = "Qualified";
        var unknownForm = TestWorkspace.LeadForm();
        unknownForm.Status = "Hot";

        await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("leads", qualifiedForm));
        await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("leads", unknownForm));
    }

    [Fact]
    public async Task Qualified_lead_converts_to_customer_and_opportunity_once()
    {
        var form = TestWorkspace.LeadForm();
        var leadId = (await workspace.Rahul().SaveRecordAsync("leads", form)).Id;
        Assert.Equal(409, (await ExpectRuleError(() => workspace.Rahul().ConvertLeadAsync(leadId))).StatusCode);

        form.Status = "Contacted";
        await workspace.Rahul().SaveRecordAsync("leads", form, leadId);
        form.Status = "Qualified";
        await workspace.Rahul().SaveRecordAsync("leads", form, leadId);

        var customerId = await workspace.Rahul().ConvertLeadAsync(leadId);

        using var dbContext = workspace.NewDbContext();
        var convertedLead = await dbContext.Leads.SingleAsync(lead => lead.Id == leadId);
        var newDeal = await dbContext.Opportunities.SingleAsync(opportunity => opportunity.LeadId == leadId);
        Assert.Equal("Converted", convertedLead.Status);
        Assert.Equal(customerId, convertedLead.CustomerId);
        Assert.Equal(250000, newDeal.Amount);
        Assert.Equal(customerId, newDeal.CustomerId);
        Assert.True(await dbContext.AuditLogs.AnyAsync(log => log.Action == "Convert" && log.RecordId == leadId.ToString()));
        await ExpectRuleError(() => workspace.Rahul().ConvertLeadAsync(leadId));
    }

    // ---------- Authorization and ownership (PDF 7, 17.10) ----------

    [Fact]
    public async Task Sales_executive_cannot_see_another_executives_record()
    {
        var customerId = await CreateCustomerForRahulAsync();

        var error = await ExpectRuleError(() => workspace.Neha().FindRecordAsync("customers", customerId));

        Assert.Equal(404, error.StatusCode);
        Assert.Single(await workspace.Rahul().GetRecordsAsync("customers", new ViewModels.RecordFilter()));
        Assert.Empty(await workspace.Neha().GetRecordsAsync("customers", new ViewModels.RecordFilter()));
        Assert.Single(await workspace.Manager().GetRecordsAsync("customers", new ViewModels.RecordFilter()));
    }

    [Fact]
    public async Task Sales_executive_can_only_assign_records_to_themselves()
    {
        var error = await ExpectRuleError(() => workspace.Rahul().SaveRecordAsync("customers", TestWorkspace.CustomerForm(TestWorkspace.NehaId)));

        Assert.Equal(403, error.StatusCode);
    }

    [Theory]
    [InlineData(TestWorkspace.AdminId)]
    [InlineData(TestWorkspace.ManagerId)]
    [InlineData("missing-user")]
    public async Task Records_are_assigned_only_to_active_sales_executives(string ownerId)
    {
        var error = await ExpectRuleError(() => workspace.Manager().SaveRecordAsync("customers", TestWorkspace.CustomerForm(ownerId)));

        Assert.Equal("Assign the record to an active Sales Executive.", error.Message);
    }

    [Fact]
    public async Task Manager_can_assign_to_any_sales_executive_and_list_only_sales_executives()
    {
        var savedCustomer = await workspace.Manager().SaveRecordAsync("customers", TestWorkspace.CustomerForm(TestWorkspace.NehaId));
        var assignableNames = (await workspace.Manager().GetAssignableUsersAsync()).Select(user => user.Name).ToList();

        Assert.Equal(TestWorkspace.NehaId, savedCustomer.OwnerId);
        Assert.Equal(["Neha Iyer", "Rahul Verma"], assignableNames);
    }

    [Fact]
    public async Task Related_record_must_belong_to_the_same_owner()
    {
        var rahulCustomerId = await CreateCustomerForRahulAsync();

        var error = await ExpectRuleError(() => workspace.Manager().SaveRecordAsync("opportunities", TestWorkspace.OpportunityForm(rahulCustomerId, TestWorkspace.NehaId)));

        Assert.Equal("CustomerId", error.FieldName);
    }

    // ---------- Delete and audit (PDF 4.9, 17.16) ----------

    [Fact]
    public async Task Delete_is_soft_and_blocked_while_linked_records_exist()
    {
        var customerId = await CreateCustomerForRahulAsync();
        var followUpId = (await workspace.Rahul().SaveRecordAsync("followups", TestWorkspace.FollowUpForm(customerId))).Id;

        Assert.Equal(409, (await ExpectRuleError(() => workspace.Rahul().DeleteRecordAsync("customers", customerId))).StatusCode);

        await workspace.Rahul().DeleteRecordAsync("followups", followUpId);
        await workspace.Rahul().DeleteRecordAsync("customers", customerId);

        using var dbContext = workspace.NewDbContext();
        Assert.True((await dbContext.Customers.SingleAsync(customer => customer.Id == customerId)).Deleted);
        Assert.Equal(404, (await ExpectRuleError(() => workspace.Rahul().FindRecordAsync("customers", customerId))).StatusCode);
        Assert.True(await dbContext.AuditLogs.AnyAsync(log => log.Action == "Delete" && log.RecordId == customerId.ToString()));
    }

    [Fact]
    public async Task Create_and_update_write_old_and_new_values_to_the_audit_log()
    {
        var form = TestWorkspace.CustomerForm();
        var customerId = (await workspace.Rahul().SaveRecordAsync("customers", form)).Id;
        form.Name = "Karthik Rao (updated)";
        await workspace.Rahul().SaveRecordAsync("customers", form, customerId);

        using var dbContext = workspace.NewDbContext();
        var updateEntry = await dbContext.AuditLogs.SingleAsync(log => log.Action == "Update" && log.RecordId == customerId.ToString());
        Assert.Equal(TestWorkspace.RahulId, updateEntry.UserId);
        Assert.Contains("Karthik Rao", updateEntry.OldValue);
        Assert.Contains("Karthik Rao (updated)", updateEntry.NewValue);
        Assert.True(await dbContext.AuditLogs.AnyAsync(log => log.Action == "Create" && log.RecordId == customerId.ToString()));
    }

    [Fact]
    public async Task Audit_entries_cannot_be_changed_or_deleted()
    {
        await CreateCustomerForRahulAsync();
        using var dbContext = workspace.NewDbContext();
        var auditEntry = await dbContext.AuditLogs.FirstAsync();

        auditEntry.Action = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.AuditLogs.Remove(await dbContext.AuditLogs.FirstAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    // ---------- Search (PDF 17.13) ----------

    [Fact]
    public async Task Search_matches_name_email_phone_and_company_case_insensitively()
    {
        var form = TestWorkspace.CustomerForm();
        form.Company = "Nirmaan Industries";
        await workspace.Rahul().SaveRecordAsync("customers", form);

        foreach (var searchText in new[] { "karthik", form.Email!.ToUpperInvariant(), form.Phone!, "nirmaan" })
            Assert.Single(await workspace.Rahul().GetRecordsAsync("customers", new ViewModels.RecordFilter { Search = searchText }));
        Assert.Empty(await workspace.Rahul().GetRecordsAsync("customers", new ViewModels.RecordFilter { Search = "nobody" }));
    }
}
