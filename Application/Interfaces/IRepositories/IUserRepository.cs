
using CommonService.Domain.Entities;

namespace CommonService.Application.Interfaces.IRepositories;
public interface IUserRepository
{
    Task AddAsync(User user);
}

