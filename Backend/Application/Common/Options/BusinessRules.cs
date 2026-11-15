namespace CommonService.Application.Common.Options;

/// <summary>
/// Single options class for all business parameters (decisions.md section 4, BASE-05).
/// Holds default values for every key; bound from configuration section "BusinessRules".
/// </summary>
public class BusinessRules
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
    public BookingRules Booking { get; set; } = new();
    public RatingRules Rating { get; set; } = new();
    public CancelRules Cancel { get; set; } = new();
    public AuthRules Auth { get; set; } = new();
    public PrivacyRules Privacy { get; set; } = new();
    public CustomerRules Customer { get; set; } = new();
    public AddressRules Address { get; set; } = new();
    public WorkerRules Worker { get; set; } = new();
    public AcceptanceRules Acceptance { get; set; } = new();
}

public class ShiftRules
{
    public int MaxHours { get; set; } = 4;
}

public class AreaRules
{
    public decimal StandardMaxM2 { get; set; } = 80m;
}

public class GpsRules
{
    public double CheckInToleranceMeters { get; set; } = 100.0;
}

public class DispatchRules
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

    /// <summary>
    /// D36: after the end of the shift of a large ECONOMY house (one worker), the worker has this many minutes to run
    /// <c>submit-completion</c>; if the job is still <c>IN_PROGRESS</c> then, the 2nd job opens by itself (owner decision 2026-11-15).
    /// </summary>
    public int ContinuationConfirmMinutes { get; set; } = 15;

    /// <summary>
    /// BR-02 fallback: how long the seat-1 worker has to confirm the second consecutive shift after the customer agreed (leader decision
    /// 2026-10-14, 2 minutes). Longer than <see cref="OfferTimeoutSeconds"/> because a worker must read a different question.
    /// </summary>
    public int FallbackConfirmSeconds { get; set; } = 120;
}

public class CommissionRules
{
    public decimal Freelancer { get; set; } = 0.200m;
}

public class AbsenceRules
{
    public decimal FeeRate { get; set; } = 0.40m;
    public int MinWaitMinutes { get; set; } = 15;
    public int MinCallAttempts { get; set; } = 2;
    public int CustomerDisputeHours { get; set; } = 24;
}

public class VolRules
{
    public double Threshold { get; set; } = 100.0;
    public int ResizeWidthPx { get; set; } = 640;
}

public class PhotoRules
{
    public int MinPerPhase { get; set; } = 3;
    public int MaxPerPhase { get; set; } = 5;
}

public class PaymentRules
{
    public int QrExpiryMinutes { get; set; } = 15;
    public int ReconcileAfterMinutes { get; set; } = 3;
    public int ReconcileIntervalSeconds { get; set; } = 60;
    public bool SandboxAutoConfirm { get; set; } = true;
    public int SandboxAutoConfirmDelaySeconds { get; set; } = 3;
}

public class EkycFakeRules
{
    public decimal Confidence { get; set; } = 92.00m;
}

public class EkycRules
{
    public decimal AutoApproveConfidence { get; set; } = 85.00m;
    public EkycFakeRules Fake { get; set; } = new();
    public decimal AuditRate { get; set; } = 0.20m;
    public int FullAuditFirstJobs { get; set; } = 5;
}

public class OtpRules
{
    public int Length { get; set; } = 6;
    public int TtlMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxPerPhonePerHour { get; set; } = 5;
    public int MaxPerIpPerHour { get; set; } = 20;
}

public class SubscriptionRules
{
    public int GraceDays { get; set; } = 7;
}

public class AgencyRules
{
    public decimal MinEscrowBalance { get; set; } = 5_000_000m;
}

public class SlaPenaltyRules
{
    public decimal NoShow { get; set; } = 20m;
    public decimal Shortage { get; set; } = 10m;
    public decimal QualityComplaint { get; set; } = 5m;
}

public class SlaRules
{
    public decimal InitialScore { get; set; } = 100.00m;
    public SlaPenaltyRules Penalty { get; set; } = new();
    public decimal WarnAtOrBelow { get; set; } = 70m;
    public decimal BlockPremiumBelow { get; set; } = 50m;
    public int AppealHours { get; set; } = 48;
}

