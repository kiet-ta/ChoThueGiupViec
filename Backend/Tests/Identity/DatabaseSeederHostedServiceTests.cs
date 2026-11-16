using CommonService.Infrastructure.Modules.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommonService.Tests.Identity;

/// <summary>BE-DEV-MIGRATE-01: only the Development environment migrates (and so creates) the database on start.</summary>
public class DatabaseSeederHostedServiceTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "CommonService";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    [InlineData("Test", false)]
    public void Only_Development_migrates_on_start(string environment, bool expected)
    {
        Assert.Equal(expected, DatabaseSeederHostedService.ShouldMigrateOnStart(new FakeEnvironment(environment)));
    }
}
