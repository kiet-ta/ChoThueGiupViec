using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Configuration;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Real IFileStorage storing files on the local disk (development and on-premise deployments).
/// Configured via "FileStorage:BasePath" or defaults to AppContext.BaseDirectory/App_Data/Uploads.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;

    public LocalFileStorage(IConfiguration configuration)
    {
        var configuredPath = configuration["FileStorage:BasePath"];
        _basePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data", "Uploads")
            : Path.GetFullPath(configuredPath);
    }

    public async Task<StoredFile> SaveAsync(
        string folder,
        string fileName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        // Sanitize file name and folder to prevent path traversal
        var safeFolder = Path.GetFileName(folder);
        var safeFileName = Path.GetFileName(fileName);
        var uniqueFileName = $"{Guid.NewGuid():N}_{safeFileName}";

        var targetDir = Path.Combine(_basePath, safeFolder);
        Directory.CreateDirectory(targetDir);

        var fullPath = Path.Combine(targetDir, uniqueFileName);
        var relativePath = Path.Combine(safeFolder, uniqueFileName).Replace('\\', '/');

        await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var fileInfo = new FileInfo(fullPath);
        var url = $"/files/{relativePath}";

        return new StoredFile(relativePath, url, fileInfo.Length);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            // Path traversal attempt
            return Task.FromResult<Stream?>(null);
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
