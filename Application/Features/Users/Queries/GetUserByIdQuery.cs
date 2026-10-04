//using CommonService.Application.Interfaces.IRepositories;
//using CommonService.Application.Interfaces.IServices;
//using MediatR;

//namespace CommonService.Application.Features.Users.Queries;
//public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto>
//{
//    private readonly ICacheService _cache;
//    private readonly IUserRepository _repo;

//    public GetUserByIdQueryHandler(ICacheService cache, IUserRepository repo)
//    {
//        _cache = cache;
//        _repo = repo;
//    }

//    public async Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
//    {
//        string cacheKey = $"user:{request.Id}";
//        var cached = await _cache.GetAsync<UserDto>(cacheKey);
//        if (cached != null) return cached;

//        var user = await _repo.GetByIdAsync(request.Id);
//        if (user == null) return null;

//        var dto = new UserDto { Id = user.Id, Name = user.Name };
//        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5));

//        return dto;
//    }
//}
