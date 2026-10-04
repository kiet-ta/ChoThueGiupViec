using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Infrastructure.Persistance
{
    public class UserRepository : IUserRepository
    {
        // In a real implementation, this would interact with a database


        //demo database
        private readonly List<User> _users = new();
        public Task AddAsync(User user)
        {
            _users.Add(user);
            Console.WriteLine($"[DB] User {user.Username} saved");
            return Task.CompletedTask;
        }
    }
}
