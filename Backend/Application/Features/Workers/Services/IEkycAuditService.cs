using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Domain service interface for eKYC sample audit logic (decisions.md Q05).
/// </summary>
public interface IEkycAuditService
{
    /// <summary>
    /// Evaluates whether a worker's completed job should be selected for eKYC sample audit according to Q05 rules:
    /// - Agency workers: never audited for eKYC (returns false).
    /// - Freelancer with completedJobs &lt;= FullAuditFirstJobs (default 5): 100% audit (returns true).
    /// - Freelancer with completedJobs &gt; 5: random sample audit at AuditRate (default 20%, returns true if roll &lt; AuditRate).
    /// </summary>
    bool ShouldAuditJob(Worker worker, double? randomRoll = null);
}
