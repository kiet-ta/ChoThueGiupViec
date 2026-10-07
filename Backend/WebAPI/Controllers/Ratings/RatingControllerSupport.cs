using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ratings.Services;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Ratings;

/// <summary>Maps a <see cref="RatingResult{T}"/> to the ApiResponse envelope and the status codes of contract ratings.md.</summary>
internal static class RatingControllerSupport
{
    public static IActionResult ToActionResult<T>(this ControllerBase controller, RatingResult<T> result, string successMessage)
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
