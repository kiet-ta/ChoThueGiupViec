using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Payments;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Agencies;

/// <summary>The escrow-relevant columns of an agency.</summary>
public sealed record AgencyEscrowRow(int AgencyId, decimal Balance, decimal SlaScore, string Status);

/// <summary>An escrow ledger row as the Partner or Admin sees it (decisions SC-4; never another agency's).</summary>
public sealed record EscrowTransactionDto(
    long EscrowTxnId, string TxnType, decimal Amount, decimal BalanceAfter, decimal? SlaPointsDelta,
    long? OrderId, int? DisputeId, long? PaymentId, string Reason, DateTime CreatedAt, bool CanAppeal);

/// <summary>The agency's escrow and SLA state (contract agencies.md 6).</summary>
/// <param name="HasEscrowWarning">An ACTIVE agency below the minimum (decisions Q25 R4a): still operating, needs a top-up.</param>
/// <param name="HasSlaWarning">The score is at or below <c>Sla.WarnAtOrBelow</c> (Q09).</param>
/// <param name="ExcludedFromPremium">The score is below <c>Sla.BlockPremiumBelow</c> or the agency is not ACTIVE: it receives no Premium job.</param>
public sealed record EscrowStateDto(
    decimal EscrowDepositBalance, string AgencyStatus, decimal MinEscrowBalance, bool HasEscrowWarning,
    decimal SlaScore, bool HasSlaWarning, bool ExcludedFromPremium);

public sealed record DepositRequest(decimal? Amount);

public sealed record DepositPurchaseDto(long PaymentId, string GatewayTxnRef, decimal Amount, string? QrPayload, string? PayUrl, DateTime ExpiresAt);

public sealed record AppealRequest(string? Reason);

public sealed record EscrowAppealDto(
    int AppealId, long EscrowTxnId, int AgencyId, string Reason, string Status, DateTime CreatedAt, DateTime? DecidedAt, string? DecisionNote);

public sealed record AppealDecisionRequest(bool? Accept, string? Note);

public static class EscrowErrorCodes
{
    public const string AppealWindowClosed = "APPEAL_WINDOW_CLOSED";
    public const string AppealExists = "APPEAL_EXISTS";
    public const string NotAPenalty = "NOT_A_PENALTY";
    public const string AppealDecided = "APPEAL_ALREADY_DECIDED";
    public const string AlreadyReversed = "ALREADY_REVERSED";
}

public static class AppealStatuses
{
    public const string Pending = "PENDING";
    public const string Accepted = "ACCEPTED";
    public const string Rejected = "REJECTED";
}

/// <summary>Persistence of the escrow ledger (BE-M5-07). Every change of the balance is one appended <see cref="EscrowTransaction"/> (SC-4).</summary>
public interface IEscrowRepository
{
    Task<AgencyEscrowRow?> GetAgencyAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimistic update: sets balance, score and status only while the balance is still <paramref name="expectedBalance"/>; false means a
    /// concurrent change won and the caller re-reads. Runs inside the caller's transaction.
    /// </summary>
    Task<bool> TryUpdateAgencyAsync(
        int agencyId, decimal expectedBalance, decimal newBalance, decimal newSlaScore, string newStatus, CancellationToken cancellationToken = default);

    /// <summary>Stages the ledger row (append only); the unit of work saves it.</summary>
    void AddTransaction(EscrowTransaction row);

    Task<EscrowTransaction?> GetTransactionAsync(long escrowTxnId, CancellationToken cancellationToken = default);

    /// <summary>An existing PENALTY row of the same agency, order, dispute and reason (the idempotency key of a penalty), or null.</summary>
    Task<EscrowTransaction?> FindPenaltyAsync(int agencyId, long? orderId, int? disputeId, string reason, CancellationToken cancellationToken = default);

    Task<bool> HasReversalAsync(long penaltyTxnId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EscrowTransaction> Items, int Total)> ListAsync(int agencyId, int skip, int take, CancellationToken cancellationToken = default);

