using CommonService.Application.Features.Customers.Dtos;

namespace CommonService.Application.Features.Customers.Services;

public sealed class CustomerProfileResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public CustomerProfileDto? Data { get; init; }

    public static CustomerProfileResult Ok(CustomerProfileDto data) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = data
    };

    public static CustomerProfileResult ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors
    };

    public static CustomerProfileResult NotFound(string message = "Customer not found.") => new()
    {
        Success = false,
        StatusCode = 404,
        ErrorMessage = message
    };
}
