using System.Text.Json;
using System.Text.Json.Nodes;
using CommonService.Application;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Swagger;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace CommonService.Tests.Contracts;

public class OpenApiSnapshotTests
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HARNESS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root containing HARNESS.md not found.");
    }

    private static (OpenApiDocument Document, string FormattedJson) GenerateLiveDocument()
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

        // Format JSON with 2-space indentation
        var rawJson = doc.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);
        var jsonNode = JsonNode.Parse(rawJson);
        var formattedJson = jsonNode?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

        return (doc, formattedJson);
    }

    [Fact]
    public void Swagger_document_contains_bearer_security_scheme()
    {
        var (doc, _) = GenerateLiveDocument();

        Assert.NotNull(doc.Components);
        Assert.True(doc.Components.SecuritySchemes.ContainsKey("Bearer"), "Missing Bearer security scheme.");

        var bearer = doc.Components.SecuritySchemes["Bearer"];
        Assert.Equal(SecuritySchemeType.Http, bearer.Type);
        Assert.Equal("bearer", bearer.Scheme);
        Assert.Equal("JWT", bearer.BearerFormat);
    }

    [Fact]
    public void Swagger_document_operations_have_consistent_operationIds()
    {
        var (doc, _) = GenerateLiveDocument();

        Assert.NotEmpty(doc.Paths);
        foreach (var (path, pathItem) in doc.Paths)
        {
            foreach (var (method, operation) in pathItem.Operations)
            {
                Assert.False(string.IsNullOrWhiteSpace(operation.OperationId),
                    $"Operation for {method} {path} has no operationId.");
                Assert.Contains("_", operation.OperationId);
            }
        }
    }

    [Fact]
    public void Snapshot_matches_live_openapi_document()
    {
        var (_, liveJson) = GenerateLiveDocument();
        var snapshotPath = Path.Combine(FindRepoRoot(), ".spec", "contracts", "openapi.json");

        Assert.True(File.Exists(snapshotPath), $"Snapshot file does not exist at {snapshotPath}");

        var snapshotJson = File.ReadAllText(snapshotPath).Replace("\r\n", "\n").Trim();
        var normalizedLiveJson = liveJson.Replace("\r\n", "\n").Trim();

        Assert.Equal(snapshotJson, normalizedLiveJson);
    }
}
