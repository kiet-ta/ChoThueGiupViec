namespace CommonService.Application.Features.Booking.Services;

/// <summary>The <c>PriceRule</c> shape of contract booking.md section 2 (serviceTier is ECONOMY or PREMIUM, times in UTC).</summary>
public sealed record PriceRuleDto(int RuleId, string ServiceTier, string AreaBracket, decimal UnitPrice, bool IsActive, DateTime UpdatedAt, int? UpdatedBy);

/// <summary>Body of PUT /api/admin/price-rules/{ruleId} (contract booking.md 4.2).</summary>
public sealed record UpdatePriceRuleRequest(decimal UnitPrice, string? Reason);

/// <summary>Admin edits of the price table (BE-M2-02a, decisions Q01, G-5).</summary>
public interface IPriceRuleAdminService
{
    /// <summary>All rules, ordered by service tier then area bracket.</summary>
    Task<IReadOnlyList<PriceRuleDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the unit price and writes one audit row in the same transaction. Applies to NEW orders only.
    /// ValidationException (400), NotFoundException (404). The same price as the current one changes nothing and writes nothing.
    /// </summary>
    Task<PriceRuleDto> UpdateUnitPriceAsync(int adminId, int ruleId, decimal unitPrice, string reason, CancellationToken cancellationToken = default);
}
