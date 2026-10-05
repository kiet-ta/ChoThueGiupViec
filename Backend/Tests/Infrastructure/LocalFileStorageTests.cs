using System.Text;
using CommonService.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace CommonService.Tests.Infrastructure;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "storage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:BasePath"] = _tempDirectory
            })
            .Build();

        _storage = new LocalFileStorage(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup error on windows locks
            }
        }
    }

    [Fact]
    public async Task SaveAsync_creates_file_and_returns_valid_StoredFile()
    {
        var content = "Hello world storage test";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var stored = await _storage.SaveAsync("guarantees", "contract.pdf", stream, "application/pdf");

        Assert.NotNull(stored);
        Assert.NotEmpty(stored.Path);
        Assert.StartsWith("/files/guarantees/", stored.Url);
        Assert.Equal(content.Length, stored.SizeBytes);

        // Verify file exists on disk
        using var readStream = await _storage.OpenReadAsync(stored.Path);
        Assert.NotNull(readStream);
        using var reader = new StreamReader(readStream);
        var readContent = await reader.ReadToEndAsync();
        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task DeleteAsync_removes_file_from_storage()
    {
        var content = "To be deleted";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var stored = await _storage.SaveAsync("photos", "before.jpg", stream, "image/jpeg");

        // Verify it was saved
        var existsStream = await _storage.OpenReadAsync(stored.Path);
        Assert.NotNull(existsStream);
        existsStream.Dispose();

        // Delete
        await _storage.DeleteAsync(stored.Path);

        // Verify it is gone
        var goneStream = await _storage.OpenReadAsync(stored.Path);
        Assert.Null(goneStream);
    }

    [Fact]
    public async Task Path_traversal_attempt_returns_null_and_does_not_escape_base_path()
    {
        var outsideFile = Path.Combine(Path.GetTempPath(), "secret_file_" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outsideFile, "super secret");

        try
        {
            var traversalPath = "../../" + Path.GetFileName(outsideFile);
            var stream = await _storage.OpenReadAsync(traversalPath);
            Assert.Null(stream);

            // Attempt deletion via traversal
            await _storage.DeleteAsync(traversalPath);
            Assert.True(File.Exists(outsideFile)); // File should remain intact
        }
        finally
        {
            if (File.Exists(outsideFile))
            {
                File.Delete(outsideFile);
            }
        }
    }

    [Fact]
    public async Task OpenReadAsync_on_nonexistent_file_returns_null()
    {
        var stream = await _storage.OpenReadAsync("photos/does_not_exist.png");
        Assert.Null(stream);
    }
}
