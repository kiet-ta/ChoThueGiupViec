using MediatR;
using System.ComponentModel.DataAnnotations;
namespace CommonService.Application.Features.Users.Commands;

public class CreateUserCommand : IRequest<Guid>
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Email format is invalid")]
    public required string Email { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [MaxLength(50, ErrorMessage = "Name must not exceed 50 characters")]
    public required String Username { get; set; }

    [Required(ErrorMessage = "Password is required")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    public required String Password { get; set; }


}

