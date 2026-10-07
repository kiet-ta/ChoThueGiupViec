using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Disputes.Services;

/// <summary>Who files: a customer (on their own order) or a worker (with an assignment on the order).</summary>
public enum DisputeSide
{
    Customer,
    Worker,
}

public interface IDisputeFilingService
{
    Task<DisputeResult<DisputeDto>> FileAsync(
        DisputeSide side, int callerId, FileDisputeRequestDto request, CancellationToken cancellationToken = default);

    Task<DisputeResult<IReadOnlyList<DisputeDto>>> ListAsync(DisputeSide side, int callerId, CancellationToken cancellationToken = default);

    Task<DisputeResult<DisputeDto>> GetAsync(DisputeSide side, int callerId, int disputeId, CancellationToken cancellationToken = default);
}

/// <summary>Filing and own lists of disputes (contract disputes.md 2.1, PRD 4.3 step 1, BE-M6-02a).</summary>
public sealed class DisputeFilingService(IDisputeRepository disputes, IClock clock, IOptions<DisputeOptions> options) : IDisputeFilingService
{
    private static readonly JobAssignmentStatus[] DisputableStatuses =
    [
        JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed, JobAssignmentStatus.Absent,
    ];

    public async Task<DisputeResult<DisputeDto>> FileAsync(
        DisputeSide side, int callerId, FileDisputeRequestDto request, CancellationToken cancellationToken = default)
    {
        var errors = Validate(request, out var orderId, out var category, out var description, out var evidence);
        if (errors.Count > 0) return DisputeResult<DisputeDto>.ValidationError(errors);

        var order = await disputes.GetOrderAsync(orderId, cancellationToken);
        if (order is null || !IsOnOrder(side, callerId, order)) return DisputeResult<DisputeDto>.NotFound();

        var eligible = order.Assignments.Where(a => DisputableStatuses.Contains(a.Status)).ToList();
        if (eligible.Count == 0) return DisputeResult<DisputeDto>.Conflict("There is nothing to dispute on this order yet.");

        var now = clock.UtcNow;
        var windowEnd = eligible.Max(a => Later(a.CompletedAtUtc, clock.ToUtc(a.ShiftEndLocal))).AddHours(options.Value.FileWindowHours);
        if (now > windowEnd) return DisputeResult<DisputeDto>.Conflict("The time to file a dispute for this order has passed.");

        var ticket = new DisputeTicket
        {
            OrderId = orderId,
            RaisedBy = side == DisputeSide.Customer ? DisputeConstants.RaisedByCustomer : DisputeConstants.RaisedByWorker,
            Category = category,
            Description = description,
            EvidenceUrls = DisputeMapper.SerializeEvidence(evidence),
            DisputeStatus = DisputeConstants.Open,
            SlaDueAt = now.AddHours(options.Value.SlaHours),
            CreatedAt = now,
        };

        // DISPUTE_TICKET has one row per order (unique index), so this also settles two simultaneous filings.
        if (!await disputes.TryAddAsync(ticket, cancellationToken))
        {
            return DisputeResult<DisputeDto>.Conflict("A dispute already exists for this order.");
        }

        return DisputeResult<DisputeDto>.Created(DisputeMapper.ToDto(ticket));
    }

    public async Task<DisputeResult<IReadOnlyList<DisputeDto>>> ListAsync(
        DisputeSide side, int callerId, CancellationToken cancellationToken = default)
    {
        var tickets = side == DisputeSide.Customer
            ? await disputes.ListForCustomerAsync(callerId, cancellationToken)
            : await disputes.ListForWorkerAsync(callerId, cancellationToken);
        return DisputeResult<IReadOnlyList<DisputeDto>>.Ok(tickets.Select(DisputeMapper.ToDto).ToList());
    }

    public async Task<DisputeResult<DisputeDto>> GetAsync(
        DisputeSide side, int callerId, int disputeId, CancellationToken cancellationToken = default)
    {
        var ticket = side == DisputeSide.Customer
            ? await disputes.GetForCustomerAsync(disputeId, callerId, cancellationToken)
            : await disputes.GetForWorkerAsync(disputeId, callerId, cancellationToken);
        return ticket is null ? DisputeResult<DisputeDto>.NotFound() : DisputeResult<DisputeDto>.Ok(DisputeMapper.ToDto(ticket));
    }

    private static bool IsOnOrder(DisputeSide side, int callerId, DisputeOrderInfo order) =>
        side == DisputeSide.Customer ? order.CustomerId == callerId : order.Assignments.Any(a => a.WorkerId == callerId);

    private static DateTime Later(DateTime? a, DateTime b) => a is { } value && value > b ? value : b;

    private static Dictionary<string, string[]> Validate(
        FileDisputeRequestDto request, out long orderId, out string category, out string description, out List<string> evidence)
    {
        var errors = new Dictionary<string, string[]>();
        orderId = 0;

        if (request.OrderId is not { } id || id <= 0) errors["orderId"] = ["The order id is required."];
        else orderId = id;

        category = (request.Category ?? string.Empty).Trim();
        if (!DisputeConstants.Categories.Contains(category, StringComparer.Ordinal))
        {
            errors["category"] = [$"Category must be one of: {string.Join(", ", DisputeConstants.Categories)}."];
        }

        description = (request.Description ?? string.Empty).Trim();
        if (description.Length == 0) errors["description"] = ["A description is required."];
        else if (description.Length > DisputeConstants.MaxDescriptionLength)
        {
            errors["description"] = [$"The description must be at most {DisputeConstants.MaxDescriptionLength} characters."];
        }

        evidence = (request.EvidenceUrls ?? []).Select(u => (u ?? string.Empty).Trim()).ToList();
        if (evidence.Count == 0) errors["evidenceUrls"] = ["At least one piece of evidence is required (PRD 4.3)."];
        else if (evidence.Count > DisputeConstants.MaxEvidenceCount)
        {
            errors["evidenceUrls"] = [$"At most {DisputeConstants.MaxEvidenceCount} pieces of evidence are allowed."];
        }
        else if (evidence.Any(u => u.Length == 0 || u.Length > DisputeConstants.MaxEvidenceUrlLength))
        {
            errors["evidenceUrls"] = [$"Every evidence entry must be 1-{DisputeConstants.MaxEvidenceUrlLength} characters."];
        }

        return errors;
    }
}
