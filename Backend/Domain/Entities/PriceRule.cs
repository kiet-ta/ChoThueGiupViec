using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table PRICE_RULE. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class PriceRule
{
    /// <summary>price_rule.rule_id INT (PK)</summary>
    public int RuleId { get; set; }

    /// <summary>price_rule.service_tier VARCHAR(10)</summary>
    public ServiceTier ServiceTier { get; set; }

    /// <summary>price_rule.area_bracket VARCHAR(20)</summary>
    public string AreaBracket { get; set; } = string.Empty;

    /// <summary>price_rule.unit_price DECIMAL(18,2)</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>price_rule.is_active BIT</summary>
    public bool IsActive { get; set; }

    /// <summary>price_rule.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>price_rule.updated_by INT NULL</summary>
    public int? UpdatedBy { get; set; }
}
