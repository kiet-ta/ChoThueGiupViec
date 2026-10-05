using MediatR;
using System.ComponentModel.DataAnnotations;
using ApplicationValidationException = CommonService.Application.Exceptions.ValidationException;

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
            var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in results)
            {
                var members = result.MemberNames.Any() ? result.MemberNames : new[] { string.Empty };
                var message = result.ErrorMessage ?? "Validation error occurred.";

                foreach (var member in members)
                {
                    if (!errors.TryGetValue(member, out var list))
                    {
                        list = new List<string>();
                        errors[member] = list;
                    }
                    list.Add(message);
                }
            }

            var mappedErrors = errors.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);

            throw new ApplicationValidationException(mappedErrors);
        }

        return await next();
    }
}
