using System.Security.Claims;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

// All CRM reads and writes go through this class. The MVC screens and the JSON API both call it,
// so ownership checks and business rules are written only once.
public class CrmService(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor, AuditService auditService)
{
    ClaimsPrincipal CurrentUser => httpContextAccessor.HttpContext!.User;
    public string CurrentUserId => CurrentUser.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public bool IsSalesExecutive => CurrentUser.IsInRole(CrmCatalog.SalesExecutiveRole);

    // ---------- Reading ----------

    // Base query for a module with the role filter already applied.
    // A sales executive only ever gets rows assigned to them; managers and admins see the whole team.
    public IQueryable<CrmRecord> GetScopedQuery(string moduleName)
    {
        IQueryable<CrmRecord> query = moduleName switch
        {
            "customers" => dbContext.Customers,
            "leads" => dbContext.Leads.Include(lead => lead.Customer),
            "opportunities" => dbContext.Opportunities.Include(opportunity => opportunity.Customer),
            "followups" => dbContext.FollowUps.Include(followUp => followUp.Customer).Include(followUp => followUp.Lead).Include(followUp => followUp.Opportunity),
            "activities" => dbContext.Activities.Include(activity => activity.Customer).Include(activity => activity.Lead),
            _ => throw new BusinessRuleException("", "Page not found.", 404)
        };
        query = query.Include(record => record.Owner).Where(record => !record.Deleted);
        return IsSalesExecutive ? query.Where(record => record.OwnerId == CurrentUserId) : query;
    }

    // Missing and "not yours" both give 404, so nobody can probe for other people's record IDs.
    public async Task<CrmRecord> FindRecordAsync(string moduleName, Guid recordId) =>
        await GetScopedQuery(moduleName).FirstOrDefaultAsync(record => record.Id == recordId)
        ?? throw new BusinessRuleException("", "Record not found or not assigned to you.", 404);

    // IDs of everyone holding the Sales Executive role (as a query, so it runs inside SQL).
    IQueryable<string> SalesExecutiveIds() =>
        from userRole in dbContext.UserRoles
        join role in dbContext.Roles on userRole.RoleId equals role.Id
        where role.Name == CrmCatalog.SalesExecutiveRole
        select userRole.UserId;

    // CRM records are assigned to a Sales Executive (assignment PDF 4.4 / 4.5).
    // A Sales Executive can only pick themselves; managers and admins pick any active Sales Executive.
    public async Task<List<AppUser>> GetAssignableUsersAsync()
    {
        var salesExecutiveIds = SalesExecutiveIds();
        return await dbContext.Users
            .Where(user => user.Active && salesExecutiveIds.Contains(user.Id) && (!IsSalesExecutive || user.Id == CurrentUserId))
            .OrderBy(user => user.Name)
            .ToListAsync();
    }

    // People to filter reports by. The audit and activity reports also need admins and managers.
    public async Task<List<AppUser>> GetReportUsersAsync() =>
        await dbContext.Users
            .Where(user => !IsSalesExecutive || user.Id == CurrentUserId)
            .OrderBy(user => user.Name)
            .ToListAsync();

    // Reminder badge in the header: planned follow-ups due today or already overdue.
    public async Task<int> CountDueFollowUpsAsync()
    {
        var today = CrmCatalog.TodayInIndia;
        var query = dbContext.FollowUps.Where(followUp => !followUp.Deleted && followUp.Status == "Planned" && followUp.DueDate <= today);
        if (IsSalesExecutive)
            query = query.Where(followUp => followUp.OwnerId == CurrentUserId);
        return await query.CountAsync();
    }

