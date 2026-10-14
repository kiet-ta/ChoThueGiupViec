using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.ValueObjects;
using MediatR;

namespace CommonService.Application.Features.Disputes.Services;

public interface IDisputeVerdictService
{
    /// <summary>The Admin's verdict (contract disputes.md 2.3): decide the ticket, move the money, audit, then publish <see cref="DisputeResolved"/>.</summary>
    Task<DisputeResult<AdminDisputeDto>> ResolveAsync(
        int adminId, int disputeId, ResolveDisputeRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verdict of a dispute (BE-M6-02b). Everything that must stay consistent (the ticket, the refund, the SLA penalty, the audit row)
/// runs in ONE unit of work; a refusal of a port undoes the whole thing and the ticket stays open.
/// </summary>
public sealed class DisputeVerdictService(
    IDisputeRepository disputes,
    IRefundService refunds,
    ISlaPenaltyService slaPenalties,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher) : IDisputeVerdictService
{
    public const int MaxNoteLength = 255; // ADMIN_AUDIT_LOG.reason NVARCHAR(255)

    private const string AuditEntityType = "DISPUTE_TICKET";
    private const string AuditStatusField = "dispute_status";
    private const string AuditLockField = "worker_lock_requested";
    private const string AuditWorkerEntityType = "WORKER";
    private const string AuditWorkStatusField = "work_status";

    /// <summary>Thrown inside the unit of work to undo it; never leaves this class.</summary>
    private sealed class VerdictAbortedException(string message) : Exception(message);

    public async Task<DisputeResult<AdminDisputeDto>> ResolveAsync(
        int adminId, int disputeId, ResolveDisputeRequestDto request, CancellationToken cancellationToken = default)
    {
        var ticket = await disputes.FindTrackedAsync(disputeId, cancellationToken);
        if (ticket is null) return DisputeResult<AdminDisputeDto>.NotFound();
        if (!DisputeConstants.Unresolved.Contains(ticket.DisputeStatus))
        {
            return DisputeResult<AdminDisputeDto>.Conflict("The dispute is already decided.");
        }

        var assignments = await disputes.GetVerdictAssignmentsAsync(ticket.OrderId, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        var fault = ParseFault(request.FaultParty, errors);
        var amount = request.CompensationAmount ?? 0m;
        var lockWorker = request.LockWorker ?? false;
        var note = (request.Note ?? string.Empty).Trim();
        var orderTotal = assignments.Sum(a => a.GrossAmount);

        if (note.Length == 0) errors["note"] = ["A note is required."];
        else if (note.Length > MaxNoteLength) errors["note"] = [$"The note must be at most {MaxNoteLength} characters."];

        if (amount < 0) errors["compensationAmount"] = ["The compensation must not be negative."];
        else if (amount != Vnd.Round(amount)) errors["compensationAmount"] = ["The compensation must be a whole number of VND."];
        else if (amount > orderTotal) errors["compensationAmount"] = ["The compensation must not exceed the value of the order's assignments."];
        else if (amount > 0 && fault is not (FaultParty.Freelancer or FaultParty.Agency))
        {
            errors["compensationAmount"] = ["Only a FREELANCER or AGENCY verdict can carry a compensation."];
        }

        if (lockWorker && fault != FaultParty.Freelancer) errors["lockWorker"] = ["A worker can be locked only with a FREELANCER verdict."];

        var responsible = ResponsibleAssignments(fault, assignments);
        if (fault is FaultParty.Freelancer or FaultParty.Agency && responsible.Count == 0)
        {
            errors["faultParty"] = [fault == FaultParty.Freelancer
                ? "The order has no freelancer assignment."
                : "The order has no agency assignment."];
        }

        if (errors.Count > 0) return DisputeResult<AdminDisputeDto>.ValidationError(errors);

        var shares = Allocate(amount, responsible);
        var newStatus = fault is null ? DisputeConstants.Dismissed : DisputeConstants.Resolved;
        var now = clock.UtcNow;
        var previousStatus = ticket.DisputeStatus;
        var absenceFee = ticket.Category == DisputeConstants.AbsentFeeCategory;

        try
        {
            var decided = await unitOfWork.ExecuteInTransactionAsync<DisputeTicket?>(async () =>
            {
                var done = await disputes.TryResolveAsync(disputeId, adminId, newStatus, fault, amount, now, cancellationToken);
                if (done is null) return null; // somebody decided it between our read and now

                if (amount > 0)
                {
                    var refund = await refunds.RefundAsync(
                        new RefundRequest(done.OrderId, amount, $"Dispute {disputeId}: {note}"), cancellationToken);
                    if (!refund.Succeeded) throw new VerdictAbortedException(refund.Message ?? "The refund was refused.");
                }

                // Q09: an upheld quality dispute costs the agency SLA points and escrow. An absence-fee reversal does not (decision D9).
                if (fault == FaultParty.Agency && !absenceFee)
                {
                    foreach (var byAgency in responsible.GroupBy(a => a.AgencyId!.Value))
                    {
                        var agencyShare = byAgency.Sum(a => shares.GetValueOrDefault(a.AssignmentId)); // no entry when the amount is 0
                        await slaPenalties.ApplyAsync(
                            new SlaPenaltyRequest(byAgency.Key, SlaViolation.QualityComplaint, done.OrderId, disputeId, agencyShare, 0m,
                                $"Upheld dispute {disputeId}"),
                            cancellationToken);
                    }
                }

                await audit.WriteAsync(
                    new AuditEntry(AuditActorType.Admin, adminId, AuditEntityType, disputeId.ToString(), AuditStatusField,
                        previousStatus, newStatus, note),
                    cancellationToken);
                if (lockWorker)
                {
                    await audit.WriteAsync(
                        new AuditEntry(AuditActorType.Admin, adminId, AuditEntityType, disputeId.ToString(), AuditLockField,
                            "false", "true", note),
                        cancellationToken);

                    // The request is real: lock the freelancers of the order in the same transaction, one audit row per worker changed.
                    var locked = await disputes.LockFreelancersAsync(
                        responsible.Select(a => a.WorkerId).Distinct().ToList(), cancellationToken);
                    foreach (var workerId in locked)
                    {
                        await audit.WriteAsync(
                            new AuditEntry(AuditActorType.Admin, adminId, AuditWorkerEntityType, workerId.ToString(), AuditWorkStatusField,
                                null, "LOCKED", $"Dispute {disputeId}: {note}"),
                            cancellationToken);
                    }
                }

                return done;
            }, cancellationToken);

            if (decided is null) return DisputeResult<AdminDisputeDto>.Conflict("The dispute is already decided.");

            // After the commit: one event per assignment of the order (the event is per assignment, the ticket per order).
            foreach (var a in assignments)
            {
                await publisher.Publish(
                    new DisputeResolved(disputeId, a.AssignmentId, fault, shares.GetValueOrDefault(a.AssignmentId), note, now),
                    cancellationToken);
            }

            return DisputeResult<AdminDisputeDto>.Ok(DisputeMapper.ToAdminDto(decided));
        }
        catch (VerdictAbortedException ex)
        {
            return DisputeResult<AdminDisputeDto>.BadGateway(ex.Message);
        }
    }

    private static FaultParty? ParseFault(string? raw, Dictionary<string, string[]> errors)
    {
        if (raw is null) return null;
        foreach (var candidate in Enum.GetValues<FaultParty>())
        {
            if (string.Equals(DbEnum.ToDb(candidate), raw.Trim(), StringComparison.OrdinalIgnoreCase)) return candidate;
        }

        errors["faultParty"] = ["faultParty must be FREELANCER, AGENCY, CUSTOMER or null."];
        return null;
    }

    /// <summary>The assignments the verdict blames: the freelancer's (no agency) or the agency's; none for CUSTOMER and a dismissal.</summary>
    private static List<DisputeVerdictAssignment> ResponsibleAssignments(FaultParty? fault, IReadOnlyList<DisputeVerdictAssignment> all) =>
        fault switch
        {
            FaultParty.Freelancer => all.Where(a => a.AgencyId is null).ToList(),
            FaultParty.Agency => all.Where(a => a.AgencyId is not null).ToList(),
            _ => [],
        };

    /// <summary>
    /// Splits the compensation over the blamed assignments by gross value, whole VND, the remainder on the last one so the parts add
    /// up exactly (decision G-2).
    /// </summary>
    private static Dictionary<long, decimal> Allocate(decimal amount, IReadOnlyList<DisputeVerdictAssignment> responsible)
    {
        var shares = new Dictionary<long, decimal>();
        if (amount <= 0 || responsible.Count == 0) return shares;

        var total = responsible.Sum(a => a.GrossAmount);
        var given = 0m;
        for (var i = 0; i < responsible.Count; i++)
        {
            var share = i == responsible.Count - 1
                ? amount - given
                : total > 0 ? Math.Min(Vnd.Round(amount * responsible[i].GrossAmount / total), amount - given) : 0m;
            shares[responsible[i].AssignmentId] = share;
            given += share;
        }

        return shares;
    }
}
