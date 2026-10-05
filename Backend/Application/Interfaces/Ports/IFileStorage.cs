namespace CommonService.Application.Interfaces.Ports;

/// <summary>Where a stored file ended up. <see cref="Path"/> is the opaque key to pass back to the other methods.</summary>
public sealed record StoredFile(string Path, string Url, long SizeBytes);

/// <summary>Binary storage for before/after photos, signed guarantee PDFs and dispute evidence (local disk in development).</summary>
public interface IFileStorage
{
    /// <summary>Store <paramref name="content"/> under <paramref name="folder"/>. The storage may rename the file to keep keys unique.</summary>
    Task<StoredFile> SaveAsync(string folder, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Open a stored file, or return null when the path is unknown.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Delete a stored file. Deleting an unknown path is not an error.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
