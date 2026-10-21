namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// What a job photo file may be (WORKERS-PHOTO-01, contract workers.md 2.4.1). The limits and the signature check are the ones
/// <c>DisputeEvidenceService</c> uses for its photos, kept here as Workers-owned constants because modules do not call each other.
/// </summary>
public static class JobPhotoFileRules
{
    /// <summary>Folder of <c>IFileStorage</c> that holds the job photos.</summary>
    public const string Folder = "job-photos";

    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    /// <summary>The declared type without its parameters, lower case ("image/jpeg; charset=x" becomes "image/jpeg").</summary>
    public static string NormalizeContentType(string? contentType) =>
        (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

    public static bool IsAllowedContentType(string contentType) => Extensions.ContainsKey(contentType);

    public static string ExtensionOf(string contentType) => Extensions[contentType];

    /// <summary>The content type of a stored photo, from the extension of its storage path; null when it is not one of ours.</summary>
    public static string? ContentTypeOf(string storagePath) => Path.GetExtension(storagePath).ToLowerInvariant() switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null,
    };

    /// <summary>True when the first bytes are the signature of the declared type, so a renamed file is not taken for a photo.</summary>
    public static bool SignatureMatches(string contentType, ReadOnlySpan<byte> b) => contentType switch
    {
        "image/jpeg" => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        "image/png" => b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,
        "image/webp" => b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,
        _ => false,
    };

    /// <summary>
    /// A photo row whose <c>image_url</c> is a storage path under <see cref="Folder"/> has a stored file; any other value is a URL the
    /// client sent (the Development-only JSON body, or a row from before the upload existed) and has no file of ours.
    /// </summary>
    public static bool IsStoredPath(string imageUrl) => imageUrl.StartsWith(Folder + "/", StringComparison.Ordinal);

    /// <summary>The authenticated address of the stored file of a photo (the one the clients are given as <c>photoUrl</c>).</summary>
    public static string FileAddress(long assignmentId, long photoId) => $"/api/workers/assignments/{assignmentId}/photos/{photoId}/file";
}
