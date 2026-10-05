using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Persistence;

/// <summary>
/// Platform EF Core DbContext for SQL Server (decision D1).
/// Covers all 27 tables of the MVP schema.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AdminAccount> Admins => Set<AdminAccount>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();
    public DbSet<CheckInLog> CheckInLogs => Set<CheckInLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<DisputeTicket> DisputeTickets => Set<DisputeTicket>();
    public DbSet<EscrowTransaction> EscrowTransactions => Set<EscrowTransaction>();
    public DbSet<FavoriteWorker> FavoriteWorkers => Set<FavoriteWorker>();
    public DbSet<IncidentLog> IncidentLogs => Set<IncidentLog>();
    public DbSet<JobAssignment> JobAssignments => Set<JobAssignment>();
    public DbSet<JobOrder> JobOrders => Set<JobOrder>();
    public DbSet<JobOrderExtension> JobOrderExtensions => Set<JobOrderExtension>();
    public DbSet<JobPhoto> JobPhotos => Set<JobPhoto>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<PartnerAgency> PartnerAgencies => Set<PartnerAgency>();
    public DbSet<PartnerSubscription> PartnerSubscriptions => Set<PartnerSubscription>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PayoutBatch> PayoutBatches => Set<PayoutBatch>();
    public DbSet<PayoutItem> PayoutItems => Set<PayoutItem>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<SubscriptionPackage> SubscriptionPackages => Set<SubscriptionPackage>();
    public DbSet<TwoWayRating> TwoWayRatings => Set<TwoWayRating>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<WorkerSkill> WorkerSkills => Set<WorkerSkill>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Decisions G-3: UTC enforcement for every DateTime
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HaveColumnType("DATETIME2");

        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>()
            .HaveColumnType("DATETIME2");

        // Decisions G-2: default decimal precision (18, 2)
        configurationBuilder.Properties<decimal>()
            .HavePrecision(18, 2);

        // Enum conversions via DbEnum (UPPER_SNAKE_CASE)
        configurationBuilder.Properties<AuditActorType>()
            .HaveConversion<DbEnumValueConverter<AuditActorType>>();

        configurationBuilder.Properties<CustomerAccountStatus>()
            .HaveConversion<DbEnumValueConverter<CustomerAccountStatus>>();

        configurationBuilder.Properties<HousingType>()
            .HaveConversion<DbEnumValueConverter<HousingType>>();

        configurationBuilder.Properties<FaultParty>()
            .HaveConversion<DbEnumValueConverter<FaultParty>>();

        configurationBuilder.Properties<FaultParty?>()
            .HaveConversion<NullableDbEnumValueConverter<FaultParty>>();

        configurationBuilder.Properties<EscrowTransactionType>()
            .HaveConversion<DbEnumValueConverter<EscrowTransactionType>>();

        configurationBuilder.Properties<ServiceTier>()
            .HaveConversion<DbEnumValueConverter<ServiceTier>>();

        configurationBuilder.Properties<JobAssignmentStatus>()
            .HaveConversion<DbEnumValueConverter<JobAssignmentStatus>>();

        configurationBuilder.Properties<JobOrderStatus>()
            .HaveConversion<DbEnumValueConverter<JobOrderStatus>>();

        configurationBuilder.Properties<UserRole>()
            .HaveConversion<DbEnumValueConverter<UserRole>>();

        configurationBuilder.Properties<PaymentPurpose>()
            .HaveConversion<DbEnumValueConverter<PaymentPurpose>>();

        configurationBuilder.Properties<PaymentStatus>()
            .HaveConversion<DbEnumValueConverter<PaymentStatus>>();

        configurationBuilder.Properties<PayeeType>()
            .HaveConversion<DbEnumValueConverter<PayeeType>>();

        configurationBuilder.Properties<WorkerType>()
            .HaveConversion<DbEnumValueConverter<WorkerType>>();

        configurationBuilder.Properties<WorkStatus>()
            .HaveConversion<DbEnumValueConverter<WorkStatus>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
