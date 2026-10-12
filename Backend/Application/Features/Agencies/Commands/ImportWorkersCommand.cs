using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Common.Models;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>
/// Command to import workers from CSV file
/// </summary>
public sealed record ImportWorkersCommand(
    string CsvContent,
    int AgencyId,
    bool DryRun
) : IRequest<ApiResponse<ImportWorkerResponseDto>>;