using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ledger;

public static class LedgerHash
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string Compute(LedgerEntry entry)
    {
        var timestamp = DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc);
        var text = string.Join('|', entry.PrevHash, entry.EntryId.ToString(CultureInfo.InvariantCulture),
            DbEnum.ToDb(entry.FromPartyType), entry.FromPartyId?.ToString(CultureInfo.InvariantCulture) ?? "",
            DbEnum.ToDb(entry.ToPartyType), entry.ToPartyId?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.Amount.ToString("F2", CultureInfo.InvariantCulture), DbEnum.ToDb(entry.EntryType),
            DbEnum.ToDb(entry.RefType), entry.RefId.ToString(CultureInfo.InvariantCulture), timestamp.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
