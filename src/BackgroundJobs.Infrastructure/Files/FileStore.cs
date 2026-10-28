using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace BackgroundJobs.Infrastructure.Files;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Folder shared by the API and the workers (a Docker volume in Compose).</summary>
    public string Root { get; set; } = Path.Combine(Path.GetTempPath(), "background-jobs");
}

public sealed record StoredFile(string Path, long Size, string Sha256);

/// <summary>
/// Files on a disk shared by the API and the workers. Paths are always generated here and kept relative,
/// so nothing a client sends ends up in a file system path.
/// </summary>
public sealed class FileStore(IOptions<StorageOptions> options, TimeProvider clock)
{
    private string Root => options.Value.Root;

    /// <summary>Copies the stream to a new file while hashing it, and stops once it grows past the limit.</summary>
    public async Task<StoredFile> SaveAsync(Stream content, string folder, string extension, long maxBytes, CancellationToken ct)
    {
        var relative = NewPath(folder, extension);
        var full = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        var buffer = new byte[81920];
        try
        {
            await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                size += read;
                if (size > maxBytes)
                {
                    throw new FileTooLargeException(maxBytes);
                }

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            Delete(relative);
            throw;
        }

        return new StoredFile(relative, size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public Stream OpenRead(string relativePath) =>
        new FileStream(FullPath(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    /// <summary>A new file to write into, with its relative path.</summary>
    public (string Path, Stream Stream) Create(string folder, string extension)
    {
        var relative = NewPath(folder, extension);
        var full = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return (relative, new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true));
    }

    public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

    public void Delete(string? relativePath)
    {
        if (!string.IsNullOrEmpty(relativePath) && File.Exists(FullPath(relativePath)))
        {
            File.Delete(FullPath(relativePath));
        }
    }

    private string NewPath(string folder, string extension) =>
        Path.Combine(folder, clock.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture), $"{Guid.NewGuid():N}{extension}");

    private string FullPath(string relativePath)
    {
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path is outside the storage folder.");
        }

        return full;
    }
}

public sealed class FileTooLargeException(long maxBytes) : Exception($"The file is larger than {maxBytes / (1024 * 1024)} MB.")
{
    public long MaxBytes { get; } = maxBytes;
}
