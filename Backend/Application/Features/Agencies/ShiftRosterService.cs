using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Agencies;

/// <summary>One shift of a worker on the agency's roster (a <c>BOOKING_SLOT</c> of the agency).</summary>
public sealed record RosterEntryDto(int SlotId, int WorkerId, string WorkerName, DateOnly SlotDate, string ShiftCode, string StartTime, string EndTime, string SlotStatus);

/// <summary>An entry of <c>PUT /api/agencies/roster</c>.</summary>
public sealed record RosterRequestEntry(int? WorkerId, DateOnly? SlotDate, string? ShiftCode);

/// <summary>The request of <c>PUT /api/agencies/roster</c>.</summary>
public sealed record RosterRequest(IReadOnlyList<RosterRequestEntry>? Entries);

/// <summary>A problem of one entry: its 0-based position in <c>entries</c> and a code.</summary>
public sealed record RosterEntryError(int Index, string Code, string Message);

/// <summary>Result of a roster write: the slots now on the roster, how many were created, and nothing else changed.</summary>
public sealed record RosterResultDto(int Created, int Unchanged, IReadOnlyList<RosterEntryDto> Entries);

public static class RosterErrorCodes
{
    public const string InvalidEntry = "INVALID_ENTRY";
    public const string WorkerNotOfAgency = "WORKER_NOT_OF_AGENCY";
    public const string WorkerLocked = "WORKER_NOT_AVAILABLE";
    public const string DuplicateEntry = "DUPLICATE_ENTRY";
    public const string SlotNotEditable = "SLOT_NOT_EDITABLE";
    public const string PastDate = "PAST_DATE";
    public const string BeyondHorizon = "BEYOND_HORIZON";
    public const string RosterInvalid = "ROSTER_INVALID";
    public const string SlotNotFound = "SLOT_NOT_FOUND";
}

/// <summary>Raised when a roster write is rejected as a whole; carries the per-entry errors (HTTP 422).</summary>
public sealed class RosterRejectedException(IReadOnlyList<RosterEntryError> errors)
    : BusinessRuleViolationException("The roster was not changed: some entries are invalid.", RosterErrorCodes.RosterInvalid)
{
    public IReadOnlyList<RosterEntryError> Errors { get; } = errors;
}

/// <summary>Persistence of the agency roster (BE-M5-05).</summary>
public interface IRosterRepository
{
    /// <summary>The agency's workers among <paramref name="workerIds"/> with their name and work status (workers of other agencies are absent).</summary>
    Task<IReadOnlyDictionary<int, (string FullName, WorkStatus Status)>> GetAgencyWorkersAsync(
        int agencyId, IReadOnlyCollection<int> workerIds, CancellationToken cancellationToken = default);

