using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using MediatR;

namespace CommonService.Application.Features.Workers.Queries;

public sealed record GetWorkerSlotsQuery(DateOnly? StartDate = null, DateOnly? EndDate = null) : IRequest<ApiResponse<IReadOnlyList<BookingSlotResponse>>>;

public sealed class GetWorkerSlotsQueryHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<GetWorkerSlotsQuery, ApiResponse<IReadOnlyList<BookingSlotResponse>>>
{
    public async Task<ApiResponse<IReadOnlyList<BookingSlotResponse>>> Handle(GetWorkerSlotsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var start = query.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var end = query.EndDate ?? start.AddDays(7);

        if (end < start)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "endDate", ["endDate must be greater than or equal to startDate."] }
            });
        }

        var slots = await workerRepository.GetWorkerSlotsAsync(workerId, start, end, cancellationToken);

        var responses = slots.Select(s => new BookingSlotResponse(
            s.SlotId,
            s.WorkerId,
            s.SlotDate,
            s.ShiftCode,
            s.StartTime.ToString("HH:mm"),
            s.EndTime.ToString("HH:mm"),
            s.SlotStatus == "AVAILABLE"
        )).ToList();

        return ApiResponse<IReadOnlyList<BookingSlotResponse>>.Ok(responses, "Retrieved booking slots successfully.");
    }
}
