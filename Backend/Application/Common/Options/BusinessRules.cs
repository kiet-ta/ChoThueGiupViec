namespace CommonService.Application.Common.Options;

/// <summary>
/// Single options class for all business parameters (decisions.md section 4, BASE-05).
/// Holds default values for every key; bound from configuration section "BusinessRules".
/// </summary>
public sealed class BusinessRules
{
    public const string SectionName = "BusinessRules";

    public ShiftRules Shift { get; set; } = new();
    public AreaRules Area { get; set; } = new();
    public GpsRules Gps { get; set; } = new();
    public DispatchRules Dispatch { get; set; } = new();
    public CommissionRules Commission { get; set; } = new();
    public AbsenceRules Absence { get; set; } = new();
    public VolRules Vol { get; set; } = new();
    public PhotoRules Photos { get; set; } = new();
    public PaymentRules Payments { get; set; } = new();
    public EkycRules Ekyc { get; set; } = new();
    public OtpRules Otp { get; set; } = new();
    public SubscriptionRules Subscription { get; set; } = new();
    public AgencyRules Agency { get; set; } = new();
    public SlaRules Sla { get; set; } = new();
    public SuperFreelancerRules SuperFreelancer { get; set; } = new();
    public PremiumRules Premium { get; set; } = new();
    public RatingRules Rating { get; set; } = new();
    public CancelRules Cancel { get; set; } = new();
    public AuthRules Auth { get; set; } = new();
    public PrivacyRules Privacy { get; set; } = new();
    public CustomerRules Customer { get; set; } = new();
    public AddressRules Address { get; set; } = new();
}

public sealed class ShiftRules
{
    public int MaxHours { get; set; } = 4;
}

public sealed class AreaRules
{
    public decimal StandardMaxM2 { get; set; } = 80m;
}

public sealed class GpsRules
{
    public double CheckInToleranceMeters { get; set; } = 100.0;
}

public sealed class DispatchRules
{
    private static readonly double[] DefaultRadiusSteps = [5.0, 7.0, 10.0];
    private double[]? _radiusStepsKm;

    public double[] RadiusStepsKm
    {
        get => _radiusStepsKm ?? DefaultRadiusSteps;
        set
        {
            if (_radiusStepsKm == null && value.Length > DefaultRadiusSteps.Length &&
                value.Take(DefaultRadiusSteps.Length).SequenceEqual(DefaultRadiusSteps))
            {
                _radiusStepsKm = value.Skip(DefaultRadiusSteps.Length).ToArray();
            }
            else
            {
                _radiusStepsKm = value;
            }
        }
    }

    public int OfferTimeoutSeconds { get; set; } = 30;
}

public sealed class CommissionRules
{
    public decimal Freelancer { get; set; } = 0.200m;
}

public sealed class AbsenceRules
{
    public decimal FeeRate { get; set; } = 0.40m;
    public int MinWaitMinutes { get; set; } = 15;
    public int MinCallAttempts { get; set; } = 2;
    public int CustomerDisputeHours { get; set; } = 24;
}

public sealed class VolRules
{
    public double Threshold { get; set; } = 100.0;
    public int ResizeWidthPx { get; set; } = 640;
}

public sealed class PhotoRules
{
    public int MinPerPhase { get; set; } = 3;
    public int MaxPerPhase { get; set; } = 5;
}

public sealed class PaymentRules
{
    public int QrExpiryMinutes { get; set; } = 15;
    public int ReconcileAfterMinutes { get; set; } = 3;
    public int ReconcileIntervalSeconds { get; set; } = 60;
}

public sealed class EkycFakeRules
{
    public decimal Confidence { get; set; } = 92.00m;
}

public sealed class EkycRules
{
    public decimal AutoApproveConfidence { get; set; } = 85.00m;
    public EkycFakeRules Fake { get; set; } = new();
    public decimal AuditRate { get; set; } = 0.20m;
    public int FullAuditFirstJobs { get; set; } = 5;
}

public sealed class OtpRules
{
    public int Length { get; set; } = 6;
    public int TtlMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxPerPhonePerHour { get; set; } = 5;
    public int MaxPerIpPerHour { get; set; } = 20;
}

public sealed class SubscriptionRules
{
    public int GraceDays { get; set; } = 7;
}

public sealed class AgencyRules
{
    public decimal MinEscrowBalance { get; set; } = 5_000_000m;
}

public sealed class SlaPenaltyRules
{
    public decimal NoShow { get; set; } = 20m;
    public decimal Shortage { get; set; } = 10m;
    public decimal QualityComplaint { get; set; } = 5m;
}

public sealed class SlaRules
{
    public decimal InitialScore { get; set; } = 100.00m;
    public SlaPenaltyRules Penalty { get; set; } = new();
    public decimal WarnAtOrBelow { get; set; } = 70m;
    public decimal BlockPremiumBelow { get; set; } = 50m;
    public int AppealHours { get; set; } = 48;
}

public sealed class SuperFreelancerRules
{
    public decimal MinRating { get; set; } = 4.80m;
    public int MinCompletedJobs { get; set; } = 50;
    public int NoUpheldDisputeDays { get; set; } = 180;
    public decimal RevokeBelowRating { get; set; } = 4.70m;
}

public sealed class PremiumRules
{
    public int MinLeadHours { get; set; } = 4;
}

public sealed class RatingMinAvgAfterJobsRules
{
    public decimal Rating { get; set; } = 4.00m;
    public int Jobs { get; set; } = 10;
}

public sealed class RatingRules
{
    public int WindowHours { get; set; } = 48;
    public int LowStarThreshold { get; set; } = 2;
    public int ConsecutiveLowToFlag { get; set; } = 3;
    public RatingMinAvgAfterJobsRules MinAvgAfterJobs { get; set; } = new();
}

public sealed class CancelRules
{
    public int FullRefundHoursBefore { get; set; } = 2;
    public decimal LateFeeRate { get; set; } = 0.40m;
    public int WorkerCancelLockCount { get; set; } = 3;
    public int WorkerCancelWindowDays { get; set; } = 30;
}

public sealed class AuthRules
{
    public int MinPasswordLength { get; set; } = 10;
    public int LockoutFailures { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int RegistrationTokenMinutes { get; set; } = 30;
}

public sealed class PrivacyRules
{
    public int PhoneVisibleBeforeMinutes { get; set; } = 60;
    public int PhoneVisibleAfterMinutes { get; set; } = 60;
}

public sealed class CustomerRules
{
    public decimal InitialTrustScore { get; set; } = 0.00m;
}

public sealed class AddressRules
{
    public decimal RoomMaxAreaM2 { get; set; } = 30m;
    public int HouseMaxFloors { get; set; } = 10;
}
