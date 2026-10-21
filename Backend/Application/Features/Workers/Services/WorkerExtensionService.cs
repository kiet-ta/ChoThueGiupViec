using System.Globalization;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;

namespace CommonService.Application.Features.Workers.Services;

public sealed class WorkerExtensionService(
    IWorkerExtensionRepository extensions,
    IPublisher publisher,
    ICurrentUser currentUser,
    IClock clock) : IWorkerExtensionService
{
    private const string NotYourExtension = "Extension was not found for this worker.";
    private const string DeclineReason = "The worker declined the extension.";

    public async Task<ApiResponse<ExtensionResponse>> RespondAsync(long assignmentId, bool accept, CancellationToken ct = default)
    {
        // Fails closed before anything is read: only an authenticated worker with a real id.
        if (!currentUser.IsAuthenticated || currentUser.Role != UserRole.Worker || currentUser.UserId is not int workerId || workerId <= 0)
        {
            throw new ForbiddenAccessException(NotYourExtension);
        }

        var extension = await extensions.FindForWorkerAsync(assignmentId, workerId, ct)
            ?? throw new NotFoundException(NotYourExtension);

        if (extension.ExtStatus != ExtensionStatuses.Paid)
        {
            throw new BusinessRuleViolationException($"Cannot respond to an extension in status {extension.ExtStatus}.");
        }

        var now = clock.UtcNow;
        string status;
        if (accept)
        {
            if (!await extensions.TryAcceptAsync(extension.ExtensionId, now, ct))
            {
                throw new BusinessRuleViolationException("The extension was already answered.");
            }

            status = ExtensionStatuses.Accepted;
        }
        else
        {
            // Payments marks it DECLINED and refunds: writing DECLINED here first would make its idempotent handler skip the refund.
            await publisher.Publish(
                new ExtensionDeclined(extension.ExtensionId, extension.OrderId, workerId, DeclineReason, now), ct);
            status = ExtensionStatuses.Declined;
        }

        var response = new ExtensionResponse(
            assignmentId,
            extension.ExtensionId,
            extension.ExtraHours,
            extension.ExtraAmount,
            status,
            NewEndTime(extension, accept));
        return ApiResponse<ExtensionResponse>.Ok(response, accept ? "Extension accepted." : "Extension declined.");
    }

    /// <summary>The local end of the shift, longer by the extra hours only when the extension was accepted; empty for an unknown shift code.</summary>
    private static string NewEndTime(ExtensionForWorker extension, bool accepted)
    {
        if (BookingShifts.GetLocalTimes(extension.ShiftCode) is not { } times)
        {
            return string.Empty;
        }

        var end = accepted ? times.End.AddHours((double)extension.ExtraHours) : times.End;
        return end.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
