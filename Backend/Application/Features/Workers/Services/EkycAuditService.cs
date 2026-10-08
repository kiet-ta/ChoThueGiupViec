using CommonService.Application.Common.Options;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Implementation of eKYC sample audit logic (decisions.md Q05).
/// </summary>
public sealed class EkycAuditService(IOptions<BusinessRules> rulesOptions) : IEkycAuditService
{
    private readonly BusinessRules _rules = rulesOptions.Value;

    public bool ShouldAuditJob(Worker worker, double? randomRoll = null)
    {
        ArgumentNullException.ThrowIfNull(worker);

        if (worker.WorkerType != WorkerType.Freelancer)
        {
            return false;
        }

        if (worker.CompletedJobs <= _rules.Ekyc.FullAuditFirstJobs)
        {
            return true;
        }

        double roll = randomRoll ?? Random.Shared.NextDouble();
        return roll < (double)_rules.Ekyc.AuditRate;
    }
}
