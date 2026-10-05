namespace CommonService.Application.Interfaces.Ports;

/// <summary>Delivers the plain OTP code to a phone (decisions Q06). The real SMS provider is deferred (Q06b).</summary>
public interface IOtpSender
{
    /// <param name="phoneNumber">Normalized national form, 0XXXXXXXXX (decisions Q20 O4).</param>
    /// <param name="code">The plain code; only its HMAC is stored by Identity.</param>
    Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default);
}
