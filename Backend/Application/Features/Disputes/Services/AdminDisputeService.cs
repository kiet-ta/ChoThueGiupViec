using System.Globalization;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Disputes.Services;

public interface IAdminDisputeService
{
    /// <summary>Every argument is the raw query-string text so that a bad value becomes a 400 with field messages (decision O5).</summary>
    Task<DisputeResult<DisputePageDto>> SearchAsync(
        string? status, string? priority, string? nearSla, string? page, string? pageSize, CancellationToken cancellationToken = default);

    Task<DisputeResult<DisputeCaseFileDto>> GetAsync(int disputeId, CancellationToken cancellationToken = default);

    /// <summary>OPEN to IN_REVIEW; the admin is recorded as the handler in <c>resolved_by</c> (contract disputes.md 2.3).</summary>
    Task<DisputeResult<AdminDisputeDto>> TakeAsync(int adminId, int disputeId, CancellationToken cancellationToken = default);
}

/// <summary>Admin queue, case file and take of disputes (contract disputes.md 2.2-2.3, BE-M6-02a).</summary>
public sealed class AdminDisputeService(IDisputeRepository disputes, IClock clock, IOptions<DisputeOptions> options) : IAdminDisputeService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<DisputeResult<DisputePageDto>> SearchAsync(
        string? status, string? priority, string? nearSla, string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        var opt = options.Value;
        var now = clock.UtcNow;

        var statuses = ParseStatuses(status, errors);
        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, DefaultPageSize, 1, MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {MaxPageSize}.", errors);

        var near = false;
        if (!string.IsNullOrWhiteSpace(nearSla))
        {
            if (!bool.TryParse(nearSla.Trim(), out near)) errors["nearSla"] = ["nearSla must be true or false."];
        }

        DateTime? dueFrom = null;
        DateTime? dueBefore = null;
        var normalizedPriority = string.IsNullOrWhiteSpace(priority) ? null : priority.Trim().ToUpperInvariant();
        switch (normalizedPriority)
        {
            case null:
                break;
            case DisputeConstants.PriorityHigh:
                dueBefore = now.AddHours(opt.PriorityHighHours);
                break;
            case DisputeConstants.PriorityMedium:
                dueFrom = now.AddHours(opt.PriorityHighHours);
                dueBefore = now.AddHours(opt.PriorityMediumHours);
                break;
            case DisputeConstants.PriorityLow:
                dueFrom = now.AddHours(opt.PriorityMediumHours);
                break;
            default:
                errors["priority"] = ["Priority must be HIGH, MEDIUM or LOW."];
                break;
        }

        if (errors.Count > 0) return DisputeResult<DisputePageDto>.ValidationError(errors);

        // A priority or the nearSla flag only makes sense for tickets nobody has decided yet.
        if (normalizedPriority is not null || near)
        {
            statuses = statuses.Where(s => DisputeConstants.Unresolved.Contains(s)).ToList();
        }

        if (near)
        {
            var nearBefore = now.AddHours(opt.NearSlaHours);
            dueBefore = dueBefore is { } existing && existing < nearBefore ? existing : nearBefore;
        }

        var (tickets, total) = await disputes.SearchAsync(new DisputeQueueFilter(statuses, dueFrom, dueBefore), pageNumber, size, cancellationToken);
        var summaries = await BuildSummariesAsync(tickets, now, cancellationToken);

        return DisputeResult<DisputePageDto>.Ok(new DisputePageDto { Items = summaries, Page = pageNumber, PageSize = size, Total = total });
    }

    public async Task<DisputeResult<DisputeCaseFileDto>> GetAsync(int disputeId, CancellationToken cancellationToken = default)
    {
        var ticket = await disputes.FindTrackedAsync(disputeId, cancellationToken);
        if (ticket is null) return DisputeResult<DisputeCaseFileDto>.NotFound();

        var now = clock.UtcNow;
        var summary = (await BuildSummariesAsync([ticket], now, cancellationToken)).Single();
        var data = await disputes.GetCaseDataAsync(ticket.OrderId, cancellationToken);

        return DisputeResult<DisputeCaseFileDto>.Ok(new DisputeCaseFileDto
        {
            Dispute = DisputeMapper.ToAdminDto(ticket),
            Summary = summary,
            ShiftTimeline = BuildTimeline(ticket, data),
            Checklist = null,
            Photos = data.Photos
                .OrderBy(p => p.AssignmentId).ThenBy(p => p.Phase, StringComparer.Ordinal).ThenBy(p => p.AngleNo)
                .Select(p => new DisputePhotoDto
                {
                    AssignmentId = p.AssignmentId,
                    Phase = p.Phase,
                    AngleNo = p.AngleNo,
                    Url = p.Url,
                    VolScore = p.VolScore,
                    IsAccepted = p.IsAccepted,
                })
                .ToList(),
        });
    }

    public async Task<DisputeResult<AdminDisputeDto>> TakeAsync(int adminId, int disputeId, CancellationToken cancellationToken = default)
    {
        var ticket = await disputes.FindTrackedAsync(disputeId, cancellationToken);
        if (ticket is null) return DisputeResult<AdminDisputeDto>.NotFound();
        if (ticket.DisputeStatus != DisputeConstants.Open) return DisputeResult<AdminDisputeDto>.Conflict("The dispute is not open.");

        ticket.DisputeStatus = DisputeConstants.InReview;
        ticket.ResolvedBy = adminId; // the handler; the contract uses this column for it until the verdict
        await disputes.SaveAsync(cancellationToken);
        return DisputeResult<AdminDisputeDto>.Ok(DisputeMapper.ToAdminDto(ticket));
    }

    private async Task<List<DisputeSummaryDto>> BuildSummariesAsync(
        IReadOnlyList<DisputeTicket> tickets, DateTime now, CancellationToken cancellationToken)
    {
        if (tickets.Count == 0) return [];
        var data = await disputes.GetSummaryDataAsync(tickets.Select(t => t.OrderId).Distinct().ToList(), cancellationToken);

        return tickets.Select(t =>
        {
            data.TryGetValue(t.OrderId, out var d);
            return new DisputeSummaryDto
            {
                DisputeId = t.DisputeId,
                OrderId = t.OrderId,
                OrderCode = d?.OrderCode ?? string.Empty,
                CustomerName = d?.CustomerName ?? string.Empty,
                Workers = (d?.Workers ?? []).Select(w => new DisputeWorkerDto
                {
                    WorkerId = w.WorkerId,
                    FullName = w.FullName,
                    WorkerType = DbEnum.ToDb(w.WorkerType),
                    AgencyName = w.AgencyName,
                }).ToList(),
                RaisedBy = t.RaisedBy,
                Category = t.Category,
                Priority = DisputeMapper.PriorityOf(t, now, options.Value),
                SlaDueAt = DateTime.SpecifyKind(t.SlaDueAt, DateTimeKind.Utc),
                SlaSecondsRemaining = DisputeMapper.SecondsRemaining(t, now),
                DisputeStatus = t.DisputeStatus,
                AutoCancelled = t.Category == "ABSENT_FEE",
            };
        }).ToList();
    }

    private static List<ShiftTimelineEntryDto> BuildTimeline(DisputeTicket ticket, DisputeCaseData data)
    {
        var entries = new List<ShiftTimelineEntryDto>();

        foreach (var c in data.CheckIns)
        {
            entries.Add(new ShiftTimelineEntryDto
            {
                At = Utc(c.CheckedInAtUtc),
                Type = "CHECK_IN",
                GpsVerified = c.GpsVerified,
                DistanceM = c.DistanceM,
                Detail = string.Create(CultureInfo.InvariantCulture, $"Check-in at {c.DistanceM:0.#} m from the address"),
            });
        }

        foreach (var p in data.Photos.Where(p => p.Phase == "AFTER"))
        {
            entries.Add(new ShiftTimelineEntryDto
            {
                At = Utc(p.CapturedAtUtc),
                Type = "PHOTO_AFTER",
                VolScore = p.VolScore,
                Detail = $"After photo, angle {p.AngleNo}, {(p.IsAccepted ? "accepted" : "rejected")}",
            });
        }

        entries.Add(new ShiftTimelineEntryDto
        {
            At = Utc(ticket.CreatedAt),
            Type = "CUSTOMER_DISPUTED",
            Detail = $"Dispute filed by {ticket.RaisedBy}",
        });

        foreach (var done in data.Completions)
        {
            entries.Add(new ShiftTimelineEntryDto { At = Utc(done.CompletedAtUtc), Type = "CHECK_OUT", Detail = "Job completed" });
        }

        return entries.OrderBy(e => e.At).ToList();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static List<string> ParseStatuses(string? raw, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [.. DisputeConstants.Unresolved];

        var statuses = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = DisputeConstants.Statuses.FirstOrDefault(s => string.Equals(s, part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                errors["status"] = [$"Status must be a comma-separated list of: {string.Join(", ", DisputeConstants.Statuses)}."];
                return [];
            }

            if (!statuses.Contains(match)) statuses.Add(match);
        }

        return statuses;
    }

    private static int ParseInt(string? raw, int fallback, int min, int max, string field, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors[field] = [message];
        return fallback;
    }
}
