using CommonService.Infrastructure.Modules.Payments.SePay;
using Microsoft.Extensions.Configuration;

namespace CommonService.Tests.Payments;

// Reads the COMMITTED Backend/appsettings.json (not a hand-built dictionary): the file is the single place
// where the whole SePay configuration surface is declared; real values come from SePay__* environment variables.
public class SePayAppSettingsTests
{
    private static IConfigurationSection CommittedSection()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HARNESS.md")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir.FullName, "Backend", "appsettings.json"), optional: false)
            .Build()
            .GetSection(SePayOptions.SectionName);
    }

    [Fact]
    public void CommittedAppSettings_DeclareTheSePaySection_WithEveryKeyOfSePayOptions()
    {
        var section = CommittedSection();

        Assert.True(section.Exists());
        foreach (var key in new[] { "AccountNumber", "BankName", "WebhookSecret", "ApiToken", "ApiBaseUrl", "QrBaseUrl", "CodePrefix" })
        {
            Assert.False(string.IsNullOrWhiteSpace(section[key]), $"SePay:{key} is not declared in Backend/appsettings.json");
        }
    }

    [Fact]
    public void CommittedAppSettings_HoldOnlyPlaceholderSecrets_SoNoRealKeyCanBeCommittedUnnoticed()
    {
        var section = CommittedSection();

        foreach (var key in new[] { "AccountNumber", "BankName", "WebhookSecret", "ApiToken" })
        {
            Assert.Contains("PLACEHOLDER", section[key], StringComparison.OrdinalIgnoreCase);
        }

        var options = new SePayOptions
        {
            AccountNumber = section["AccountNumber"]!,
            BankName = section["BankName"]!,
            WebhookSecret = section["WebhookSecret"]!,
            ApiToken = section["ApiToken"]!,
        };
        Assert.False(options.HasCredentials);
    }

    [Fact]
    public void CommittedAppSettings_PointApiBaseUrlAtTheSandboxHost()
    {
        var options = new SePayOptions { ApiBaseUrl = CommittedSection()["ApiBaseUrl"]! };

        Assert.True(options.PointsAtSandbox);
    }
}
