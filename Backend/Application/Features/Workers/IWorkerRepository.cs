using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Workers;

/// <summary>
/// Repository contract for Worker aggregate root.
/// Lives inside Application/Features/Workers per module encapsulation convention.
/// </summary>
public interface IWorkerRepository : IRepository<Worker, int>
{
    Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BookingSlot>> GetWorkerSlotsAsync(int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<BookingSlot?> GetWorkerSlotByShiftAsync(int workerId, DateOnly slotDate, string shiftCode, CancellationToken cancellationToken = default);
    Task AddSlotAsync(BookingSlot slot, CancellationToken cancellationToken = default);
    void UpdateSlot(BookingSlot slot);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}


