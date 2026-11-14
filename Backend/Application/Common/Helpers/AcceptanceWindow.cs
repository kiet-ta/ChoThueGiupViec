using CommonService.Application.Common.Options;
using CommonService.Domain.Enums;

namespace CommonService.Application.Common.Helpers;

/// <summary>The phase of an assignment in the acceptance window, as both apps show it (decision D32).</summary>
public static class AcceptancePhases
{
    public const string InProgress = "IN_PROGRESS";
    public const string OnSite = "ON_SITE";
    public const string Review = "REVIEW";
    public const string Disputed = "DISPUTED";
    public const string Completed = "COMPLETED";
}

/// <summary>
/// The acceptance block shared by the customer and the worker (decision D32, contract workers.md / booking.md). Every time is UTC and comes
/// from the server: the apps never count time themselves. Null for an assignment outside the execution (offered, cancelled, absent...).
/// </summary>
public record AcceptanceDto(
    string Phase,
    DateTime? SubmittedAt,
    DateTime? RedoUntil,
    DateTime? AutoAcceptAt,
    DateTime? DisputeUntil,
    string? CompletionSource,
    int RedoCount);

/// <summary>
/// The pure rules of the 24-hour acceptance window (decision D32, ACCEPT-01). T is <c>submitted_at</c>: the moment the worker submitted the
/// AFTER photos. T to T+RedoMinutes the worker is on site and a redo can be asked; T+ReminderHours the customer is reminded; T+AutoAcceptHours
/// the job is accepted automatically when no dispute is open, and the dispute window closes.
/// </summary>
public static class AcceptanceWindow
{
    public static DateTime RedoUntil(DateTime submittedAt, AcceptanceRules rules) => submittedAt.AddMinutes(rules.RedoMinutes);

    public static DateTime ReminderAt(DateTime submittedAt, AcceptanceRules rules) => submittedAt.AddHours(rules.ReminderHours);

    public static DateTime AutoAcceptAt(DateTime submittedAt, AcceptanceRules rules) => submittedAt.AddHours(rules.AutoAcceptHours);

    /// <summary>A redo is possible only while the worker is still on site: now strictly before T+RedoMinutes.</summary>
    public static bool CanRedo(DateTime? submittedAt, DateTime nowUtc, AcceptanceRules rules) =>
        submittedAt is { } t && nowUtc < RedoUntil(t, rules);

    /// <summary>Auto-acceptance is due from T+AutoAcceptHours (inclusive) when no dispute of the order is open.</summary>
    public static bool IsAutoAcceptDue(DateTime? submittedAt, bool disputeOpen, DateTime nowUtc, AcceptanceRules rules) =>
        submittedAt is { } t && !disputeOpen && nowUtc >= AutoAcceptAt(t, rules);

    /// <summary>The block for one assignment; null when the assignment is not in the execution part of its life (nothing to show).</summary>
    public static AcceptanceDto? Describe(
        JobAssignmentStatus status, DateTime? submittedAt, bool disputeOpen, CompletionSource? source, int redoCount, DateTime nowUtc, AcceptanceRules rules)
    {
        DateTime? redoUntil = submittedAt is { } a ? RedoUntil(a, rules) : null;
        DateTime? until = submittedAt is { } b ? AutoAcceptAt(b, rules) : null;
        var sourceText = source is { } s ? DbEnum.ToDb(s) : null;

        return status switch
        {
            JobAssignmentStatus.InProgress => new AcceptanceDto(AcceptancePhases.InProgress, null, null, null, null, null, redoCount),
            JobAssignmentStatus.AwaitingAcceptance when disputeOpen =>
                new AcceptanceDto(AcceptancePhases.Disputed, submittedAt, redoUntil, null, until, null, redoCount),
            JobAssignmentStatus.AwaitingAcceptance when submittedAt is { } t && nowUtc < RedoUntil(t, rules) =>
                new AcceptanceDto(AcceptancePhases.OnSite, t, redoUntil, until, until, null, redoCount),
            JobAssignmentStatus.AwaitingAcceptance =>
                new AcceptanceDto(AcceptancePhases.Review, submittedAt, redoUntil, until, until, null, redoCount),
            JobAssignmentStatus.Completed =>
                new AcceptanceDto(AcceptancePhases.Completed, submittedAt, redoUntil, null, until, sourceText, redoCount),
            _ => null,
        };
    }
}
