using System.Reflection;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Domain;

public class DomainModelTests
{
    private static readonly string[] EntityNames =
    [
        // 22 tables of Backend/GiupViec_Physical_DB_MVP5.drawio
        "Customer", "CustomerAddress", "FavoriteWorker", "JobOrder", "JobOrderExtension", "PaymentTransaction",
        "JobAssignment", "CheckInLog", "IncidentLog", "JobPhoto", "Worker", "BookingSlot", "TwoWayRating", "DisputeTicket",
        "PartnerAgency", "PartnerSubscription", "SubscriptionPackage", "Skill", "WorkerSkill", "AdminAccount", "PayoutBatch", "PayoutItem",
        // 5 tables added by decisions section 3 (SC-2, SC-3, SC-4, SC-6, SC-7)
        "PriceRule", "AdminAuditLog", "EscrowTransaction", "OtpCode", "RefreshToken",
    ];

    [Fact]
    public void All_27_entities_exist_as_partial_domain_classes()
    {
        Assert.Equal(27, EntityNames.Length);
        var domain = typeof(Customer).Assembly;
        foreach (var name in EntityNames)
        {
            var type = domain.GetType($"CommonService.Domain.Entities.{name}");
            Assert.True(type is { IsClass: true, IsPublic: true }, $"missing public entity class {name}");
        }
    }

    [Fact]
    public void JobAssignment_is_the_flat_execution_node()
    {
        var props = typeof(JobAssignment).GetProperties().ToDictionary(p => p.Name, p => p.PropertyType);

        Assert.Equal(typeof(long), props["OrderId"]);
        Assert.Equal(typeof(int), props["CustomerId"]);
        Assert.Equal(typeof(int), props["WorkerId"]);
        Assert.Equal(typeof(int?), props["AgencyId"]);
        Assert.Equal(typeof(int), props["SlotId"]);
        Assert.Equal(typeof(ServiceTier), props["ServiceTier"]);
        Assert.Equal(typeof(decimal), props["PayoutAmount"]);
    }

    [Fact]
    public void Entities_have_no_navigation_properties()
    {
        var domain = typeof(Customer).Assembly;
        foreach (var name in EntityNames)
        {
            var type = domain.GetType($"CommonService.Domain.Entities.{name}")!;
            var navigations = type.GetProperties()
                .Where(p => p.PropertyType.Namespace == "CommonService.Domain.Entities"
                         || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments().Any(a => a.Namespace == "CommonService.Domain.Entities")))
                .Select(p => p.Name);
            Assert.Empty(navigations);
        }
    }

    [Fact]
    public void Status_and_sti_properties_cannot_be_set_from_outside()
    {
        Assert.False(HasPublicSetter(typeof(JobOrder), nameof(JobOrder.OrderStatus)));
        Assert.False(HasPublicSetter(typeof(JobAssignment), nameof(JobAssignment.AssignmentStatus)));
        Assert.False(HasPublicSetter(typeof(Worker), nameof(Worker.WorkStatus)));
        Assert.False(HasPublicSetter(typeof(Worker), nameof(Worker.WorkerType)));
        Assert.False(HasPublicSetter(typeof(Worker), nameof(Worker.AgencyId)));
    }

    [Fact]
    public void Freelancer_has_no_agency_and_starts_pending()
    {
        var w = Worker.CreateFreelancer("0901234567", "012345678901", "Nguyen Van A");

        Assert.Equal(WorkerType.Freelancer, w.WorkerType);
        Assert.Null(w.AgencyId);
        Assert.Equal(WorkStatus.Pending, w.WorkStatus);
    }

    [Fact]
    public void Agency_staff_belongs_to_an_agency_and_starts_idle()
    {
        var w = Worker.CreateAgencyStaff(7, "0901234568", "012345678902", "Tran Thi B");

        Assert.Equal(WorkerType.AgencyStaff, w.WorkerType);
        Assert.Equal(7, w.AgencyId);
        Assert.Equal(WorkStatus.Idle, w.WorkStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Agency_staff_needs_a_real_agency(int agencyId) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Worker.CreateAgencyStaff(agencyId, "0901234568", "012345678902", "X"));

    [Fact]
    public void Total_area_is_floor_area_times_floors()
    {
        var address = new CustomerAddress { FloorAreaM2 = 45.50m, NumFloors = 3 };

        Assert.Equal(136.50m, address.TotalAreaM2);
    }

    [Theory]
    [InlineData(typeof(ServiceTier), 10)]
    [InlineData(typeof(WorkerType), 12)]
    [InlineData(typeof(PayeeType), 10)]
    [InlineData(typeof(HousingType), 12)]
    [InlineData(typeof(CustomerAccountStatus), 10)]
    [InlineData(typeof(UserRole), 10)]
    [InlineData(typeof(AuditActorType), 10)]
    [InlineData(typeof(EscrowTransactionType), 10)]
    [InlineData(typeof(PaymentPurpose), 12)]
    [InlineData(typeof(PaymentStatus), 10)]
    [InlineData(typeof(JobOrderStatus), 20)]
    [InlineData(typeof(JobAssignmentStatus), 20)]
    [InlineData(typeof(WorkStatus), 10)]
    [InlineData(typeof(FaultParty), 12)]
    public void Every_enum_value_fits_its_varchar_column(Type enumType, int columnLength)
    {
        foreach (var value in Enum.GetValues(enumType))
        {
            var db = (string)typeof(DbEnum).GetMethod(nameof(DbEnum.ToDb))!.MakeGenericMethod(enumType).Invoke(null, [value])!;
            Assert.True(db.Length <= columnLength, $"{enumType.Name}.{value} -> '{db}' is longer than VARCHAR({columnLength})");
        }
    }

    [Theory]
    [InlineData(JobAssignmentStatus.CheckedIn, "CHECKED_IN")]
    [InlineData(JobAssignmentStatus.AwaitingAcceptance, "AWAITING_ACCEPTANCE")]
    [InlineData(JobAssignmentStatus.CancelledByWorker, "CANCELLED_BY_WORKER")]
    [InlineData(JobAssignmentStatus.Assigned, "ASSIGNED")]
    public void DbEnum_uses_upper_snake_case(JobAssignmentStatus value, string expected)
    {
        Assert.Equal(expected, DbEnum.ToDb(value));
        Assert.Equal(value, DbEnum.Parse<JobAssignmentStatus>(expected));
    }

    [Fact]
    public void DbEnum_rejects_unknown_values() =>
        Assert.Throws<ArgumentException>(() => DbEnum.Parse<JobOrderStatus>("NOPE"));

    private static bool HasPublicSetter(Type type, string property) =>
        type.GetProperty(property)!.GetSetMethod(nonPublic: false) is not null;
}
