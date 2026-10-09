namespace CommonService.Application.Features.Disputes.Dtos;

/// <summary>Body of the two POST .../disputes/evidence endpoints (contract disputes.md 2.1a). Nullable so a missing value is a 400 with a field message.</summary>
public sealed class UploadEvidenceRequestDto
{
    public string? FileName { get; init; }

    /// <summary>image/jpeg, image/png or image/webp.</summary>
    public string? ContentType { get; init; }

    /// <summary>The photo, base64 encoded, at most 5 MB once decoded.</summary>
    public string? ContentBase64 { get; init; }
}

/// <summary>A stored evidence photo: put <see cref="Url"/> into the <c>evidenceUrls</c> of the dispute.</summary>
public sealed class EvidenceFileDto
{
    public string Path { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}
