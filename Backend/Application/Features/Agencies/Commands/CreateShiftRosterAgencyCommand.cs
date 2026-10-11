using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies.Dtos;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

public sealed record CreateShiftRosterAgencyCommand(CreateShiftRosterAgencyRequest Request) : IRequest<ApiResponse<ShiftRosterAgencyResponse>>;

public sealed class CreateShiftRosterAgencyCommandHandler(
    IShiftRosterAgencyRepository agencyShiftRosterRepository,
    IAgencyRepository agencyRepository)
    : IRequestHandler<CreateShiftRosterAgencyCommand, ApiResponse<ShiftRosterAgencyResponse>>
{
    public async Task<ApiResponse<ShiftRosterAgencyResponse>> Handle(CreateShiftRosterAgencyCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        // 1. Validate inputs
        if (req.WorkerId <= 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "workerId", ["Worker ID must be a positive integer."] }
            });
        }

        if (string.IsNullOrWhiteSpace(req.ShiftCode))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "shiftCode", ["Shift code is required."] }
            });
        }

        if (req.StartTime >= req.EndTime)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "timing", ["Start time must be before end time."] }
            });
        }

        // 2. Verify the worker exists and belongs to the agency
        // Note: In a real implementation, we would get the agency ID from the authenticated user's context
        // For now, we'll need to determine how to get the current agency ID
        // This is a simplification - in reality, this would come from auth context
        int agencyId = 1; // TODO: Replace with actual agency ID from auth context

        var workerExists = await agencyRepository.GetByIdAsync(req.WorkerId, cancellationToken) != null;
        if (!workerExists)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "workerId", ["Worker does not exist."] }
            });
        }

        // 3. Check if the slot is available for this worker (prevent double-booking)
        var isSlotAvailable = await agencyShiftRosterRepository.IsSlotAvailableForWorkerAsync(
            req.WorkerId,
            req.SlotDate,
            req.ShiftCode,
            cancellationToken);

        if (!isSlotAvailable)
        {
            throw new BusinessRuleViolationException("Worker is already assigned to this shift slot.");
        }

        // 4. Create the agency shift roster entity
        var roster = new ShiftRosterAgency
        {
            AgencyId = agencyId,
            WorkerId = req.WorkerId,
            SlotDate = req.SlotDate,
            ShiftCode = req.ShiftCode,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Note = req.Note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // 5. Save to database
        await agencyShiftRosterRepository.AddAsync(roster, cancellationToken);
        await agencyShiftRosterRepository.SaveChangesAsync(cancellationToken);

        // 6. Get worker details for response (in a real app, we'd join or fetch separately)
        var worker = await agencyRepository.GetByIdAsync(req.WorkerId, cancellationToken);
        var workerFullName = worker?.FullName ?? "Unknown Worker";
        var workerPhoneNumber = worker?.PhoneNumber ?? string.Empty;

        // 7. Create response
        var response = new ShiftRosterAgencyResponse(
            roster.RosterId,
            roster.AgencyId,
            roster.WorkerId,
            workerFullName,
            workerPhoneNumber,
            roster.SlotDate,
            roster.ShiftCode,
            roster.StartTime,
            roster.EndTime,
            roster.Note,
            roster.CreatedAt,
            roster.UpdatedAt
        );

        return ApiResponse<ShiftRosterAgencyResponse>.Ok(response, "Shift roster entry created successfully.");
    }
}
