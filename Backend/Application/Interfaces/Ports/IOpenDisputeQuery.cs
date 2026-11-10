namespace CommonService.Application.Interfaces.Ports;

/// <summary>Returns the supplied orders that have an OPEN or IN_REVIEW dispute.</summary>
public interface IOpenDisputeQuery
{
    Task<IReadOnlySet<long>> GetOpenOrderIdsAsync(IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default);
}
