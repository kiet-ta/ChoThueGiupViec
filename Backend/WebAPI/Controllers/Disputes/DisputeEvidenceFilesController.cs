using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Disputes;

/// <summary>
/// Serves a stored dispute evidence photo (contract disputes.md 2.1a). Anonymous on purpose: a browser sends no bearer header for an
/// image, and the Admin console shows the photos in <c>&lt;img&gt;</c>. The name contains a GUID chosen by the server, so only somebody
/// who was given the address can ask for it. Only the evidence folder, only image extensions, never a path.
/// </summary>
[ApiController]
[AllowAnonymous]
public class DisputeEvidenceFilesController(IFileStorage storage) : ControllerBase
{
    [HttpGet("files/" + DisputeEvidenceService.Folder + "/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string fileName, CancellationToken ct)
    {
        if (!DisputeEvidenceService.IsSafeStoredName(fileName)) return NotFound();

        var stream = await storage.OpenReadAsync($"{DisputeEvidenceService.Folder}/{fileName}", ct);
        if (stream is null) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(stream, DisputeEvidenceService.ContentTypeOf(fileName)!);
    }
}
