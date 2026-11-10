using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

public partial class LedgerEntry
{
    public long EntryId { get; set; }
    public string EntryCode { get; private set; } = string.Empty;
    public LedgerPartyType FromPartyType { get; set; }
    public int? FromPartyId { get; set; }
    public LedgerPartyType ToPartyType { get; set; }
    public int? ToPartyId { get; set; }
    public decimal Amount { get; set; }
    public LedgerEntryType EntryType { get; set; }
    public LedgerRefType RefType { get; set; }
    public long RefId { get; set; }
    public long? OrderId { get; set; }
    public DateTime? AvailableAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int? CreatedByAdminId { get; set; }
    public string PrevHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
