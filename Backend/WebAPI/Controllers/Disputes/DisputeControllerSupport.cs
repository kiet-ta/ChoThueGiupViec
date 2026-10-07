using CommonService.Application.Common.Models;
using CommonService.Application.Features.Disputes.Services;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Disputes;

/// <summary>Maps a <see cref="DisputeResult{T}"/> to the ApiResponse envelope and the status codes of contract disputes.md.</summary>
internal static class DisputeControllerSupport
{
    public static IActionResult ToActionResult<T>(this ControllerBase controller, DisputeResult<T> result, string successMessage)
    {
        if (result.Success && result.Data is not null)
        {
            return controller.StatusCode(result.StatusCode, ApiResponse<T>.Ok(result.Data, successMessage));
        }

        return controller.StatusCode(result.StatusCode, ApiResponse<object>.Fail(
            result.ErrorMessage ?? "Request failed.",
            result.ValidationErrors is not null ? new { errors = result.ValidationErrors } : null));
    }
}