    /// <summary>Roster slots of the agency in the date range (inclusive), with the worker's name, ordered by date, shift, worker.</summary>
    Task<IReadOnlyList<RosterEntryDto>> ListAsync(int agencyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    /// <summary>Slots that already exist for any of the given (worker, date, shift) keys (the UNIQUE(worker_id, slot_date, shift_code) rows).</summary>
    Task<IReadOnlyList<BookingSlot>> FindSlotsAsync(
        IReadOnlyCollection<(int WorkerId, DateOnly Date, string ShiftCode)> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the new slots and saves them (inside the caller's transaction). False, with nothing added, when a slot of the same worker, date
    /// and shift appeared in the meantime (the unique index): the caller reads again and finds it.
    /// </summary>
    Task<bool> TryAddSlotsAsync(IReadOnlyCollection<BookingSlot> slots, CancellationToken cancellationToken = default);

    /// <summary>The slot, TRACKED, when it belongs to the agency; otherwise null.</summary>
    Task<BookingSlot?> GetOwnedSlotAsync(int agencyId, int slotId, CancellationToken cancellationToken = default);

    void RemoveSlot(BookingSlot slot);
}

/// <summary>
/// Shift Roster (BE-M5-05, contract agencies.md 5, PRD 2.6/4.4): the agency plans which of its workers work which shifts; each entry is a
/// <c>BOOKING_SLOT</c> with <c>agency_id</c> set and <c>slot_source = AGENCY</c>, <c>AVAILABLE</c> until a Premium hold or an assignment takes it.
/// A write is all-or-nothing: any invalid entry (a worker of another agency, a locked worker, a slot already held or booked, a repeated
/// entry, a past date or beyond the booking horizon) rejects the whole request and changes nothing. Re-sending an existing AVAILABLE slot
/// is a no-op (idempotent). Doubling a worker on the same shift is impossible by <c>UNIQUE(worker_id, slot_date, shift_code)</c>.
/// </summary>
public sealed class ShiftRosterService(
    IRosterRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<BusinessRules> rules)
{
    public const string AgencySlotSource = "AGENCY";

    /// <summary>provenance: BOOKING_SLOT.start_time/end_time of each shift as Booking and Workers use them (BookingShifts, ShiftHelper).</summary>
    private static readonly Dictionary<string, (TimeOnly Start, TimeOnly End)> ShiftTimes = new()
    {
        ["SHIFT_MORNING"] = (new TimeOnly(8, 0), new TimeOnly(12, 0)),
        ["SHIFT_AFTERNOON"] = (new TimeOnly(13, 0), new TimeOnly(17, 0)),
        ["SHIFT_EVENING"] = (new TimeOnly(17, 30), new TimeOnly(20, 30)),
    };

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    /// <summary>The roster of the week that contains <paramref name="date"/> (Monday to Sunday), or of today's week when null.</summary>
    public async Task<IReadOnlyList<RosterEntryDto>> GetWeekAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        var day = date ?? clock.LocalToday;
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return await repository.ListAsync(AgencyId, monday, monday.AddDays(6), cancellationToken);
    }

    public async Task<RosterResultDto> PutAsync(RosterRequest request, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        if (request.Entries is not { Count: > 0 })
        {
            throw new ValidationException("entries", "entries must contain at least one entry.");
        }

        var today = clock.LocalToday;
        var last = today.AddDays(rules.Value.Booking.MaxDaysAhead);
        var errors = new List<RosterEntryError>();
        var parsed = new List<(int Index, int WorkerId, DateOnly Date, string Shift)>();
        var seen = new HashSet<(int, DateOnly, string)>();

        for (var i = 0; i < request.Entries.Count; i++)
        {
            var e = request.Entries[i];
            if (e.WorkerId is not > 0 || e.SlotDate is null || e.ShiftCode is null || !ShiftTimes.ContainsKey(e.ShiftCode))
            {
                errors.Add(new RosterEntryError(i, RosterErrorCodes.InvalidEntry, "workerId, slotDate and a known shiftCode are required."));
                continue;
            }

            if (e.SlotDate.Value < today)
            {
                errors.Add(new RosterEntryError(i, RosterErrorCodes.PastDate, "The date is in the past."));
                continue;
            }

            if (e.SlotDate.Value > last)
            {
                errors.Add(new RosterEntryError(i, RosterErrorCodes.BeyondHorizon, $"The date is more than {rules.Value.Booking.MaxDaysAhead} days ahead."));
                continue;
            }

            if (!seen.Add((e.WorkerId.Value, e.SlotDate.Value, e.ShiftCode)))
            {
                errors.Add(new RosterEntryError(i, RosterErrorCodes.DuplicateEntry, "This worker, date and shift appear twice in the request."));
                continue;
            }

            parsed.Add((i, e.WorkerId.Value, e.SlotDate.Value, e.ShiftCode));
        }

        var workers = await repository.GetAgencyWorkersAsync(agencyId, parsed.Select(p => p.WorkerId).Distinct().ToList(), cancellationToken);
        var existing = parsed.Count == 0
            ? []
            : await repository.FindSlotsAsync(parsed.Select(p => (p.WorkerId, p.Date, p.Shift)).ToList(), cancellationToken);
        var existingByKey = existing.ToDictionary(s => (s.WorkerId, s.SlotDate, s.ShiftCode));

        var toCreate = new List<BookingSlot>();
        var unchanged = 0;
        var now = clock.UtcNow;
        foreach (var (index, workerId, date, shift) in parsed)
        {
            if (!workers.TryGetValue(workerId, out var worker))
            {
                // A worker of another agency looks the same as an unknown one (never reveal who works where).
                errors.Add(new RosterEntryError(index, RosterErrorCodes.WorkerNotOfAgency, "The worker is not a worker of this agency."));
                continue;
            }

            if (worker.Status is WorkStatus.Locked or WorkStatus.Pending or WorkStatus.Retiring or WorkStatus.Retired)
            {
                errors.Add(new RosterEntryError(index, RosterErrorCodes.WorkerLocked, "A locked, retiring or retired worker cannot be rostered."));
                continue;
            }

            if (existingByKey.TryGetValue((workerId, date, shift), out var slot))
            {
                if (slot.AgencyId == agencyId && slot.SlotStatus == DispatchSlotStatuses.Available)
                {
                    unchanged++;
                    continue;
                }

                errors.Add(new RosterEntryError(index, RosterErrorCodes.SlotNotEditable, "The slot already exists and is held, booked or blocked."));
                continue;
            }

            var (start, end) = ShiftTimes[shift];
            toCreate.Add(new BookingSlot
            {
                WorkerId = workerId,
                AgencyId = agencyId,
                SlotDate = date,
                ShiftCode = shift,
                StartTime = start,
                EndTime = end,
                SlotSource = AgencySlotSource,
                SlotStatus = DispatchSlotStatuses.Available,
                UpdatedAt = now,
            });
        }

        if (errors.Count > 0)
        {
            throw new RosterRejectedException(errors.OrderBy(e => e.Index).ToList());
        }

        if (toCreate.Count > 0)
        {
            var added = await unitOfWork.ExecuteInTransactionAsync(
                () => repository.TryAddSlotsAsync(toCreate, cancellationToken), cancellationToken);
            if (!added)
            {
                // A concurrent request added one of these slots between the read and the write (UNIQUE worker, date, shift).
                throw new BusinessRuleViolationException(
                    "A slot of this request was created at the same time; read the roster again and retry.", RosterErrorCodes.SlotNotEditable);
            }
        }

        var dates = parsed.Select(p => p.Date).ToList();
        var week = await repository.ListAsync(agencyId, dates.Min(), dates.Max(), cancellationToken);
        return new RosterResultDto(toCreate.Count, unchanged, week);
    }

    /// <summary>Removes one roster slot; only an <c>AVAILABLE</c> slot of the agency can go (a held or booked one carries an order).</summary>
    public async Task RemoveAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var slot = await repository.GetOwnedSlotAsync(agencyId, slotId, cancellationToken)
                ?? throw new NotFoundException("Roster slot", slotId);
            if (slot.SlotStatus != DispatchSlotStatuses.Available)
            {
                throw new BusinessRuleViolationException(
                    "A held, booked or blocked slot cannot be removed from the roster.", RosterErrorCodes.SlotNotEditable);
            }

            repository.RemoveSlot(slot);
            return true;
        }, cancellationToken);
    }
}
