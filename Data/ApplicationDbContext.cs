using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // One table per module, shared columns repeated in each table.
        modelBuilder.Entity<CrmRecord>().UseTpcMappingStrategy();
        modelBuilder.Entity<CrmRecord>().Property(record => record.Id).ValueGeneratedNever();
        modelBuilder.Entity<CrmRecord>()
            .HasOne(record => record.Owner)
            .WithMany()
            .HasForeignKey(record => record.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // The service checks duplicates first; these indexes still stop two parallel requests.
        modelBuilder.Entity<Customer>().HasIndex(customer => customer.Email).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(customer => customer.Phone).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(customer => customer.Code).IsUnique();
        modelBuilder.Entity<Lead>().HasIndex(lead => lead.Code).IsUnique();
        modelBuilder.Entity<AuditLog>().HasIndex(log => log.CreatedAt);

        MapToAssignmentNames(modelBuilder);

        // Records are soft deleted, so a hard cascade delete should never happen.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }

    // Table and column names follow section 17.3 of the assignment PDF (CustomerId, CustomerName,
    // AssignedTo, FollowUpDate, EntityName, IpAddress ...). C# property names stay short and readable,
    // EF Core translates between the two.
    static void MapToAssignmentNames(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.Property(appUser => appUser.Active).HasColumnName("IsActive");
            user.Property(appUser => appUser.CreatedAt).HasColumnName("CreatedDate");
        });

        modelBuilder.Entity<Customer>().ToTable("Customer", table =>
        {
            table.Property(customer => customer.Id).HasColumnName("CustomerId");
            table.Property(customer => customer.Code).HasColumnName("CustomerCode");
            table.Property(customer => customer.Name).HasColumnName("CustomerName");
            table.Property(customer => customer.Company).HasColumnName("CompanyName");
            MapCommonColumns(table);
        });

        modelBuilder.Entity<Lead>().ToTable("Lead", table =>
        {
            table.Property(lead => lead.Id).HasColumnName("LeadId");
            table.Property(lead => lead.Code).HasColumnName("LeadCode");
            table.Property(lead => lead.Name).HasColumnName("LeadName");
            table.Property(lead => lead.Company).HasColumnName("CompanyName");
            MapCommonColumns(table);
        });

        modelBuilder.Entity<Opportunity>().ToTable("Opportunity", table =>
        {
            table.Property(opportunity => opportunity.Id).HasColumnName("OpportunityId");
            table.Property(opportunity => opportunity.Name).HasColumnName("OpportunityName");
            table.Property(opportunity => opportunity.CloseDate).HasColumnName("ExpectedCloseDate");
            MapCommonColumns(table);
        });

        modelBuilder.Entity<FollowUp>().ToTable("FollowUp", table =>
        {
            table.Property(followUp => followUp.Id).HasColumnName("FollowUpId");
            table.Property(followUp => followUp.Name).HasColumnName("Subject");
            table.Property(followUp => followUp.DueDate).HasColumnName("FollowUpDate");
            table.Property(followUp => followUp.Type).HasColumnName("FollowUpType");
            table.Property(followUp => followUp.Notes).HasColumnName("Remarks");
            MapCommonColumns(table);
        });

        modelBuilder.Entity<Activity>().ToTable("Activity", table =>
        {
            table.Property(activity => activity.Id).HasColumnName("ActivityId");
            table.Property(activity => activity.Name).HasColumnName("Subject");
            table.Property(activity => activity.DueDate).HasColumnName("ActivityDate");
            table.Property(activity => activity.Type).HasColumnName("ActivityType");
            table.Property(activity => activity.Notes).HasColumnName("Description");
            MapCommonColumns(table);
        });

        modelBuilder.Entity<AuditLog>().ToTable("AuditLog");
        modelBuilder.Entity<AuditLog>(audit =>
        {
            audit.Property(log => log.Id).HasColumnName("AuditLogId");
            audit.Property(log => log.Entity).HasColumnName("EntityName");
            audit.Property(log => log.CreatedAt).HasColumnName("CreatedDate");
            audit.Property(log => log.Ip).HasColumnName("IpAddress");
        });
    }

    // Columns every CRM table shares: owner, created/modified dates and the soft-delete flag.
    static void MapCommonColumns<TRecord>(Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder<TRecord> table) where TRecord : CrmRecord
    {
        table.Property(record => record.OwnerId).HasColumnName("AssignedTo");
        table.Property(record => record.CreatedAt).HasColumnName("CreatedDate");
        table.Property(record => record.UpdatedAt).HasColumnName("ModifiedDate");
        table.Property(record => record.Deleted).HasColumnName("IsDeleted");
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditRowTouched = ChangeTracker.Entries<AuditLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (auditRowTouched)
            throw new InvalidOperationException("Audit entries cannot be changed or removed.");
        return base.SaveChangesAsync(cancellationToken);
    }
}