    Task<EscrowAppeal?> FindAppealByTransactionAsync(long escrowTxnId, CancellationToken cancellationToken = default);

    void AddAppeal(EscrowAppeal appeal);

    /// <summary>The appeal, TRACKED, so the caller's unit of work saves its decision.</summary>
    Task<EscrowAppeal?> GetAppealForUpdateAsync(int appealId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EscrowAppeal>> ListAppealsAsync(string? status, int take, CancellationToken cancellationToken = default);

    /// <summary>The PENDING payment of the agency's escrow deposit (not tracked), or null.</summary>
    Task<PaymentTransaction?> FindPendingDepositAsync(long paymentId, CancellationToken cancellationToken = default);
}

/// <summary>The pure escrow rules (decisions Q09, Q25 R4), no I/O.</summary>
public static class EscrowRules
{
    /// <summary>SLA points of a violation (positive; decisions Q09: NO_SHOW 20, SHORTAGE 10, QUALITY_COMPLAINT 5).</summary>
    public static decimal Points(SlaViolation violation, SlaPenaltyRules rules) => violation switch
    {
        SlaViolation.NoShow => rules.NoShow,
        SlaViolation.Shortage => rules.Shortage,
        _ => rules.QualityComplaint,
    };

    /// <summary>
    /// A penalty against <paramref name="balance"/>: the amount asked is the customer refund plus the rescue cost (decisions Q09 formula;
    /// the platform fee is the third deduction-order item and is not charged, open question 6 of agencies.md). The balance never goes
    /// below zero: what exists is taken and the rest is the shortfall. A balance that reaches zero suspends an ACTIVE agency (Q25 R4).
    /// </summary>
    public static (decimal Deducted, decimal Shortfall, decimal NewBalance, string NewStatus) Penalize(
        decimal balance, decimal amountAsked, string status)
    {
        var asked = Math.Max(0m, amountAsked);
        var deducted = Math.Min(Math.Max(0m, balance), asked);
        var newBalance = balance - deducted;
        var newStatus = status == AgencyStatuses.Active && newBalance <= 0m && asked > 0m ? AgencyStatuses.Suspended : status;
        return (deducted, asked - deducted, newBalance, newStatus);
    }

    /// <summary>The status after money comes in: PENDING or SUSPENDED become ACTIVE once the balance reaches the minimum (Q25 R3, R4).</summary>
    public static string StatusAfterCredit(string status, decimal newBalance, decimal minimum) =>
        status is AgencyStatuses.Pending or AgencyStatuses.Suspended && AgencyEligibility.StatusFor(newBalance, minimum) == AgencyStatuses.Active
            ? AgencyStatuses.Active
            : status;
}

/// <summary>
/// Escrow fund (BE-M5-07, decisions Q09, SC-4, contract agencies.md 6): top-up through the payment gateway, the ledger, the real
/// <see cref="ISlaPenaltyService"/> and the 48-hour appeal that an Admin settles with a REVERSAL row.
/// </summary>
public sealed class EscrowService(
    IEscrowRepository repository,
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<EscrowService>? logger = null) : ISlaPenaltyService
{
    /// <summary>provenance: a lost optimistic update re-reads; four writers on one agency at once is already far beyond real traffic.</summary>
    private const int MaxAttempts = 5;

    private readonly ILogger<EscrowService> _logger = logger ?? NullLogger<EscrowService>.Instance;

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    private int AdminId => currentUser.UserId is int id && currentUser.Role == UserRole.Admin
        ? id
        : throw new ForbiddenAccessException("Authenticated admin is required.");

    // ---- Partner

    public async Task<EscrowStateDto> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var agency = await repository.GetAgencyAsync(agencyId, cancellationToken) ?? throw new NotFoundException("Agency", agencyId);
        var min = rules.Value.Agency.MinEscrowBalance;
        var sla = rules.Value.Sla;
        return new EscrowStateDto(
            agency.Balance,
            agency.Status,
            min,
            AgencyEligibility.HasEscrowWarning(agency.Status, agency.Balance, min),
            agency.SlaScore,
            agency.SlaScore <= sla.WarnAtOrBelow,
            agency.Status != AgencyStatuses.Active || agency.SlaScore < sla.BlockPremiumBelow);
    }

