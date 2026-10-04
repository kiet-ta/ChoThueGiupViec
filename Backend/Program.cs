using CommonService.Application;
using CommonService.Infrastructure;
using CommonService.Middleware;

namespace CommonService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllers();

            // Register Application Layer (MediatR, Behaviors, etc.)
            builder.Services.AddApplication();

            // Register Infrastructure Layer (Repositories, Cache, etc.)
            builder.Services.AddInfrastructure(builder.Configuration);

            //BindingAddress Swagger/OpenAPI
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            //Register Middleware
            app.UseMiddleware<ErrorHandlingMiddleware>();
            app.UseMiddleware<RequestResponseLoggingMiddleware>();
            app.UseMiddleware<ResponseWrapperMiddleware>();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
