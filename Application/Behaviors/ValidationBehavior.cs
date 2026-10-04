using MediatR;
using System.ComponentModel.DataAnnotations;

namespace CommonService.Application.Behaviors;
public class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(request, null, null);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(request, context, results, true))
        {
            var errors = results.Select(r => r.ErrorMessage).ToList();
            throw new ValidationException(
                $"Validation failed: {string.Join(", ", errors)}"
            );
        }

        return await next();
    }
}


