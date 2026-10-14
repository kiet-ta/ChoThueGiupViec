using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// In-memory <see cref="IDispatchRepository"/> with the same conditional semantics as <c>EfDispatchRepository</c>: an offer holds its slot
/// (<c>AVAILABLE -&gt; HELD</c>), accept succeeds only once (<c>OFFERED -&gt; ASSIGNED</c>, slot <c>HELD -&gt; BOOKED</c>), close frees the slot.
/// Like the EF update, an offer on a slot that was never registered with <see cref="AddSlot"/> fails (returns null).
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

    /// <summary>Date and shift of the slots registered with <see cref="AddSlotOnShift"/> (the others have none).</summary>
    public Dictionary<int, (DateOnly Date, string ShiftCode)> SlotShifts { get; } = new();

    public void AddSlotOnShift(int slotId, int workerId, DateOnly date, string shiftCode, string status = DispatchSlotStatuses.Available)
    {
        lock (_gate)
        {
            Slots[slotId] = (workerId, status);
            SlotShifts[slotId] = (date, shiftCode);
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
            if (!Slots.TryGetValue(offer.SlotId, out var slot) || slot.Status != DispatchSlotStatuses.Available)
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

    public Task<IReadOnlyCollection<byte>> GetTakenSeatsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<byte>>(Assignments.Values
                .Where(a => a.OrderId == orderId && IsLive(a.AssignmentStatus))
                .Select(a => a.AssignmentSeq)
                .Distinct()
                .ToList());
        }
    }

    /// <summary><c>JOB_ORDER.updated_at</c> per order (default: long ago), for the sweep's quiet window.</summary>
    public Dictionary<long, DateTime> OrderUpdatedAt { get; } = new();

    public Task<IReadOnlyList<long>> GetOrdersMissingSeatsAsync(DateTime quietSinceUtc, DateOnly scheduledFrom, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<long>>(Orders.Values
                .Where(o => o.OrderStatus == JobOrderStatus.Dispatching && o.ScheduledDate >= scheduledFrom)
                .Where(o => (OrderUpdatedAt.TryGetValue(o.OrderId, out var updatedAt) ? updatedAt : DateTime.MinValue) <= quietSinceUtc)
                .Where(o => Assignments.Values.Count(a => a.OrderId == o.OrderId && IsLive(a.AssignmentStatus)) < o.RequiredWorkers)
                .Where(o => !Assignments.Values.Any(a => a.OrderId == o.OrderId && a.UpdatedAt > quietSinceUtc))
                .Select(o => o.OrderId)
                .ToList());
        }
    }

    public Task<int> CancelOpenAssignmentsOfOrderAsync(long orderId, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var open = Assignments.Values
                .Where(a => a.OrderId == orderId && a.AssignmentStatus is JobAssignmentStatus.Offered or JobAssignmentStatus.Assigned)
                .ToList();
            foreach (var a in open)
            {
                a.TransitionTo(JobAssignmentStatus.Cancelled);
                a.UpdatedAt = cancelledAtUtc;
                if (Slots.TryGetValue(a.SlotId, out var slot) && slot.Status is DispatchSlotStatuses.Held or DispatchSlotStatuses.Booked)
                {
                    Slots[a.SlotId] = (slot.WorkerId, DispatchSlotStatuses.Available);
                }
            }

            return Task.FromResult(open.Count);
        }
    }

    public Task<IReadOnlyList<JobAssignment>> GetLiveAssignmentsOfOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<JobAssignment>>(Assignments.Values
                .Where(a => a.OrderId == orderId && IsLive(a.AssignmentStatus))
                .OrderBy(a => a.AssignmentSeq)
                .ToList());
        }
    }

    public Task<bool> TryMoveOrderStatusAsync(long orderId, JobOrderStatus from, JobOrderStatus to, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Orders.TryGetValue(orderId, out var order) || order.OrderStatus != from)
            {
                return Task.FromResult(false);
            }

            Orders[orderId] = order with { OrderStatus = to };
            OrderUpdatedAt[orderId] = atUtc;
            return Task.FromResult(true);
        }
    }

    public Task<BookingSlot?> GetAvailableSlotAsync(int workerId, DateOnly date, string shiftCode, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var slotId = SlotShifts
                .Where(s => s.Value.Date == date && s.Value.ShiftCode == shiftCode
                    && Slots.TryGetValue(s.Key, out var slot) && slot.WorkerId == workerId && slot.Status == DispatchSlotStatuses.Available)
                .Select(s => (int?)s.Key)
                .FirstOrDefault();
            return Task.FromResult<BookingSlot?>(slotId is null
                ? null
                : new BookingSlot { SlotId = slotId.Value, WorkerId = workerId, SlotDate = date, ShiftCode = shiftCode, SlotStatus = DispatchSlotStatuses.Available });
        }
    }

    public Task<IReadOnlyList<DispatchOrderContext>> GetAwaitingFallbackOrdersAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DispatchOrderContext>>(
                Orders.Values.Where(o => o.OrderStatus == JobOrderStatus.AwaitingFallback).OrderBy(o => o.OrderId).ToList());
        }
    }

    /// <summary>Set the order's status, as Booking would (e.g. CANCELLED).</summary>
    public void SetOrderStatus(long orderId, JobOrderStatus status)
    {
        lock (_gate)
        {
            Orders[orderId] = Orders[orderId] with { OrderStatus = status };
        }
    }

    private static bool IsLive(JobAssignmentStatus status) =>
        status is not (JobAssignmentStatus.Cancelled or JobAssignmentStatus.CancelledByWorker or JobAssignmentStatus.Reassigned);
}
