using System.Text.Json;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Disputes.Services;

/// <summary>Entity to DTO mapping and the derived priority, shared by the filing and the admin services.</summary>
public static class DisputeMapper
{
    public static List<string> ParseEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return []; // a malformed legacy value must not break a list
        }
    }

    public static string SerializeEvidence(IEnumerable<string> urls) => JsonSerializer.Serialize(urls);

    public static DisputeDto ToDto(DisputeTicket t) => new()
    {
        DisputeId = t.DisputeId,
        OrderId = t.OrderId,
        RaisedBy = t.RaisedBy,
        Category = t.Category,
        Description = t.Description,
        EvidenceUrls = ParseEvidence(t.EvidenceUrls),
        DisputeStatus = t.DisputeStatus,
        FaultParty = FaultText(t.FaultParty),
        CompensationAmount = t.CompensationAmount,
        SlaDueAt = Utc(t.SlaDueAt),
        ResolvedAt = UtcOrNull(t.ResolvedAt),
        CreatedAt = Utc(t.CreatedAt),
    };

    public static AdminDisputeDto ToAdminDto(DisputeTicket t) => new()
    {
        DisputeId = t.DisputeId,
        OrderId = t.OrderId,
        RaisedBy = t.RaisedBy,
        Category = t.Category,
        Description = t.Description,
        EvidenceUrls = ParseEvidence(t.EvidenceUrls),
        DisputeStatus = t.DisputeStatus,
        FaultParty = FaultText(t.FaultParty),
        CompensationAmount = t.CompensationAmount,
        SlaDueAt = Utc(t.SlaDueAt),
        ResolvedAt = UtcOrNull(t.ResolvedAt),
        CreatedAt = Utc(t.CreatedAt),
        ResolvedBy = t.ResolvedBy,
    };

    private static string? FaultText(FaultParty? fault) => fault is { } f ? DbEnum.ToDb(f) : null;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? UtcOrNull(DateTime? value) => value is { } v ? Utc(v) : null;

    /// <summary>HIGH under <c>PriorityHighHours</c> left (overdue included), MEDIUM under <c>PriorityMediumHours</c>, else LOW; a decided ticket is LOW.</summary>
    public static string PriorityOf(DisputeTicket ticket, DateTime nowUtc, DisputeOptions options)
    {
        if (!DisputeConstants.Unresolved.Contains(ticket.DisputeStatus)) return DisputeConstants.PriorityLow;
        var left = Utc(ticket.SlaDueAt) - nowUtc;
        if (left < TimeSpan.FromHours(options.PriorityHighHours)) return DisputeConstants.PriorityHigh;
        return left < TimeSpan.FromHours(options.PriorityMediumHours) ? DisputeConstants.PriorityMedium : DisputeConstants.PriorityLow;
    }

    /// <summary>Whole seconds to the SLA (negative when overdue); 0 once the ticket is decided.</summary>
    public static long SecondsRemaining(DisputeTicket ticket, DateTime nowUtc) =>
        DisputeConstants.Unresolved.Contains(ticket.DisputeStatus)
            ? (long)Math.Floor((Utc(ticket.SlaDueAt) - nowUtc).TotalSeconds)
            : 0;
}
