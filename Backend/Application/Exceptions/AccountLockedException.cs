namespace CommonService.Application.Exceptions;

/// <summary>
/// Exception thrown when an account is temporarily locked after repeated failed logins.
/// Maps to HTTP 423 Locked with a Retry-After header of <see cref="RetryAfterSeconds"/>.
/// </summary>
public class AccountLockedException : Exception
{
    public int RetryAfterSeconds { get; }

    public AccountLockedException(int retryAfterSeconds, string message = "Account is temporarily locked after repeated failed logins.")
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