    public async Task<List<RecordDto>> GetRecordsAsync(string moduleName, RecordFilter filter)
    {
        var query = GetScopedQuery(moduleName);
        var wantsOverdue = moduleName == "followups" && filter.Status == "Overdue";

        // Status and owner go to SQL. "Overdue" is not stored, it means Planned with a past date.
        if (!string.IsNullOrEmpty(filter.Status))
        {
            var statusInDatabase = wantsOverdue ? "Planned" : filter.Status;
            query = query.Where(record => record.Status == statusInDatabase);
        }
        if (!string.IsNullOrEmpty(filter.Owner))
            query = query.Where(record => record.OwnerId == filter.Owner);

        var rows = (await query.AsNoTracking().ToListAsync()).Select(ToRecordDto);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var searchText = filter.Search.Trim();
            rows = rows.Where(row => string.Join(" ", row.Name, row.Email, row.Phone, row.Company, row.RelatedTo, row.Code, row.Owner, row.Status)
                .Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }
        if (wantsOverdue)
            rows = rows.Where(row => row.DueDate < CrmCatalog.TodayInIndia);
        if (!string.IsNullOrEmpty(filter.Type))
            rows = rows.Where(row => row.Type == filter.Type);
        if (!string.IsNullOrEmpty(filter.Stage))
            rows = rows.Where(row => row.Stage == filter.Stage);

        // Follow-ups and activities filter on their own date; other modules on the created date.
        if (filter.From.HasValue)
            rows = rows.Where(row => (row.DueDate ?? CrmCatalog.ToIndiaDate(row.CreatedAt)) >= filter.From);
        if (filter.To.HasValue)
            rows = rows.Where(row => (row.DueDate ?? CrmCatalog.ToIndiaDate(row.CreatedAt)) <= filter.To);

        rows = filter.Sort switch
        {
            "name" => rows.OrderBy(row => row.Name),
            "oldest" => rows.OrderBy(row => row.CreatedAt),
            "amount" => rows.OrderByDescending(row => row.Amount),
            "date" => rows.OrderBy(row => row.DueDate ?? row.CloseDate),
            _ => rows.OrderByDescending(row => row.CreatedAt)
        };
        return rows.ToList();
    }