public class SuperFreelancerRules
{
    public decimal MinRating { get; set; } = 4.80m;
    public int MinCompletedJobs { get; set; } = 50;
    public int NoUpheldDisputeDays { get; set; } = 180;
    public decimal RevokeBelowRating { get; set; } = 4.70m;
}

public class PremiumRules
{
    public int MinLeadHours { get; set; } = 4;
}

public class BookingRules
{
    /// <summary>Booking horizon in days for both tiers (contract booking.md B3).</summary>
    public int MaxDaysAhead { get; set; } = 14;

    /// <summary>A timed Economy job (decisions D34) must start at least this many minutes from now.</summary>
    public int EconomyMinLeadMinutes { get; set; } = 60;

    /// <summary>Shortest timed job in hours (decisions D34, the 2 h package of the market).</summary>
    public decimal MinDurationHours { get; set; } = 2m;

    /// <summary>Area one worker cleans in one hour; the suggested duration is area per worker / this, rounded up to 0.5 h (D34).</summary>
    public decimal M2PerHour { get; set; } = 20m;

    /// <summary>Free time kept before and after every timed job of a worker for travel (decisions D34).</summary>
    public int TravelBufferMinutes { get; set; } = 30;
}

public class RatingMinAvgAfterJobsRules
{
    public decimal Rating { get; set; } = 4.00m;
    public int Jobs { get; set; } = 10;
}

public class RatingRules
{
    public int WindowHours { get; set; } = 48;
    public int LowStarThreshold { get; set; } = 2;
    public int ConsecutiveLowToFlag { get; set; } = 3;
    public RatingMinAvgAfterJobsRules MinAvgAfterJobs { get; set; } = new();
}

public class CancelRules
{
    public int FullRefundHoursBefore { get; set; } = 2;
    public decimal LateFeeRate { get; set; } = 0.40m;
    public int WorkerCancelLockCount { get; set; } = 3;
    public int WorkerCancelWindowDays { get; set; } = 30;

    /// <summary>
    /// Decisions D35: after a freelancer cancels, the lost seat is re-dispatched until this many minutes before the job start; a seat
    /// still open then fails the order (automatic 100 % refund). A cancel inside this window fails the order at once.
    /// </summary>
    public int RedispatchCutoffMinutes { get; set; } = 30;
}

public class AuthRules
{
    public int MinPasswordLength { get; set; } = 10;
    public int LockoutFailures { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int RegistrationTokenMinutes { get; set; } = 30;
}

public class PrivacyRules
{
    public int PhoneVisibleBeforeMinutes { get; set; } = 60;
    public int PhoneVisibleAfterMinutes { get; set; } = 60;
}

/// <summary>Rules of the freelancer's own account (LEDGER-28). The default is in the class, so appsettings.json needs no entry.</summary>
/// <summary>
/// The 24-hour acceptance window (decision D32, ACCEPT-01), counted from T = the moment the worker submits the AFTER photos: the customer can
/// ask for a redo while the worker is still on site, is reminded, and the job is accepted automatically when the window runs out.
/// </summary>
public class AcceptanceRules
{
    /// <summary>The worker stays on site this long after T; a redo can only be asked inside it (BR-07: 15 to 30 minutes).</summary>
    public int RedoMinutes { get; set; } = 30;

    /// <summary>The customer is reminded this long after T.</summary>
    public int ReminderHours { get; set; } = 20;

    /// <summary>Without an open dispute the job is accepted automatically this long after T; the dispute window ends at the same moment.</summary>
    public int AutoAcceptHours { get; set; } = 24;
}

public class WorkerRules
{
    /// <summary>After a request to change the payout account is sent, no new request can be sent for this many days (decided with Duy: 15).</summary>
    public int BankChangeLockDays { get; set; } = 15;

    /// <summary>Most hours of live jobs one worker may hold on one day (decisions D34).</summary>
    public int MaxJobHoursPerDay { get; set; } = 8;
}

public class CustomerRules
{
    public decimal InitialTrustScore { get; set; } = 0.00m;
}

public class AddressRules
{
    public decimal RoomMaxAreaM2 { get; set; } = 30m;
    public int HouseMaxFloors { get; set; } = 10;
}
