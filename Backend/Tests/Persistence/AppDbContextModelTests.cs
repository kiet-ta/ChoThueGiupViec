using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Persistence;

public class AppDbContextModelTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Model_Contains_All_27_Tables()
    {
        using var context = CreateContext();
        var model = context.Model;

        var expectedTables = new[]
        {
            "ADMIN",
            "ADMIN_AUDIT_LOG",
            "BOOKING_SLOT",
            "CHECK_IN_LOG",
            "CUSTOMER",
            "CUSTOMER_ADDRESS",
            "DISPUTE_TICKET",
            "ESCROW_TRANSACTION",
            "FAVORITE_WORKER",
            "INCIDENT_LOG",
            "JOB_ASSIGNMENT",
            "JOB_ORDER",
            "JOB_ORDER_EXTENSION",
            "JOB_PHOTO",
            "OTP_CODE",
            "PARTNER_AGENCY",
            "PARTNER_SUBSCRIPTION",
            "PAYMENT_TRANSACTION",
            "PAYOUT_BATCH",
            "PAYOUT_ITEM",
            "PRICE_RULE",
            "REFRESH_TOKEN",
            "SKILL",
            "SUBSCRIPTION_PACKAGE",
            "TWO_WAY_RATING",
            "WORKER",
            "WORKER_SKILL"
        };

        var actualTables = model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t != null)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        Assert.Equal(27, expectedTables.Length);
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, actualTables);
        }
    }

    [Fact]
    public void AdminAccount_Maps_To_ADMIN_Table()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AdminAccount));
        Assert.NotNull(entityType);
        Assert.Equal("ADMIN", entityType.GetTableName());
    }

    [Fact]
    public void BookingSlot_Has_Unique_Index_On_Worker_Date_Shift()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(BookingSlot));
        Assert.NotNull(entityType);

        var uniqueIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(BookingSlot.WorkerId), nameof(BookingSlot.SlotDate), nameof(BookingSlot.ShiftCode) }));

        Assert.NotNull(uniqueIndex);
    }

    [Fact]
    public void JobAssignment_Has_Required_Indexes_And_Filtered_Unique_Slot_Index()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(JobAssignment));
        Assert.NotNull(entityType);

        var indexes = entityType.GetIndexes().ToList();

        // 0-JOIN indexes
        Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(JobAssignment.WorkerId)));
        Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(JobAssignment.AgencyId)));
        Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(JobAssignment.CustomerId)));
        Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(JobAssignment.OrderId)));

        // Filtered unique index on slot_id
        var slotIndex = indexes.FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(JobAssignment.SlotId)));
        Assert.NotNull(slotIndex);
        Assert.True(slotIndex.IsUnique);
        Assert.NotNull(slotIndex.GetFilter());
        Assert.Contains("CANCELLED", slotIndex.GetFilter()!);
        Assert.Contains("CANCELLED_BY_WORKER", slotIndex.GetFilter()!);
        Assert.Contains("REASSIGNED", slotIndex.GetFilter()!);
    }

    [Fact]
    public void CheckConstraints_Are_Configured_On_Worker_Payment_And_Payout()
    {
        using var context = CreateContext();
        var model = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;

        var worker = model.FindEntityType(typeof(Worker));
        Assert.NotNull(worker);
        var workerCc = worker.GetCheckConstraints().FirstOrDefault(c => c.Name == "CK_WORKER_type_agency");
        Assert.NotNull(workerCc);

        var payment = model.FindEntityType(typeof(PaymentTransaction));
        Assert.NotNull(payment);
        var paymentCc = payment.GetCheckConstraints().FirstOrDefault(c => c.Name == "CK_PAYMENT_TRANSACTION_purpose");
        Assert.NotNull(paymentCc);

        var payout = model.FindEntityType(typeof(PayoutItem));
        Assert.NotNull(payout);
        var payoutCc = payout.GetCheckConstraints().FirstOrDefault(c => c.Name == "CK_PAYOUT_ITEM_payee");
        Assert.NotNull(payoutCc);
    }

    [Fact]
    public void ValueConverters_Applied_For_Enums_And_DateTime_UTC()
    {
        using var context = CreateContext();
        var model = context.Model;

        // Verify DateTime converter on Customer.CreatedAt
        var customer = model.FindEntityType(typeof(Customer));
        Assert.NotNull(customer);
        var createdAtProp = customer.FindProperty(nameof(Customer.CreatedAt));
        Assert.NotNull(createdAtProp);
        Assert.NotNull(createdAtProp.GetValueConverter());

        // Verify Enum converter on Customer.AccountStatus
        var statusProp = customer.FindProperty(nameof(Customer.AccountStatus));
        Assert.NotNull(statusProp);
        Assert.NotNull(statusProp.GetValueConverter());
        Assert.Equal(typeof(string), statusProp.GetValueConverter()!.ProviderClrType);
    }
}
