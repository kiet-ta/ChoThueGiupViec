using System.Globalization;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking.Services;

public sealed class PriceRuleAdminService(
    IPriceRuleRepository rules,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    IClock clock) : IPriceRuleAdminService
{
    /// <summary>PRICE_RULE.unit_price is DECIMAL(18,2); whole VND only, so 16 digits at most.</summary>
    internal const decimal MaxUnitPrice = 9_999_999_999_999_999m;
    internal const int MaxReasonLength = 255;
    internal const string EntityType = "PRICE_RULE";
    internal const string FieldName = "unit_price";

    public async Task<IReadOnlyList<PriceRuleDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await rules.ListAsync(cancellationToken)).Select(ToDto).ToList();

    public async Task<PriceRuleDto> UpdateUnitPriceAsync(int adminId, int ruleId, decimal unitPrice, string reason, CancellationToken cancellationToken = default)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        Validate(unitPrice, cleanReason);

        var rule = await rules.GetForUpdateAsync(ruleId, cancellationToken)
            ?? throw new NotFoundException("PriceRule", ruleId);

        if (rule.UnitPrice == unitPrice)
        {
            return ToDto(rule);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var oldValue = rule.UnitPrice;

            // The audit row joins this unit of work (G-5): it is committed or rolled back with the price.
            await auditLog.WriteAsync(
                new AuditEntry(
                    AuditActorType.Admin, adminId, EntityType, ruleId.ToString(CultureInfo.InvariantCulture), FieldName,
                    Format(oldValue), Format(unitPrice), cleanReason),
                cancellationToken);

            rule.UnitPrice = unitPrice;
            rule.UpdatedAt = clock.UtcNow;
            rule.UpdatedBy = adminId;
            return ToDto(rule);
        }, cancellationToken);
    }

    private static void Validate(decimal unitPrice, string reason)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (unitPrice <= 0 || unitPrice != decimal.Truncate(unitPrice) || unitPrice > MaxUnitPrice)
        {
            errors["unitPrice"] = [$"unitPrice must be a whole VND amount greater than 0 and at most {MaxUnitPrice:0}."];
        }

        if (reason.Length is 0 or > MaxReasonLength)
        {
            errors["reason"] = [$"reason is required and must be at most {MaxReasonLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static string Format(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static PriceRuleDto ToDto(PriceRule rule) => new(
        rule.RuleId, rule.ServiceTier.ToString().ToUpperInvariant(), rule.AreaBracket, rule.UnitPrice,
        rule.IsActive, rule.UpdatedAt, rule.UpdatedBy);
}
