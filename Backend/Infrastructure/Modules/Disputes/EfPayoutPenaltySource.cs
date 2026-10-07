using CommonService.Application.Features.Payouts;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Disputes;

/// <summary>
/// The Disputes side of <see cref="IPayoutPenaltySource"/> (contract payouts.md question P1): the deductions that resolved
/// <c>DISPUTE_TICKET</c> rows put on payees. Read-only. The amount of a verdict is split over the blamed assignments of the order by
/// gross value, like the <c>DisputeResolved</c> events, so each payee bears its own share.
/// </summary>
public sealed class EfPayoutPenaltySource(AppDbContext db) : IPayoutPenaltySource
{
    // DISPUTE_TICKET text values (disputes.md); the same strings the Disputes service writes.
    private const string Resolved = "RESOLVED";
    private const string AbsentFee = "ABSENT_FEE";

    public async Task<IReadOnlyDictionary<PayeeKey, decimal>> GetDecidedAsync(DateTime throughUtc, CancellationToken cancellationToken = default)
    {
        var tickets = await db.DisputeTickets.AsNoTracking()
            .Where(d => d.DisputeStatus == Resolved && d.ResolvedAt != null && d.ResolvedAt < throughUtc && d.CompensationAmount > 0)
            .Where(d => d.FaultParty == FaultParty.Freelancer || (d.FaultParty == FaultParty.Agency && d.Category == AbsentFee))
            .Select(d => new { d.OrderId, d.FaultParty, Amount = d.CompensationAmount!.Value })
            .ToListAsync(cancellationToken);
        if (tickets.Count == 0) return new Dictionary<PayeeKey, decimal>();

        var orderIds = tickets.Select(t => t.OrderId).Distinct().ToList();
        var assignments = await db.JobAssignments.AsNoTracking()
            .Where(a => orderIds.Contains(a.OrderId))
            .Select(a => new { a.OrderId, a.AssignmentId, a.WorkerId, a.AgencyId, a.GrossAmount })
            .ToListAsync(cancellationToken);

        var totals = new Dictionary<PayeeKey, decimal>();
        foreach (var ticket in tickets)
        {
            var blamed = assignments
                .Where(a => a.OrderId == ticket.OrderId && (ticket.FaultParty == FaultParty.Freelancer ? a.AgencyId is null : a.AgencyId is not null))
                .OrderBy(a => a.AssignmentId)
                .ToList();
            var shares = PayoutCalculator.SplitByWeight(ticket.Amount, blamed.Select(a => a.GrossAmount).ToList());

            for (var i = 0; i < blamed.Count; i++)
            {
                var payee = ticket.FaultParty == FaultParty.Freelancer
                    ? new PayeeKey(PayeeType.Freelancer, blamed[i].WorkerId)
                    : new PayeeKey(PayeeType.Agency, blamed[i].AgencyId!.Value);
                totals[payee] = totals.GetValueOrDefault(payee) + shares[i];
            }
        }

        return totals;
    }
}
