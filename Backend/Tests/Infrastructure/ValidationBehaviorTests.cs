using System.ComponentModel.DataAnnotations;
using CommonService.Application.Behaviors;
using MediatR;
using ApplicationValidationException = CommonService.Application.Exceptions.ValidationException;

namespace CommonService.Tests.Infrastructure;

public class ValidationBehaviorTests
{
    private class SampleRequest
    {
        [Required(ErrorMessage = "Name is required")]
        public string? Name { get; set; }

        [Range(1, 100, ErrorMessage = "Age must be between 1 and 100")]
        public int Age { get; set; }
    }

    [Fact]
    public async Task ValidationBehavior_passes_when_request_is_valid()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>();
        var request = new SampleRequest { Name = "Valid Name", Age = 25 };

        var result = await behavior.Handle(request, _ => Task.FromResult("SUCCESS"), CancellationToken.None);

        Assert.Equal("SUCCESS", result);
    }

    [Fact]
    public async Task ValidationBehavior_throws_ValidationException_when_request_is_invalid()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>();
        var request = new SampleRequest { Name = null, Age = 150 };

        var ex = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            behavior.Handle(request, _ => Task.FromResult("SUCCESS"), CancellationToken.None));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("Name"));
        Assert.True(ex.Errors.ContainsKey("Age"));
        Assert.Contains("Name is required", ex.Errors["Name"]);
        Assert.Contains("Age must be between 1 and 100", ex.Errors["Age"]);
    }
}
