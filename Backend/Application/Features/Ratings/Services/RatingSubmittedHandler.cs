using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Ratings.Services;

/// <summary>
/// What a saved rating sets in motion (BR-09, decisions Q14), in one unit of work with its audit rows:
/// a CUSTOMER rating moves <c>WORKER.rating_avg</c> (the column matching, the profile, the favourite list and order tracking read) to the
/// average of <see cref="IWorkerReputation"/>, and flags a freelancer whose average just fell under 4.00 after 10 jobs; a WORKER rating
/// flags a customer who got three low ratings in a row from different workers. A flag is an audit row the Admin reads in the audit page;
/// it never blocks anybody (Q14: the worker's rating of a customer is internal only).
/// </summary>
public sealed class RatingSubmittedHandler(
    IRatingRepository ratings,
    IWorkerRatingStore store,
    IWorkerReputation reputation,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IOptions<BusinessRules> rules) : INotificationHandler<RatingSubmitted>
{
    public const string FlagField = "low_rating_flag";
    public const string WorkerEntityType = "WORKER";
    public const string CustomerEntityType = "CUSTOMER";

    public async Task Handle(RatingSubmitted notification, CancellationToken cancellationToken)
    {
        var assignment = await ratings.GetAssignmentAsync(notification.AssignmentId, cancellationToken);
        if (assignment is null) return;

        if (string.Equals(notification.RaterRole, "CUSTOMER", StringComparison.Ordinal))
        {
            await OnCustomerRatingAsync(assignment.WorkerId, cancellationToken);
        }
        else if (string.Equals(notification.RaterRole, "WORKER", StringComparison.Ordinal))
        {
            await OnWorkerRatingAsync(assignment.CustomerId, cancellationToken);
        }
    }

    private async Task OnCustomerRatingAsync(int workerId, CancellationToken cancellationToken)
    {
        var current = await reputation.GetAsync(workerId, cancellationToken);
        if (current is null || current.RatingAvg <= 0m) return; // no customer rating yet: nothing to store

        var change = await store.SetAverageAsync(workerId, current.RatingAvg, cancellationToken);
        if (change is null) return;

        var rule = rules.Value.Rating.MinAvgAfterJobs;
        var crossedDown = change.PreviousAverage >= rule.Rating && current.RatingAvg < rule.Rating;
        if (change.WorkerType == WorkerType.Freelancer && crossedDown && current.CompletedJobs >= rule.Jobs)
        {
            await audit.WriteAsync(
                new AuditEntry(AuditActorType.System, null, WorkerEntityType, workerId.ToString(), FlagField, "false", "true",
                    string.Create(CultureInfo.InvariantCulture,
                        $"rating average {current.RatingAvg:0.00} is below {rule.Rating:0.00} after {current.CompletedJobs} completed jobs")),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task OnWorkerRatingAsync(int customerId, CancellationToken cancellationToken)
    {
        var rule = rules.Value.Rating;
        var run = rule.ConsecutiveLowToFlag;
        if (run < 1) return;

        // One more than the run, so the flag is written only by the rating that completes the streak and not by the ones after it.
        var recent = await store.GetRecentRatingsOfCustomerAsync(customerId, run + 1, cancellationToken);
        if (!IsLowRun(recent, 0, run, rule.LowStarThreshold)) return;
        if (IsLowRun(recent, 1, run, rule.LowStarThreshold)) return;

        await audit.WriteAsync(
            new AuditEntry(AuditActorType.System, null, CustomerEntityType, customerId.ToString(), FlagField, "false", "true",
                string.Create(CultureInfo.InvariantCulture, $"{run} consecutive ratings of {rule.LowStarThreshold} stars or less from different workers")),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary><paramref name="length"/> ratings from <paramref name="offset"/> on, all low, each from another worker.</summary>
    private static bool IsLowRun(IReadOnlyList<RecentCustomerRating> recent, int offset, int length, int lowStarThreshold)
    {
        if (recent.Count < offset + length) return false;

        var window = recent.Skip(offset).Take(length).ToList();
        return window.All(r => r.Stars <= lowStarThreshold) && window.Select(r => r.WorkerId).Distinct().Count() == length;
    }
}
