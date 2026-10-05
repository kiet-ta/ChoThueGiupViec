using CommonService.Application.Common.Helpers;
using CommonService.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.Tests.Infrastructure;

public class ProblemDetailsHelperTests
{
    [Fact]
    public void FromException_with_ValidationException_creates_ValidationProblemDetails()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["phoneNumber"] = new[] { "Phone number is required." },
            ["role"] = new[] { "Invalid role." }
        };
        var ex = new ValidationException(errors);

        var problem = ProblemDetailsHelper.FromException(ex, "/api/auth/otp/request");

        var valProblem = Assert.IsType<ValidationProblemDetails>(problem);
        Assert.Equal(400, valProblem.Status);
        Assert.Equal(2, valProblem.Errors.Count);
        Assert.True(valProblem.Errors.ContainsKey("phoneNumber"));
        Assert.Equal("/api/auth/otp/request", valProblem.Instance);
    }

    [Fact]
    public void FromException_with_NotFoundException_maps_to_404()
    {
        var ex = new NotFoundException("Order", 101);
        var problem = ProblemDetailsHelper.FromException(ex, "/api/orders/101");

        Assert.Equal(404, problem.Status);
        Assert.Equal("Not Found", problem.Title);
        Assert.Contains("101", problem.Detail);
    }

    [Fact]
    public void FromException_with_ForbiddenAccessException_maps_to_403()
    {
        var ex = new ForbiddenAccessException("You are not allowed to cancel this booking.");
        var problem = ProblemDetailsHelper.FromException(ex);

        Assert.Equal(403, problem.Status);
        Assert.Equal("Forbidden", problem.Title);
        Assert.Equal("You are not allowed to cancel this booking.", problem.Detail);
    }

    [Fact]
    public void FromException_with_BusinessRuleViolationException_maps_to_409_with_code()
    {
        var ex = new BusinessRuleViolationException("Shift already full", "ERR_SHIFT_FULL");
        var problem = ProblemDetailsHelper.FromException(ex);

        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal("Shift already full", problem.Detail);
        Assert.Equal("ERR_SHIFT_FULL", problem.Extensions["code"]);
    }

    [Fact]
    public void ToApiResponse_converts_ValidationProblemDetails_to_ApiResponse_with_errors()
    {
        var errors = new Dictionary<string, string[]> { ["field"] = new[] { "invalid" } };
        var valProblem = ProblemDetailsHelper.CreateValidationProblemDetails(errors);

        var response = ProblemDetailsHelper.ToApiResponse(valProblem);

        Assert.False(response.Success);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public void ToApiResponse_converts_general_ProblemDetails()
    {
        var problem = ProblemDetailsHelper.CreateProblemDetails(404, "Not Found", "Item 1 not found");
        var response = ProblemDetailsHelper.ToApiResponse(problem);

        Assert.False(response.Success);
        Assert.Equal("Item 1 not found", response.Message);
        Assert.Null(response.Data);
    }
}
