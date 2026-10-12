using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles getting available subscription packages
/// </summary>
public sealed class GetPackagesCommandHandler : IRequestHandler<GetPackagesCommand, ApiResponse<IReadOnlyList<PackageDto>>>
{
    public Task<ApiResponse<IReadOnlyList<PackageDto>>> Handle(GetPackagesCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement actual package retrieval from database or configuration
        // This is a placeholder implementation that needs to be completed

        var packages = new List<PackageDto>
        {
            new PackageDto(1, "Free", "Gói thuê bao Free (3-5 thợ, hoa hồng 20%)", 5, 20, null, null, DateTime.UtcNow),
            new PackageDto(2, "Pro", "Gói thuê bao Pro (thu phí tháng/quý, mở Shift Roster Dashboard, báo cáo, nhãn Verified Partner, tăng quota, ưu tiên điều phối)", 100, 15, 500000, 1350000, DateTime.UtcNow)
        };

        return Task.FromResult(ApiResponse<IReadOnlyList<PackageDto>>.Ok(packages));
    }
}