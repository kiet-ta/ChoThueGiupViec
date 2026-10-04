using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.IServices;
using CommonService.Infrastructure.Persistance;
using CommonService.Application.Services;
using CommonService.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register CommonService dependencies
builder.Services.AddScoped<IUserRepository, UserRepository>();
//builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Add MediatR for CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Add caching
builder.Services.AddMemoryCache();
// Or Redis: builder.Services.AddStackExchangeRedisCache(options => { ... });

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CommonService Middleware Pipeline
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseMiddleware<ResponseWrapperMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();