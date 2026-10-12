namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Response object for worker import operation
/// </summary>
public sealed record ImportWorkerResponseDto
{
    /// <summary>Number of successfully imported workers</summary>
    public int SuccessfullyImported { get; init; }

    /// <summary>List of import errors, if any</summary>
    public List<ImportWorkerErrorDto> Errors { get; init; } = new();

    /// <summary>Whether the import was successful (no errors)</summary>
    public bool IsSuccessful => Errors.Count == 0;
}