using System.Text.RegularExpressions;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Application.Features.Disputes.Services;

/// <summary>Stores one evidence photo of a dispute and returns the address to put into <c>evidenceUrls</c> (contract disputes.md 2.1a).</summary>
public interface IDisputeEvidenceService
{
    Task<DisputeResult<EvidenceFileDto>> UploadAsync(
        DisputeSide side, int userId, UploadEvidenceRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The photo is stored through <see cref="IFileStorage"/> (whose summary names dispute evidence) in the folder <see cref="Folder"/>.
/// The declared type, the size and the first bytes must agree, because the stored file is served back to anybody who knows its
/// (unguessable) name.
/// </summary>
public sealed partial class DisputeEvidenceService(IFileStorage storage) : IDisputeEvidenceService
{
    /// <summary>One folder level only: <c>LocalFileStorage</c> keeps just the last segment of the folder.</summary>
    public const string Folder = "dispute-evidence";

    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxFileNameLength = 100;

    /// <summary>Allowed content types and the extension the stored file gets.</summary>
    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    /// <summary>The content type to answer with for a stored file, by extension; null for anything that is not an evidence image.</summary>
    public static string? ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null,
    };

    /// <summary>A stored name is only letters, digits, dot, dash and underscore, so it can never leave the folder.</summary>
    public static bool IsSafeStoredName(string fileName) => fileName.Length <= 200 && SafeStoredName().IsMatch(fileName) && ContentTypeOf(fileName) is not null;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeStoredName();

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex UnsafeChars();

    public async Task<DisputeResult<EvidenceFileDto>> UploadAsync(
        DisputeSide side, int userId, UploadEvidenceRequestDto request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        var contentType = (request.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var rawName = (request.FileName ?? string.Empty).Trim();
        byte[] bytes = [];

        if (!Extensions.ContainsKey(contentType)) errors["contentType"] = ["Must be image/jpeg, image/png or image/webp."];

        if (rawName.Length == 0 || rawName.Length > MaxFileNameLength)
        {
            errors["fileName"] = [$"The file name must be 1-{MaxFileNameLength} characters."];
        }

        var base64 = request.ContentBase64 ?? string.Empty;
        if (base64.Length == 0)
        {
            errors["contentBase64"] = ["The photo is required."];
        }
        else if (base64.Length > MaxBytes / 3 * 4 + 8)
        {
            errors["contentBase64"] = ["The photo must be at most 5 MB."];
        }
        else
        {
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                errors["contentBase64"] = ["Must be valid base64."];
            }

            if (!errors.ContainsKey("contentBase64"))
            {
                if (bytes.Length == 0) errors["contentBase64"] = ["The photo is empty."];
                else if (bytes.Length > MaxBytes) errors["contentBase64"] = ["The photo must be at most 5 MB."];
                else if (Extensions.ContainsKey(contentType) && !MatchesType(contentType, bytes))
                {
                    errors["contentBase64"] = [$"The content is not a {contentType} image."];
                }
            }
        }

        if (errors.Count > 0) return DisputeResult<EvidenceFileDto>.ValidationError(errors);

        var stem = UnsafeChars().Replace(Path.GetFileNameWithoutExtension(rawName.Replace('\\', '/').Split('/')[^1]), "_");
        if (!stem.Any(char.IsLetterOrDigit)) stem = "photo";
        if (stem.Length > 60) stem = stem[..60];
        var prefix = side == DisputeSide.Customer ? "c" : "w";
        var storedName = $"{prefix}{userId}-{stem}{Extensions[contentType]}";

        await using var content = new MemoryStream(bytes, writable: false);
        var stored = await storage.SaveAsync(Folder, storedName, content, contentType, cancellationToken);
        return DisputeResult<EvidenceFileDto>.Created(new EvidenceFileDto
        {
            Path = stored.Path,
            Url = stored.Url,
            SizeBytes = stored.SizeBytes,
        });
    }

    private static bool MatchesType(string contentType, byte[] b) => contentType switch
    {
        "image/jpeg" => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        "image/png" => b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,
        "image/webp" => b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,
        _ => false,
    };
}
