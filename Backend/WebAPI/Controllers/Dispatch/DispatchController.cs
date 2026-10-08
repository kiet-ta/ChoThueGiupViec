using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Features.Dispatch.Dtos;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Application.Features.Customers;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CommonService.WebAPI.Controllers.Dispatch;

/// <summary>
/// Dispatch management endpoints (contract dispatch.md).
/// </summary>
[ApiController]
[Route("api/dispatch")]
public class DispatchController(
    FieldCheckInService fieldCheckInService,
    IFieldCheckInRepository fieldCheckInRepository,
    IJobOrderRepository jobOrderRepository,
    ICustomerAddressRepository customerAddressRepository,
    ICurrentUser currentUser,
    IOptions<BusinessRules> rulesOptions) : ControllerBase
{
    private readonly BusinessRules _rules = rulesOptions.Value;

    /// <summary>
    /// Check-in GPS tại hiện trường (BR-04). Lệch <= 100m tính là hợp lệ (`isGpsVerified: true`).
    /// </ /// <param name="assignmentId">The job assignment ID</param>
    /// <param name="request">GPS coordinates from worker</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Check-in result</returns>
    [HttpPost("assignments/{assignmentId:long}/check-in")]
    [Authorize(Policy = "WorkerOnly")]
    [ProducesResponseType(typeof(ApiResponse<CheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(
        long assignmentId,
        [FromBody] CheckInRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        // Validate GPS coordinates
        if (!IsValidGpsCoordinates(request.Latitude, request.Longitude))
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid GPS coordinates.", null));
        }

        // Get current worker ID
        if (currentUser.UserId is not int workerId)
        {
            return Unauthorized(ApiResponse<object>.Fail("Authenticated worker user ID is required.", null));
        }

        // Get assignment to validate and get OrderId
        var assignment = await fieldCheckInRepository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return NotFound(ApiResponse<object>.Fail("Job assignment not found.", null));
        }

        // Validate worker owns the assignment
        if (assignment.WorkerId != workerId && assignment.AgencyId == null)
        {
            return Forbid(ApiResponse<object>.Fail("Worker is not assigned to this job assignment.", null));
        }

        // Validate assignment is in ASSIGNED state (eligible for check-in)
        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            return StatusCode(409, ApiResponse<object>.Fail($"Assignment status ({assignment.AssignmentStatus}) is not eligible for check-in. Must be ASSIGNED.", null));
        }

        // Get JobOrder to get AddressId and CustomerId
        var jobOrder = await jobOrderRepository.GetByIdAsync(assignment.OrderId, cancellationToken);
        if (jobOrder == null)
        {
            return NotFound(ApiResponse<object>.Fail("Job order not found.", null));
        }

        // Get CustomerAddress to get latitude and longitude
        var customerAddress = await customerAddressRepository.GetOwnedAsync(jobOrder.CustomerId, jobOrder.AddressId, cancellationToken);
        if (customerAddress == null)
        {
            return NotFound(ApiResponse<object>.Fail("Customer address not found.", null));
        }

        // Create GeoPoint objects for distance calculation
        var deviceLocation = new GeoPoint(request.Latitude, request.Longitude);
        var targetOrderLocation = new GeoPoint((double)customerAddress.Latitude, (double)customerAddress.Longitude);

        // Perform GPS check-in using the service
        var result = await fieldCheckInService.CheckInAsync(
            assignmentId,
            workerId,
            deviceLocation,
            targetOrderLocation,
            cancellationToken);

        // Map result to response DTO
        if (result.Success)
        {
            return Ok(ApiResponse<CheckInResultDto>.Ok(new CheckInResultDto
            {
                CheckInId = result.CheckinId,
                AssignmentId = result.AssignmentId,
                CheckedInAt = result.CheckedInAt ?? DateTime.UtcNow,
                DistanceMeters = (double)result.DistanceMeters,
                IsGpsVerified = result.IsGpsVerified,
                RequiresAlternativeVerification = result.RequiresFallback,
                VerificationMethod = result.IsGpsVerified ? "GPS" : (result.RequiresFallback ? "FALLBACK_REQUIRED" : "NONE"),
                PlatePhotoUrl = result.FallbackMethod == "PLATE_PHOTO" ? result.FallbackPhotoUrl : null,
                IsCustomerConfirmed = result.IsCustomerConfirmed
            }, "Check-in processed successfully."));
        }
        else
        {
            // Return error with appropriate status code based on failure reason
            if (result.ErrorMessage?.Contains("Không tìm thấy ca làm việc") == true)
            {
                return NotFound(ApiResponse<object>.Fail(result.ErrorMessage, null));
            }
            else if (result.ErrorMessage?.Contains("Không thuộc về thợ này") == true ||
                     result.ErrorMessage?.Contains("Worker is not assigned") == true)
            {
                return Forbid(ApiResponse<object>.Fail(result.ErrorMessage, null));
            }
            else if (result.ErrorMessage?.Contains("trạng thái ca làm") == true ||
                     result.ErrorMessage?.Contains("Assignment status") == true ||
                     result.ErrorMessage?.Contains("not eligible for check-in") == true)
            {
                return StatusCode(409, ApiResponse<object>.Fail(result.ErrorMessage, null));
            }
            else
            {
                // Default to bad request for other validation errors
                return BadRequest(ApiResponse<object>.Fail(result.ErrorMessage ?? "Check-in failed due to validation error.", null));
            }
        }
    }

    private bool IsValidGpsCoordinates(double latitude, double longitude)
    {
        return latitude >= -90 && latitude <= 90 &&
               longitude >= -180 && longitude <= 180;
    }
}