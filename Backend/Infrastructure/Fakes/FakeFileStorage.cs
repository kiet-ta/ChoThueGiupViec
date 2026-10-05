using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Keeps files in memory.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public int Count => _files.Count;

    public async Task<StoredFile> SaveAsync(string folder, string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var path = $"{folder.Trim('/')}/{Guid.NewGuid():N}-{Path.GetFileName(fileName)}";
        _files[path] = buffer.ToArray();
        return new StoredFile(path, "/files/" + path, buffer.Length);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(_files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        _files.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}
