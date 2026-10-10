namespace CommonService.Application.Features.Payments.Services;

public enum IpnOutcome
{
    /// <summary>The IPN changed something (or is a harmless "still pending"): HTTP 200.</summary>
    Accepted,

    /// <summary>The transaction was already SUCCESS / REFUNDED / EXPIRED, or a concurrent identical IPN won: HTTP 200, nothing changed.</summary>
    AlreadyProcessed,

    /// <summary>Invalid signature, unknown reference, amount mismatch or an unsupported purpose: HTTP 400, nothing changed.</summary>
    Rejected,
}

public interface IIpnService
{
    /// <summary>
    /// Contract payments.md 2.4. <paramref name="payload"/> is the callback's raw key/value pairs (given to the gateway for the
    /// signature check), <paramref name="rawBody"/> is stored as ipn_payload on a state change.
    /// </summary>
    Task<IpnOutcome> HandleAsync(IReadOnlyDictionary<string, string> payload, string rawBody, CancellationToken cancellationToken = default);
}