    public static PagedResult<T> Paginate<T>(List<T> rows, int pageNumber, int pageSize = 10)
    {
        var lastPage = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, lastPage);
        return new(rows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), rows.Count, pageNumber, pageSize);
    }

    // ---------- Mapping ----------

    public RecordDto ToRecordDto(CrmRecord record)
    {
        var customer = record as Customer;
        var lead = record as Lead;
        var opportunity = record as Opportunity;
        var followUp = record as FollowUp;
        var activity = record as Activity;
        var moduleName = customer != null ? "customers" : lead != null ? "leads" : opportunity != null ? "opportunities" : followUp != null ? "followups" : "activities";

        var relatedName = opportunity?.Customer?.Name
            ?? followUp?.Customer?.Name ?? followUp?.Lead?.Name ?? followUp?.Opportunity?.Name
            ?? activity?.Customer?.Name ?? activity?.Lead?.Name
            ?? lead?.Customer?.Name;   // for a lead: the customer it was converted into

        return new RecordDto(record.Id, moduleName, record.Name, record.Status, record.OwnerId, record.Owner?.Name ?? "",
            record.CreatedAt, record.UpdatedAt,
            customer?.Code ?? lead?.Code ?? "",
            customer?.Email ?? lead?.Email, customer?.Phone ?? lead?.Phone, customer?.Company ?? lead?.Company,
            customer?.Address, customer?.City, customer?.State, record.Notes,
            lead?.Source ?? opportunity?.Source, lead?.Priority, lead?.ExpectedValue ?? 0,
            opportunity?.Amount ?? 0, opportunity?.Probability ?? 0, opportunity?.Stage, opportunity?.CloseDate, opportunity?.OutcomeDate,
            followUp?.DueDate ?? activity?.DueDate, followUp?.Type ?? activity?.Type,
            lead?.CustomerId ?? opportunity?.CustomerId ?? followUp?.CustomerId ?? activity?.CustomerId,
            opportunity?.LeadId ?? followUp?.LeadId ?? activity?.LeadId, followUp?.OpportunityId, relatedName);
    }

    public RecordForm ToRecordForm(CrmRecord record)
    {
        var dto = ToRecordDto(record);
        return new RecordForm
        {
            Id = dto.Id, Module = dto.Module, Name = dto.Name, Status = dto.Status, OwnerId = dto.OwnerId,
            Email = dto.Email, Phone = dto.Phone, Company = dto.Company, Address = dto.Address, City = dto.City, State = dto.State,
            Notes = dto.Notes, Source = dto.Source, Priority = dto.Priority ?? "Normal", ExpectedValue = dto.ExpectedValue,
            Amount = dto.Amount, Probability = dto.Probability, Stage = dto.Stage ?? "Qualification", CloseDate = dto.CloseDate,
            DueDate = dto.DueDate, Type = dto.Type ?? "Call", CustomerId = dto.CustomerId, LeadId = dto.LeadId, OpportunityId = dto.OpportunityId
        };
    }

    // ---------- Saving ----------

    public async Task<CrmRecord> SaveRecordAsync(string moduleName, RecordForm form, Guid? existingId = null)
    {
        if (!CrmCatalog.IsValidModule(moduleName))
            throw new BusinessRuleException("", "Page not found.", 404);

        var record = existingId.HasValue ? await FindRecordAsync(moduleName, existingId.Value) : null;
        var valueBeforeChange = record == null ? null : ToRecordDto(record);
        form.Id = existingId ?? Guid.Empty;

        await ValidateCommonRulesAsync(moduleName, form, record);
        switch (moduleName)
        {
            case "customers":
                await ValidateCustomerAsync(form);
                break;
            case "leads":
                ValidateLead(form, record);
                break;
            case "opportunities":
                await ValidateOpportunityAsync(form);
                break;
            default:
                await ValidateFollowUpOrActivityAsync(moduleName, form, record);
                break;
        }

        record ??= moduleName switch
        {
            "customers" => new Customer(),
            "leads" => new Lead(),
            "opportunities" => new Opportunity(),
            "followups" => new FollowUp(),
            _ => new Activity()
        };
        if (!existingId.HasValue)
        {
            record.CreatedBy = CurrentUserId;
            dbContext.Add(record);
        }
        await CopyFormToRecordAsync(form, record);

        var recordId = record.Id.ToString();
        auditService.AddEntry(existingId.HasValue ? "Update" : "Create", moduleName, recordId, valueBeforeChange, ToRecordDto(record));
        if (valueBeforeChange != null && valueBeforeChange.Status != record.Status)
            auditService.AddEntry("Status Change", moduleName, recordId, valueBeforeChange.Status, record.Status);
        if (record is FollowUp savedFollowUp && savedFollowUp.Status == "Completed" && valueBeforeChange?.Status != "Completed")
            await UpdateLeadAfterFollowUpAsync(savedFollowUp);
        if (valueBeforeChange?.DueDate != null && valueBeforeChange.DueDate != form.DueDate)
            auditService.AddEntry("Reschedule", moduleName, recordId, valueBeforeChange.DueDate, form.DueDate);

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Normally the unique index on customer email/phone, hit by two requests at the same time.
            throw new BusinessRuleException("", "A duplicate or related record prevented this save. Refresh and try again.", 409);
        }
        return record;
    }

    async Task ValidateCommonRulesAsync(string moduleName, RecordForm form, CrmRecord? existingRecord)
    {
        if (!CrmCatalog.StatusesFor(moduleName).Contains(form.Status))
            throw new BusinessRuleException("Status", "Select a valid status.");
        if (IsSalesExecutive && form.OwnerId != CurrentUserId)
            throw new BusinessRuleException("OwnerId", "You can only assign records to yourself.", 403);
        var salesExecutiveIds = SalesExecutiveIds();
        if (!await dbContext.Users.AnyAsync(user => user.Id == form.OwnerId && user.Active && salesExecutiveIds.Contains(user.Id)))
            throw new BusinessRuleException("OwnerId", "Assign the record to an active Sales Executive.");

        // Changing the owner of a record that has children would split the history between two people.
        if (existingRecord != null && existingRecord.OwnerId != form.OwnerId && await HasLinkedRecordsAsync(existingRecord.Id))
            throw new BusinessRuleException("OwnerId", "This record has linked records. Keep its current owner to preserve access to the complete history.", 409);

        if (moduleName is "customers" or "leads")
        {
            if (string.IsNullOrWhiteSpace(form.Email))
                throw new BusinessRuleException("Email", "Email is required.");
            if (string.IsNullOrWhiteSpace(form.Phone))
                throw new BusinessRuleException("Phone", "Phone is required.");
            form.Email = form.Email.Trim().ToLowerInvariant();
        }
    }

    async Task ValidateCustomerAsync(RecordForm form)
    {
        // Deleted customers are included on purpose, their contact details stay reserved.
        if (await dbContext.Customers.AnyAsync(customer => customer.Id != form.Id && customer.Email == form.Email))
            throw new BusinessRuleException("Email", "This email is already registered, including inactive customers.", 409);
        if (await dbContext.Customers.AnyAsync(customer => customer.Id != form.Id && customer.Phone == form.Phone))
            throw new BusinessRuleException("Phone", "This phone is already registered, including inactive customers.", 409);
    }

    static void ValidateLead(RecordForm form, CrmRecord? existingRecord)
    {
        if (decimal.Round(form.ExpectedValue, 2) != form.ExpectedValue)
            throw new BusinessRuleException("ExpectedValue", "Use up to two decimal places.");
        if (!CrmCatalog.Priorities.Contains(form.Priority))
            throw new BusinessRuleException("Priority", "Select a valid priority.");
        if (existingRecord == null && form.Status != "New")
            throw new BusinessRuleException("Status", "A new lead must start with New status.");
        if (existingRecord?.Status == "Converted")
            throw new BusinessRuleException("Status", "Converted leads are read-only.", 409);
        if (existingRecord != null && existingRecord.Status != form.Status && !CrmCatalog.NextLeadStatuses(existingRecord.Status).Contains(form.Status))
            throw new BusinessRuleException("Status", "Use New → Contacted → Qualified. Use Convert for qualified leads.");
    }

    async Task ValidateOpportunityAsync(RecordForm form)
    {
        if (form.CustomerId == null)
            throw new BusinessRuleException("CustomerId", "Select a customer.");
        if (!CrmCatalog.Stages.Contains(form.Stage))
            throw new BusinessRuleException("Stage", "Select a valid stage.");

        var expectedStatus = form.Stage is "Won" or "Lost" ? form.Stage : "Open";
        if (form.Status != expectedStatus)
            throw new BusinessRuleException("Status", "Status must match the selected stage.");
        if (form.Amount <= 0)
            throw new BusinessRuleException("Amount", "Opportunity Amount must be greater than 0.");
        if (decimal.Round(form.Amount, 2) != form.Amount)
            throw new BusinessRuleException("Amount", "Use up to two decimal places.");
        if (form.Probability is < 0 or > 100)
            throw new BusinessRuleException("Probability", "Probability must be between 0 and 100.");
        if (form.CloseDate == null)
            throw new BusinessRuleException("CloseDate", "Expected close date is required.");
        if (expectedStatus == "Open" && form.CloseDate < CrmCatalog.TodayInIndia)
            throw new BusinessRuleException("CloseDate", "Expected Close Date cannot be in the past.");
        if (form.Stage == "Won" && form.Probability != 100)
            throw new BusinessRuleException("Probability", "A won opportunity must have 100% probability.");
        if (form.Stage == "Lost" && form.Probability != 0)
            throw new BusinessRuleException("Probability", "A lost opportunity must have 0% probability.");

        await EnsureSameOwnerAsync("customers", form.CustomerId, "CustomerId", form.OwnerId);
        await EnsureSameOwnerAsync("leads", form.LeadId, "LeadId", form.OwnerId);
        if (form.LeadId.HasValue)
        {
            var linkedLead = (Lead)await FindRecordAsync("leads", form.LeadId.Value);
            if (linkedLead.CustomerId.HasValue && linkedLead.CustomerId != form.CustomerId)
                throw new BusinessRuleException("LeadId", "The converted lead belongs to a different customer.");
        }
    }

    async Task ValidateFollowUpOrActivityAsync(string moduleName, RecordForm form, CrmRecord? existingRecord)
    {
        var isFollowUp = moduleName == "followups";
        var today = CrmCatalog.TodayInIndia;

        if (!CrmCatalog.ActivityTypes.Contains(form.Type))
            throw new BusinessRuleException("Type", "Select a valid activity type.");
        if (form.DueDate == null)
            throw new BusinessRuleException("DueDate", "Date is required.");
        if (isFollowUp && (existingRecord == null || form.Status == "Planned") && form.DueDate < today)
            throw new BusinessRuleException("DueDate", "Follow-up date cannot be earlier than today.");
        if (isFollowUp && form.Status == "Missed" && form.DueDate >= today)
            throw new BusinessRuleException("Status", "Only an overdue follow-up can be marked missed.");

        var linkedCount = (form.CustomerId.HasValue ? 1 : 0) + (form.LeadId.HasValue ? 1 : 0) + (isFollowUp && form.OpportunityId.HasValue ? 1 : 0);
        if (linkedCount != 1)
            throw new BusinessRuleException("CustomerId", "Select exactly one related customer, lead or opportunity.");
        if (!isFollowUp)
            form.OpportunityId = null;

        await EnsureSameOwnerAsync("customers", form.CustomerId, "CustomerId", form.OwnerId);
        await EnsureSameOwnerAsync("leads", form.LeadId, "LeadId", form.OwnerId);
        if (isFollowUp)
            await EnsureSameOwnerAsync("opportunities", form.OpportunityId, "OpportunityId", form.OwnerId);
    }

    // The related record must be visible to the caller and owned by the same person as the new record.
    async Task EnsureSameOwnerAsync(string moduleName, Guid? relatedId, string fieldName, string ownerId)
    {
        if (relatedId == null)
            return;
        var relatedRecord = await FindRecordAsync(moduleName, relatedId.Value);
        if (relatedRecord.OwnerId != ownerId)
            throw new BusinessRuleException(fieldName, "Related record and assigned user must belong to the same sales owner.");
    }

    async Task CopyFormToRecordAsync(RecordForm form, CrmRecord record)
    {
        record.Name = form.Name.Trim();
        record.Status = form.Status;
        record.OwnerId = form.OwnerId;
        record.Owner = await dbContext.Users.FindAsync(form.OwnerId);
        record.Notes = form.Notes?.Trim() ?? "";
        record.UpdatedAt = DateTime.UtcNow;

        switch (record)
        {
            case Customer customer:
                if (string.IsNullOrEmpty(customer.Code))
                    customer.Code = "CUS-" + customer.Id.ToString("N")[..8].ToUpperInvariant();
                customer.Email = form.Email!;
                customer.Phone = form.Phone!;
                customer.Company = form.Company?.Trim() ?? "";
                customer.Address = form.Address?.Trim() ?? "";
                customer.City = form.City?.Trim() ?? "";
                customer.State = form.State?.Trim() ?? "";
                break;
            case Lead lead:
                if (string.IsNullOrEmpty(lead.Code))
                    lead.Code = "LD-" + lead.Id.ToString("N")[..8].ToUpperInvariant();
                lead.Email = form.Email!;
                lead.Phone = form.Phone!;
                lead.Company = form.Company?.Trim() ?? "";
                lead.Source = form.Source?.Trim() ?? "";
                lead.Priority = form.Priority;
                lead.ExpectedValue = form.ExpectedValue;
                break;
            case Opportunity opportunity:
                // Outcome date is the real "won/lost on" date used by the monthly sales chart.
                var isClosingNow = opportunity.Stage != form.Stage && form.Stage is "Won" or "Lost";
                if (isClosingNow)
                    opportunity.OutcomeDate = CrmCatalog.TodayInIndia;
                if (form.Status == "Open")
                    opportunity.OutcomeDate = null;
                opportunity.CustomerId = form.CustomerId!.Value;
                opportunity.LeadId = form.LeadId;
                opportunity.Stage = form.Stage;
                opportunity.Amount = form.Amount;
                opportunity.Probability = form.Probability;
                opportunity.CloseDate = form.CloseDate!.Value;
                opportunity.Source = form.Source?.Trim() ?? "";
                break;
            case FollowUp followUp:
                followUp.CustomerId = form.CustomerId;
                followUp.LeadId = form.LeadId;
                followUp.OpportunityId = form.OpportunityId;
                followUp.DueDate = form.DueDate!.Value;
                followUp.Type = form.Type;
                break;
            case Activity activity:
                activity.CustomerId = form.CustomerId;
                activity.LeadId = form.LeadId;
                activity.DueDate = form.DueDate!.Value;
                activity.Type = form.Type;
                break;
        }
    }

    async Task<bool> HasLinkedRecordsAsync(Guid recordId) =>
        await dbContext.Opportunities.AnyAsync(opportunity => !opportunity.Deleted && (opportunity.CustomerId == recordId || opportunity.LeadId == recordId))
        || await dbContext.FollowUps.AnyAsync(followUp => !followUp.Deleted && (followUp.CustomerId == recordId || followUp.LeadId == recordId || followUp.OpportunityId == recordId))
        || await dbContext.Activities.AnyAsync(activity => !activity.Deleted && (activity.CustomerId == recordId || activity.LeadId == recordId));

    // ---------- Other actions ----------

    // Soft delete. Records with linked children are kept so the history is not broken.
    public async Task DeleteRecordAsync(string moduleName, Guid recordId)
    {
        var record = await FindRecordAsync(moduleName, recordId);
        var hasChildren = await HasLinkedRecordsAsync(recordId)
            || await dbContext.Leads.AnyAsync(lead => !lead.Deleted && lead.CustomerId == recordId);
        if (hasChildren)
            throw new BusinessRuleException("", "Delete the related records before deleting this record, or keep it for its history.", 409);

        var valueBeforeDelete = ToRecordDto(record);
        record.Deleted = true;
        record.UpdatedAt = DateTime.UtcNow;
        auditService.AddEntry("Delete", moduleName, recordId.ToString(), valueBeforeDelete, new { record.Id, record.Deleted });
        await dbContext.SaveChangesAsync();
    }

    // One-click "Mark done" for a planned follow-up or a pending activity.
    public async Task MarkCompletedAsync(string moduleName, Guid recordId)
    {
        if (moduleName is not ("followups" or "activities"))
            throw new BusinessRuleException("", "Only follow-ups and activities can be marked as done.", 404);

        var record = await FindRecordAsync(moduleName, recordId);
        var openStatus = moduleName == "followups" ? "Planned" : "Pending";
        if (record.Status != openStatus)
            throw new BusinessRuleException("Status", $"Only a {openStatus.ToLowerInvariant()} item can be marked as done.", 409);

        var valueBeforeChange = ToRecordDto(record);
        record.Status = "Completed";
        record.UpdatedAt = DateTime.UtcNow;
        auditService.AddEntry("Update", moduleName, recordId.ToString(), valueBeforeChange, ToRecordDto(record));
        auditService.AddEntry("Status Change", moduleName, recordId.ToString(), openStatus, "Completed");
        if (record is FollowUp completedFollowUp)
            await UpdateLeadAfterFollowUpAsync(completedFollowUp);
        await dbContext.SaveChangesAsync();
    }

    // Follow-up workflow step "Update related CRM record" (assignment PDF section 8):
    // a completed follow-up means the lead has been contacted, so a New lead moves to Contacted.
    async Task UpdateLeadAfterFollowUpAsync(FollowUp completedFollowUp)
    {
        if (completedFollowUp.LeadId == null)
            return;
        var relatedLead = await dbContext.Leads.FirstOrDefaultAsync(lead => lead.Id == completedFollowUp.LeadId && !lead.Deleted);
        if (relatedLead == null || relatedLead.Status != "New")
            return;
        relatedLead.Status = "Contacted";
        relatedLead.UpdatedAt = DateTime.UtcNow;
        auditService.AddEntry("Status Change", "leads", relatedLead.Id.ToString(), "New",
            new { Status = "Contacted", Reason = "Follow-up completed", FollowUpId = completedFollowUp.Id });
    }

    // Qualified lead → customer (+ opportunity when the lead has a value), all in one transaction.
    public async Task<Guid> ConvertLeadAsync(Guid leadId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var lead = (Lead)await FindRecordAsync("leads", leadId);
        if (lead.Status != "Qualified")
            throw new BusinessRuleException("", "Only qualified leads can be converted.", 409);
        var leadBeforeConversion = ToRecordDto(lead);

        // Reuse the customer only when both email and phone match and it is the same owner.
        var matchingCustomer = await dbContext.Customers.FirstOrDefaultAsync(customer => customer.Email == lead.Email || customer.Phone == lead.Phone);
        if (matchingCustomer != null && (matchingCustomer.Deleted || matchingCustomer.OwnerId != lead.OwnerId
            || matchingCustomer.Email != lead.Email || matchingCustomer.Phone != lead.Phone))
            throw new BusinessRuleException("", "Customer contact details conflict with an existing record. Please resolve the duplicate first.", 409);

        if (matchingCustomer == null)
        {
            matchingCustomer = new Customer
            {
                Name = lead.Name, Email = lead.Email, Phone = lead.Phone, Company = lead.Company,
                OwnerId = lead.OwnerId, CreatedBy = CurrentUserId, Status = "Active", Notes = lead.Notes
            };
            matchingCustomer.Code = "CUS-" + matchingCustomer.Id.ToString("N")[..8].ToUpperInvariant();
            dbContext.Customers.Add(matchingCustomer);
            auditService.AddEntry("Create", "customers", matchingCustomer.Id.ToString(), newValue: ToRecordDto(matchingCustomer));
        }

        lead.Status = "Converted";
        lead.CustomerId = matchingCustomer.Id;
        lead.UpdatedAt = DateTime.UtcNow;

        if (lead.ExpectedValue > 0)
        {
            var newOpportunity = new Opportunity
            {
                Name = (string.IsNullOrWhiteSpace(lead.Company) ? lead.Name : lead.Company) + " - sales enquiry",
                CustomerId = matchingCustomer.Id, LeadId = lead.Id, OwnerId = lead.OwnerId, CreatedBy = CurrentUserId,
                Amount = lead.ExpectedValue, Probability = 25, CloseDate = CrmCatalog.TodayInIndia.AddDays(30),
                Stage = "Qualification", Status = "Open", Source = lead.Source
            };
            dbContext.Opportunities.Add(newOpportunity);
            auditService.AddEntry("Create", "opportunities", newOpportunity.Id.ToString(), newValue: ToRecordDto(newOpportunity));
        }

        auditService.AddEntry("Convert", "leads", leadId.ToString(), leadBeforeConversion, ToRecordDto(lead));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return matchingCustomer.Id;
    }
}
