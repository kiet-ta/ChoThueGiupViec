namespace CommonService.Application.Features.Payouts.Dtos;

/// <summary>Body of POST /api/admin/payout-batches (contract payouts.md 2.1).</summary>
public sealed class BuildPayoutBatchRequestDto
{
    /// <summary><c>YYYY-MM</c>.</summary>
    public string? PeriodMonth { get; init; }
}

public sealed class PayoutBatchDto
{
    public int BatchId { get; init; }
    public string PeriodMonth { get; init; } = string.Empty;

    /// <summary>DRAFT or CLOSED.</summary>
    public string BatchStatus { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }
    public int ItemCount { get; init; }
    public int? ConfirmedBy { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class PayoutItemDto
{
    public int ItemId { get; init; }

    /// <summary>FREELANCER or AGENCY.</summary>
    public string PayeeType { get; init; } = string.Empty;

    public int? WorkerId { get; init; }
    public int? AgencyId { get; init; }
    public string PayeeName { get; init; } = string.Empty;
    public int JobCount { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal CommissionAmount { get; init; }
    public decimal PenaltyAmount { get; init; }
    public decimal NetAmount { get; init; }
    public string? BankName { get; init; }

    /// <summary>Empty when the payee has none (question P4).</summary>
    public string BankAccountNo { get; init; } = string.Empty;

    /// <summary>PENDING or TRANSFERRED.</summary>
    public string ItemStatus { get; init; } = string.Empty;
}

public sealed class PayoutBatchPageDto
{
    public List<PayoutBatchDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}

public sealed class PayoutBatchDetailDto
{
    public PayoutBatchDto Batch { get; init; } = new();
    public List<PayoutItemDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }

    /// <summary>One line per payee without a bank account number (question P4); the whole batch, not only this page.</summary>
    public List<string> Warnings { get; init; } = [];
}