    public async Task<(IReadOnlyList<EscrowTransactionDto> Items, int Total)> ListTransactionsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var skip = (Math.Max(1, page) - 1) * size;
        var (items, total) = await repository.ListAsync(agencyId, skip, size, cancellationToken);
        return (items.Select(t => ToDto(t, clock.UtcNow)).ToList(), total);
    }

    /// <summary>Starts a top-up: a MoMo QR for <c>amount</c> (whole VND, positive); the IPN credits the escrow.</summary>
    public async Task<DepositPurchaseDto> StartDepositAsync(DepositRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        if (request.Amount is not { } amount || amount <= 0 || amount != decimal.Truncate(amount))
        {
            throw new ValidationException("amount", "amount must be a positive whole number of VND.");
        }

        _ = await repository.GetAgencyAsync(agencyId, cancellationToken) ?? throw new NotFoundException("Agency", agencyId);

        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(rules.Value.Payments.QrExpiryMinutes);
        PaymentQr qr;
        try
        {
            qr = await gateway.CreateQrAsync(
                new CreatePaymentRequest(
                    PaymentPurpose.EscrowDeposit,
                    agencyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    amount,
                    $"Escrow deposit - agency {agencyId}",
                    expiresAt),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PaymentGatewayUnavailableException("The payment gateway did not create the QR.", ex);
        }

        var transaction = new PaymentTransaction
        {
            GatewayTxnRef = qr.GatewayTxnRef,
            AgencyId = agencyId,
            Purpose = PaymentPurpose.EscrowDeposit,
            Gateway = "MOMO",
            Amount = amount,
            TxnStatus = PaymentStatus.Pending,
            QrPayload = qr.QrPayload,
            CreatedAt = now,
        };
        payments.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new DepositPurchaseDto(transaction.PaymentId, qr.GatewayTxnRef, amount, qr.QrPayload, qr.PayUrl, qr.ExpiresAtUtc);
    }

    /// <summary>Appeals one PENALTY row within <c>Sla.AppealHours</c> (decisions Q09); the Admin decides it.</summary>
    public async Task<EscrowAppealDto> AppealAsync(long escrowTxnId, AppealRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500)
        {
            throw new ValidationException("reason", "reason is required and must be at most 500 characters.");
        }

        var penalty = await repository.GetTransactionAsync(escrowTxnId, cancellationToken);
        if (penalty is null || penalty.AgencyId != agencyId)
        {
            throw new NotFoundException("Escrow transaction", escrowTxnId);
        }

        if (penalty.TxnType != EscrowTransactionType.Penalty)
        {
            throw new BusinessRuleViolationException("Only a penalty can be appealed.", EscrowErrorCodes.NotAPenalty);
        }

        var now = clock.UtcNow;
        if (now - penalty.CreatedAt > TimeSpan.FromHours(rules.Value.Sla.AppealHours))
        {
            throw new BusinessRuleViolationException(
                $"A penalty can be appealed within {rules.Value.Sla.AppealHours} hours.", EscrowErrorCodes.AppealWindowClosed);
        }

        if (await repository.FindAppealByTransactionAsync(escrowTxnId, cancellationToken) is not null
            || await repository.HasReversalAsync(escrowTxnId, cancellationToken))
        {
            throw new BusinessRuleViolationException("This penalty was already appealed or reversed.", EscrowErrorCodes.AppealExists);
        }

        var appeal = new EscrowAppeal
        {
            EscrowTxnId = escrowTxnId,
            AgencyId = agencyId,
            Reason = reason,
            Status = AppealStatuses.Pending,
            CreatedAt = now,
        };
        repository.AddAppeal(appeal);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(appeal);
    }

    // ---- Admin

    public async Task<IReadOnlyList<EscrowAppealDto>> ListAppealsAsync(string? status, CancellationToken cancellationToken = default)
    {
        _ = AdminId;
        var wanted = string.IsNullOrWhiteSpace(status) ? AppealStatuses.Pending : status.Trim().ToUpperInvariant();
        return (await repository.ListAppealsAsync(wanted, 100, cancellationToken)).Select(ToDto).ToList();
    }

    /// <summary>
    /// Admin decision (decisions Q09, G-5): ACCEPT appends a REVERSAL row that gives back the money and the points of the penalty and may
    /// return a SUSPENDED agency to ACTIVE; REJECT only records the decision. Idempotent: a decided appeal is a 409.
    /// </summary>
    public async Task<EscrowAppealDto> DecideAppealAsync(int appealId, AppealDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var adminId = AdminId;
        if (request.Accept is not { } accept)
        {
            throw new ValidationException("accept", "accept is required.");
        }

        var note = request.Note?.Trim();
        if (note is { Length: > 255 })
        {
            throw new ValidationException("note", "note must be at most 255 characters.");
        }

        EscrowAppeal? decided = null;
        for (var attempt = 0; attempt < MaxAttempts && decided is null; attempt++)
        {
            decided = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var appeal = await repository.GetAppealForUpdateAsync(appealId, cancellationToken)
                    ?? throw new NotFoundException("Appeal", appealId);
                if (appeal.Status != AppealStatuses.Pending)
                {
                    throw new BusinessRuleViolationException("The appeal was already decided.", EscrowErrorCodes.AppealDecided);
                }

                var now = clock.UtcNow;
                if (accept)
                {
                    var penalty = await repository.GetTransactionAsync(appeal.EscrowTxnId, cancellationToken)
                        ?? throw new NotFoundException("Escrow transaction", appeal.EscrowTxnId);
                    if (await repository.HasReversalAsync(penalty.EscrowTxnId, cancellationToken))
                    {
                        throw new BusinessRuleViolationException("The penalty was already reversed.", EscrowErrorCodes.AlreadyReversed);
                    }

                    var agency = await repository.GetAgencyAsync(penalty.AgencyId, cancellationToken)
                        ?? throw new NotFoundException("Agency", penalty.AgencyId);
                    var newBalance = agency.Balance + penalty.Amount;
                    var points = -(penalty.SlaPointsDelta ?? 0m);
                    var newScore = agency.SlaScore + points;
                    var newStatus = EscrowRules.StatusAfterCredit(agency.Status, newBalance, rules.Value.Agency.MinEscrowBalance);
                    if (!await repository.TryUpdateAgencyAsync(agency.AgencyId, agency.Balance, newBalance, newScore, newStatus, cancellationToken))
                    {
                        return null; // a concurrent change: retried below
                    }

                    repository.AddTransaction(new EscrowTransaction
                    {
                        AgencyId = agency.AgencyId,
                        TxnType = EscrowTransactionType.Reversal,
                        Amount = penalty.Amount,
                        BalanceAfter = newBalance,
                        SlaPointsDelta = points,
                        OrderId = penalty.OrderId,
                        DisputeId = penalty.DisputeId,
                        Reason = Truncate($"Appeal {appeal.AppealId} accepted: {note ?? "reversed"}", 255),
                        CreatedByAdminId = adminId,
                        CreatedAt = now,
                    });
                }

                appeal.Status = accept ? AppealStatuses.Accepted : AppealStatuses.Rejected;
                appeal.DecidedByAdminId = adminId;
                appeal.DecidedAt = now;
                appeal.DecisionNote = note;
                await auditLog.WriteAsync(
                    new AuditEntry(AuditActorType.Admin, adminId, "ESCROW_APPEAL", appealId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "status", AppealStatuses.Pending, appeal.Status, note),
                    cancellationToken);
                return appeal;
            }, cancellationToken);
        }

        return decided is null
            ? throw new BusinessRuleViolationException("The escrow changed while the appeal was decided; try again.")
            : ToDto(decided);
    }

    // ---- ISlaPenaltyService (M3 Dispatch, M6 Disputes)

    public async Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default)
    {
        var reason = Truncate($"{request.Violation.ToString().ToUpperInvariant()}: {request.Reason}", 255);
        var points = EscrowRules.Points(request.Violation, rules.Value.Sla.Penalty);

        // Idempotent: the same violation of the same order / dispute is applied once (a repeated event or retry).
        if (request.OrderId is not null || request.DisputeId is not null)
        {
            var existing = await repository.FindPenaltyAsync(request.AgencyId, request.OrderId, request.DisputeId, reason, cancellationToken);
            if (existing is not null)
            {
                var current = await repository.GetAgencyAsync(request.AgencyId, cancellationToken);
                return new SlaPenaltyResult(
                    existing.SlaPointsDelta ?? -points, current?.SlaScore ?? 0m, existing.Amount,
                    Math.Max(0m, request.CustomerRefundAmount + request.RescueCost - existing.Amount),
                    current?.Status == AgencyStatuses.Suspended);
            }
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var result = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var agency = await repository.GetAgencyAsync(request.AgencyId, cancellationToken)
                    ?? throw new NotFoundException("Agency", request.AgencyId);
                var (deducted, shortfall, newBalance, newStatus) = EscrowRules.Penalize(
                    agency.Balance, request.CustomerRefundAmount + request.RescueCost, agency.Status);
                var newScore = Math.Max(0m, agency.SlaScore - points);
                if (!await repository.TryUpdateAgencyAsync(agency.AgencyId, agency.Balance, newBalance, newScore, newStatus, cancellationToken))
                {
                    return null;
                }

                repository.AddTransaction(new EscrowTransaction
                {
                    AgencyId = agency.AgencyId,
                    TxnType = EscrowTransactionType.Penalty,
                    Amount = deducted,
                    BalanceAfter = newBalance,
                    SlaPointsDelta = -(agency.SlaScore - newScore),
                    OrderId = request.OrderId,
                    DisputeId = request.DisputeId,
                    Reason = shortfall > 0 ? Truncate($"{reason} (shortfall {shortfall:0.##})", 255) : reason,
                    CreatedAt = clock.UtcNow,
                });
                return new SlaPenaltyResult(-(agency.SlaScore - newScore), newScore, deducted, shortfall, newStatus == AgencyStatuses.Suspended);
            }, cancellationToken);

            if (result is not null)
            {
                if (result.AgencySuspended || result.Shortfall > 0)
                {
                    _logger.LogWarning(
                        "Agency {AgencyId}: penalty {Violation} deducted {Deducted}, shortfall {Shortfall}, suspended {Suspended}.",
                        request.AgencyId, request.Violation, result.EscrowDeducted, result.Shortfall, result.AgencySuspended);
                }

                return result;
            }
        }

        throw new BusinessRuleViolationException("The escrow changed repeatedly while the penalty was applied; try again.");
    }

    // ---- shared with the deposit settlement

    /// <summary>
    /// Credits the balance and appends the DEPOSIT row INSIDE the caller's open transaction (the payment is marked paid in the same one, so
    /// "paid" and "credited" cannot diverge). False when a concurrent change won the optimistic update: the caller rolls back and retries.
    /// An unknown agency is logged and counts as done (nothing to credit).
    /// </summary>
    internal async Task<bool> CreditInOpenTransactionAsync(int agencyId, decimal amount, long paymentId, string reason, CancellationToken cancellationToken)
    {
        var agency = await repository.GetAgencyAsync(agencyId, cancellationToken);
        if (agency is null)
        {
            _logger.LogError("Escrow deposit payment {PaymentId} for unknown agency {AgencyId}.", paymentId, agencyId);
            return true;
        }

        var newBalance = agency.Balance + amount;
        var newStatus = EscrowRules.StatusAfterCredit(agency.Status, newBalance, rules.Value.Agency.MinEscrowBalance);
        if (!await repository.TryUpdateAgencyAsync(agencyId, agency.Balance, newBalance, agency.SlaScore, newStatus, cancellationToken))
        {
            return false;
        }

        repository.AddTransaction(new EscrowTransaction
        {
            AgencyId = agencyId,
            TxnType = EscrowTransactionType.Deposit,
            Amount = amount,
            BalanceAfter = newBalance,
            PaymentId = paymentId,
            Reason = Truncate(reason, 255),
            CreatedAt = clock.UtcNow,
        });
        return true;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private EscrowTransactionDto ToDto(EscrowTransaction t, DateTime now) => new(
        t.EscrowTxnId, t.TxnType.ToString().ToUpperInvariant(), t.Amount, t.BalanceAfter, t.SlaPointsDelta, t.OrderId, t.DisputeId,
        t.PaymentId, t.Reason, t.CreatedAt,
        CanAppeal: t.TxnType == EscrowTransactionType.Penalty && now - t.CreatedAt <= TimeSpan.FromHours(rules.Value.Sla.AppealHours));

    private static EscrowAppealDto ToDto(EscrowAppeal a) =>
        new(a.AppealId, a.EscrowTxnId, a.AgencyId, a.Reason, a.Status, a.CreatedAt, a.DecidedAt, a.DecisionNote);
}

