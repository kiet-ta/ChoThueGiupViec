using System.Text.Json;
using System.Text.Json.Nodes;
using CommonService.Application;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Swagger;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace CommonService.Tests.Identity;

public class OpenApiUpdateHelper
{
    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "CommonService";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void Update_openapi_snapshot()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var env = new TestHostEnvironment();
        services.AddSingleton<IWebHostEnvironment>(env);
        services.AddSingleton<IHostEnvironment>(env);

        var config = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddControllers();
        services.AddApplication();
        services.AddInfrastructure(config);
        services.AddConfiguredSwagger();

        using var provider = services.BuildServiceProvider();
        var swaggerProvider = provider.GetRequiredService<ISwaggerProvider>();
        var doc = swaggerProvider.GetSwagger("v1");

        var rawJson = doc.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);
        var jsonNode = JsonNode.Parse(rawJson);
        var formattedJson = jsonNode?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HARNESS.md")))
        {
            dir = dir.Parent;
        }

        var repoRoot = dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
        var snapshotPath = Path.Combine(repoRoot, ".spec", "contracts", "openapi.json");

        File.WriteAllText(snapshotPath, formattedJson.Replace("\r\n", "\n"));
    }
}
