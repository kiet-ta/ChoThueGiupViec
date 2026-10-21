using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Workers;

/// <summary>Body of <c>POST /api/workers/assignments/{assignmentId}/photos</c> as <c>multipart/form-data</c> (contract workers.md 2.4.1).</summary>
public sealed class UploadJobPhotoFileForm
{
    /// <summary>The photo: JPEG, PNG or WebP, at most 5 MB.</summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    /// <summary>BEFORE or AFTER.</summary>
    [FromForm(Name = "photoPhase")]
    public string? PhotoPhase { get; set; }

    /// <summary>1 to 5; an AFTER photo must match the angle of an accepted BEFORE photo.</summary>
    [FromForm(Name = "angleNo")]
    public byte AngleNo { get; set; }
}