/// <summary>
/// Settles an ESCROW_DEPOSIT payment from the IPN (<see cref="IPaymentPurposeSettlement"/>): PENDING (or late EXPIRED) -&gt; SUCCESS and
/// the balance credited with one DEPOSIT ledger row in one transaction; PENDING or SUSPENDED agencies become ACTIVE at the minimum
/// (decisions Q09, Q25 R3/R4). Idempotent.
/// </summary>
public sealed class EscrowDepositSettlement(
    IPaymentRepository payments,
    EscrowService escrow,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<EscrowDepositSettlement>? logger = null) : IPaymentPurposeSettlement
{
    private readonly ILogger<EscrowDepositSettlement> _logger = logger ?? NullLogger<EscrowDepositSettlement>.Instance;

    public PaymentPurpose Purpose => PaymentPurpose.EscrowDeposit;

    public async Task<bool> SettleSuccessAsync(PaymentTransaction transaction, bool wasExpired, string rawBody, CancellationToken cancellationToken = default)
    {
        if (transaction.AgencyId is not int agencyId)
        {
            _logger.LogError("Escrow deposit payment {PaymentId} has no agency id.", transaction.PaymentId);
            return false;
        }

        var paidAt = clock.UtcNow;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                // "Paid" and "credited" are one transaction: a lost optimistic update rolls the mark back and the loop tries again.
                return await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var marked = wasExpired
                        ? await payments.TryMarkSuccessFromExpiredAsync(transaction.PaymentId, paidAt, rawBody, cancellationToken)
                        : await payments.TryMarkSuccessAsync(transaction.PaymentId, paidAt, rawBody, cancellationToken);
                    if (!marked)
                    {
                        return false; // a repeat or a lost race: already settled
                    }

                    if (!await escrow.CreditInOpenTransactionAsync(
                            agencyId, transaction.Amount, transaction.PaymentId, $"Escrow deposit {transaction.GatewayTxnRef}", cancellationToken))
                    {
                        throw new EscrowRaceException();
                    }

                    return true;
                }, cancellationToken);
            }
            catch (EscrowRaceException)
            {
                // roll back done by the unit of work; read the balance again
            }
        }

        _logger.LogError(
            "Escrow deposit payment {PaymentId} of agency {AgencyId} could not be credited after repeated races; it is still PENDING and the IPN can be replayed.",
            transaction.PaymentId, agencyId);
        return false;
    }

    /// <summary>provenance: a lost optimistic update re-reads; five writers on one agency at once is far beyond real traffic.</summary>
    private const int MaxAttempts = 5;

    private sealed class EscrowRaceException : Exception;
}
