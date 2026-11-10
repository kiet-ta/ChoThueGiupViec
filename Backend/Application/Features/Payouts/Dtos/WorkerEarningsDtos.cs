namespace CommonService.Application.Features.Payouts.Dtos;

/// <summary>One job (or absence fee) of the month in the worker's income screen.</summary>
public class WorkerEarningJobDto
{
    public long AssignmentId { get; init; }
    public long OrderId { get; init; }
    public DateTime CompletedAt { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal CommissionAmount { get; init; }

    /// <summary><c>grossAmount - commissionAmount</c>.</summary>
    public decimal NetAmount { get; init; }

    /// <summary>True for an approved customer-absence fee (paid in full, no commission).</summary>
    public bool AbsenceFee { get; init; }
}

/// <summary>The freelancer's income of one month (contract payouts.md 2.5).</summary>
public class WorkerEarningsDto
{
    public string PeriodMonth { get; init; } = string.Empty;
    public int JobCount { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal CommissionAmount { get; init; }
    public decimal PenaltyAmount { get; init; }

    /// <summary>What the worker withdrew during the month (the advance, decision F).</summary>
    public decimal AdvanceAmount { get; init; }

    /// <summary>The wallet balance carried in from the earlier months (negative when a deduction was larger than the month).</summary>
    public decimal CarryInAmount { get; init; }

    public decimal NetAmount { get; init; }

    /// <summary>NOT_BUILT (no batch yet), PENDING (a DRAFT batch) or TRANSFERRED (a CLOSED batch).</summary>
    public string PayoutStatus { get; init; } = string.Empty;

    public List<WorkerEarningJobDto> Jobs { get; init; } = [];
}

public class WorkerPayoutHistoryItemDto
{
    public int BatchId { get; init; }
    public string PeriodMonth { get; init; } = string.Empty;
    public decimal AdvanceAmount { get; init; }
    public decimal CarryInAmount { get; init; }
    public decimal NetAmount { get; init; }
    public string ItemStatus { get; init; } = string.Empty;
    public DateTime? TransferredAt { get; init; }
}

public class WorkerPayoutHistoryPageDto
{
    public List<WorkerPayoutHistoryItemDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}
