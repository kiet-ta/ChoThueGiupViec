using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>The worker answers a paid extension of the customer ("Làm lần 2", BR-08, contract workers.md 2.6.1). The caller is read from ICurrentUser.</summary>
public interface IWorkerExtensionService
{
    /// <summary>
    /// Accept: the extension becomes ACCEPTED. Decline: <c>ExtensionDeclined</c> is published and Payments marks it DECLINED and refunds 100 %.
    /// 404 when the assignment is not the caller's or has no extension of this worker; 409 unless the extension is PAID.
    /// </summary>
    Task<ApiResponse<ExtensionResponse>> RespondAsync(long assignmentId, bool accept, CancellationToken ct = default);
}
