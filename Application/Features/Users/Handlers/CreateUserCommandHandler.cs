using CommonService.Application.Features.Users.Commands;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;
using MediatR;

namespace CommonService.Application.Features.Users.Handlers
{
    public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
    {
        private readonly IUserRepository _userRepository;
        private readonly ILogger<CreateUserCommandHandler> _logger;

        public CreateUserCommandHandler(IUserRepository userRepository, ILogger<CreateUserCommandHandler> logger)
        {
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Creating a new user with username: {Username}", request.Username);

            var user = new User
            {
                Username = request.Username,
                Password = request.Password // In a real application, ensure to hash the password
            };
            await _userRepository.AddAsync(user);
            _logger.LogInformation("User {username} created successfully with Id: {id}", request.Username, user.Id);
            return user.Id;
        }
    }
}
