using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Admin;

/// <summary>Read side of the ADMIN table for the profile endpoint (BE-M6-06a). Read-only.</summary>
public interface IAdminProfileReader
{
    /// <summary>Null when the row was removed.</summary>
    Task<AdminAccount?> FindAsync(int adminId, CancellationToken cancellationToken = default);
}
