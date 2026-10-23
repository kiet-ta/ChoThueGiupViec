using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Background worker that transitions Retiring workers to Retired when they have no active or future jobs.
/// Pattern follows AgencySwapExpiryWorker (AGENCY-UAT-02).
/// </summary>
public sealed class WorkerRetirementSweep(
    IServiceProvider serviceProvider,
    IClock clock,
    ILogger<WorkerRetirementSweep>? logger = null) : BackgroundService
{
    private readonly ILogger<WorkerRetirementSweep> _logger = logger ?? NullLogger<WorkerRetirementSweep>.Instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker retirement sweep failed; will retry on next interval.");
            }

            // Run hourly (pattern from AgencySwapExpiryWorker)
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var agencyRepository = scope.ServiceProvider.GetRequiredService<IAgencyRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        var retiringWorkers = await agencyRepository.GetRetiringWorkersReadyForRetiredAsync(cancellationToken);
        if (retiringWorkers.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var worker in retiringWorkers)
        {
            try
            {
                worker.TransitionTo(WorkStatus.Retired);
                worker.UpdatedAt = now;

                // Re-publish handover for any future jobs (the rescue handler is idempotent)
                var futureJobs = await agencyRepository.GetFutureAssignedJobsAsync(worker.WorkerId, cancellationToken);
                foreach (var job in futureJobs)
                {
                    await publisher.Publish(
                        new AgencySwapWindowExpired(job.AssignmentId, 0, worker.AgencyId ?? 0, "worker retired by agency", now),
                        cancellationToken);
                }

                _logger.LogInformation("Worker {WorkerId} transitioned from Retiring to Retired", worker.WorkerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transition worker {WorkerId} to Retired", worker.WorkerId);
            }
        }
    }
}