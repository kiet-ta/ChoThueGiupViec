using CommonService.Application.Common.Models;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Agencies;

/// <summary>
/// Agency guarantee and worker import (contract agencies.md 4, BE-M5-03, decisions Q18, Q19, policy PartnerOnly). The signed guarantee
/// PDF comes first; then the <c>.xlsx</c> is validated by a dry-run and committed all-or-nothing.
/// </summary>
[ApiController]
[Route("api/agencies")]
[Authorize(Policy = "PartnerOnly")]
public class AgencyWorkerImportController(WorkerImportService service) : ControllerBase
{
    /// <summary>The downloadable import template (header row: full_name, phone_number, national_id, bank_name, bank_account_no).</summary>
    [HttpGet("workers/import/template")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public IActionResult Template() => File(WorkerImportService.BuildTemplate(), XlsxWriter.ContentType, "worker-import-template.xlsx");

    /// <summary>Uploads the signed guarantee PDF; records <c>guarantee_signed_at</c> (decisions Q19).</summary>
    [HttpPost("guarantee")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<GuaranteeSignedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SignGuarantee(IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            return BadRequest(ApiResponse<object>.Fail("file is required."));
        }

        await using var stream = file.OpenReadStream();
        var signedAt = await service.SignGuaranteeAsync(stream, file.FileName, file.Length, ct);
        return Ok(ApiResponse<GuaranteeSignedDto>.Ok(new GuaranteeSignedDto(signedAt)));
    }

    /// <summary>Validates every row and reports the errors by row; writes nothing.</summary>
    [HttpPost("workers/import/dry-run")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DryRun(IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            return BadRequest(ApiResponse<object>.Fail("file is required."));
        }

        await using var stream = file.OpenReadStream();
        return Ok(ApiResponse<ImportResultDto>.Ok(await service.DryRunAsync(stream, ct)));
    }

    /// <summary>Creates every worker of the file, or none (an invalid row or the quota rejects the whole file with 422).</summary>
    [HttpPost("workers/import/commit")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Commit(IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            return BadRequest(ApiResponse<object>.Fail("file is required."));
        }

        await using var stream = file.OpenReadStream();
        try
        {
            return Ok(ApiResponse<ImportResultDto>.Ok(await service.CommitAsync(stream, ct)));
        }
        catch (ImportRejectedException rejected)
        {
            return UnprocessableEntity(ApiResponse<ImportResultDto>.Fail(rejected.Message, rejected.Result));
        }
    }
}

public sealed record GuaranteeSignedDto(DateTime GuaranteeSignedAt);
