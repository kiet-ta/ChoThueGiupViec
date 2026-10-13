using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// In-memory <see cref="IDispatchRepository"/> with the same conditional semantics as <c>EfDispatchRepository</c>: an offer holds its slot
/// (<c>AVAILABLE -&gt; HELD</c>), accept succeeds only once (<c>OFFERED -&gt; ASSIGNED</c>, slot <c>HELD -&gt; BOOKED</c>), close frees the slot.
/// A slot not registered with <see cref="AddSlot"/> is treated as an AVAILABLE slot of the offered worker.
/// </summary>
internal sealed class InMemoryDispatchRepository : IDispatchRepository
{
    private readonly object _gate = new();
    private long _nextId = 100;

    public Dictionary<long, JobAssignment> Assignments { get; } = new();
    public Dictionary<int, (int WorkerId, string Status)> Slots { get; } = new();
    public Dictionary<long, DispatchOrderContext> Orders { get; } = new();

    public void AddSlot(int slotId, int workerId, string status = DispatchSlotStatuses.Available)
    {
        lock (_gate)
        {
            Slots[slotId] = (workerId, status);
        }
    }

    public string SlotStatus(int slotId)
    {
        lock (_gate)
        {
            return Slots[slotId].Status;
        }
    }

    public Task<DispatchOrderContext?> GetOrderContextAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Orders.TryGetValue(orderId, out var order) ? order : null);
        }
    }

    public Task<IReadOnlyCollection<long>> GetWorkerIdsAlreadyOfferedAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<long>>(
                Assignments.Values.Where(a => a.OrderId == orderId).Select(a => (long)a.WorkerId).Distinct().ToList());
        }
    }

    public Task<JobAssignment?> TryCreateOfferAsync(JobAssignment offer, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Slots.TryGetValue(offer.SlotId, out var slot))
            {
                slot = (offer.WorkerId, DispatchSlotStatuses.Available);
            }

            if (slot.Status != DispatchSlotStatuses.Available)
            {
                return Task.FromResult<JobAssignment?>(null);
            }

            Slots[offer.SlotId] = (slot.WorkerId, DispatchSlotStatuses.Held);
            offer.AssignmentId = _nextId++;
            Assignments[offer.AssignmentId] = offer;
            return Task.FromResult<JobAssignment?>(offer);
        }
    }

    public Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Assignments.TryGetValue(assignmentId, out var a) ? a : null);
        }
    }

    public Task<JobAssignment?> GetOpenOfferForWorkerAsync(int workerId, DateTime offeredAfterUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Assignments.Values
                .Where(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Offered && a.CreatedAt > offeredAfterUtc)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault());
        }
    }

    public Task<bool> TryAcceptOfferAsync(long assignmentId, int workerId, DateTime offeredAfterUtc, DateTime acceptedAtUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Assignments.TryGetValue(assignmentId, out var a)
                || a.WorkerId != workerId
                || a.AssignmentStatus != JobAssignmentStatus.Offered
                || a.CreatedAt <= offeredAfterUtc
                || !Slots.TryGetValue(a.SlotId, out var slot)
                || slot.Status != DispatchSlotStatuses.Held)
            {
                return Task.FromResult(false);
            }

            a.TransitionTo(JobAssignmentStatus.Assigned);
            a.AcceptedAt = acceptedAtUtc;
            a.UpdatedAt = acceptedAtUtc;
            Slots[a.SlotId] = (slot.WorkerId, DispatchSlotStatuses.Booked);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryCloseOfferAsync(long assignmentId, DateTime closedAtUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Assignments.TryGetValue(assignmentId, out var a) || a.AssignmentStatus != JobAssignmentStatus.Offered)
            {
                return Task.FromResult(false);
            }

            a.TransitionTo(JobAssignmentStatus.Cancelled);
            a.UpdatedAt = closedAtUtc;
            if (Slots.TryGetValue(a.SlotId, out var slot) && slot.Status == DispatchSlotStatuses.Held)
            {
                Slots[a.SlotId] = (slot.WorkerId, DispatchSlotStatuses.Available);
            }

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<JobAssignment>> GetOffersCreatedAtOrBeforeAsync(DateTime createdAtOrBeforeUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<JobAssignment>>(Assignments.Values
                .Where(a => a.AssignmentStatus == JobAssignmentStatus.Offered && a.CreatedAt <= createdAtOrBeforeUtc)
                .OrderBy(a => a.CreatedAt)
                .ToList());
        }
    }

    public Task<int?> GetSlotWorkerIdAsync(int slotId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Slots.TryGetValue(slotId, out var slot) ? (int?)slot.WorkerId : null);
        }
    }

    public Task<JobAssignment> CreateAssignedAsync(JobAssignment assignment, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
            assignment.AssignmentId = _nextId++;
            Assignments[assignment.AssignmentId] = assignment;
            if (Slots.TryGetValue(assignment.SlotId, out var slot))
            {
                Slots[assignment.SlotId] = (slot.WorkerId, DispatchSlotStatuses.Booked);
            }

            return Task.FromResult(assignment);
        }
    }
}
