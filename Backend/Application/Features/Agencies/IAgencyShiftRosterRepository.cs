using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Repository contract for AgencyShiftRoster aggregate.
/// Lives inside Application/Features/Agencies per module encapsulation convention.
/// </summary>
public interface IAgencyShiftRosterRepository : IRepository<AgencyShiftRoster, long>
{
    Task<AgencyShiftRoster?> GetByAgencyAndWorkerAndSlotAsync(
        int agencyId,
        int workerId,
        DateOnly slotDate,
        string shiftCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgencyShiftRoster>> GetByAgencyAndSlotDateAsync(
        int agencyId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgencyShiftRoster>> GetByAgencyAndWorkerAsync(
        int agencyId,
        int workerId,
        CancellationToken cancellationToken = default);

    Task<bool> IsSlotAvailableForWorkerAsync(
        int workerId,
        DateOnly slotDate,
        string shiftCode,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
