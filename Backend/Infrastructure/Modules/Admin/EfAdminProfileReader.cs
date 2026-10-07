using CommonService.Application.Features.Admin;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Reads one ADMIN row without tracking it.</summary>
public sealed class EfAdminProfileReader(AppDbContext db) : IAdminProfileReader
{
    public Task<AdminAccount?> FindAsync(int adminId, CancellationToken cancellationToken = default) =>
        db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.AdminId == adminId, cancellationToken);
}
