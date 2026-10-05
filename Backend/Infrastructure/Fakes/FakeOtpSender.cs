using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Fakes;

/// <summary>
/// Writes the OTP to the log instead of sending an SMS (decisions Q06). It refuses to be created outside
/// Development, so a host that registered it fails at startup (see <see cref="OtpSenderStartupGuard"/>).
/// </summary>
public sealed class FakeOtpSender : IOtpSender
{
    private readonly ILogger<FakeOtpSender> _logger;
    private readonly ConcurrentQueue<(string PhoneNumber, string Code)> _sent = new();

    public FakeOtpSender(IHostEnvironment environment, ILogger<FakeOtpSender> logger)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"The Fake IOtpSender is only allowed in Development (current environment: '{environment.EnvironmentName}'). Register a real IOtpSender (decisions Q06).");
        }

        _logger = logger;
    }

    public IReadOnlyCollection<(string PhoneNumber, string Code)> Sent => _sent.ToArray();

    public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue((phoneNumber, code));
        _logger.LogWarning("[FAKE OTP] {PhoneNumber}: {Code}", phoneNumber, code);
        return Task.CompletedTask;
    }
}

/// <summary>Resolves <see cref="IOtpSender"/> when the host starts so that a forbidden Fake fails the startup, not the first login.</summary>
public sealed class OtpSenderStartupGuard : IHostedService
{
    public OtpSenderStartupGuard(IOtpSender sender)
    {
        _ = sender;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
