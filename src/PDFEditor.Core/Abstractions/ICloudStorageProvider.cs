namespace PDFEditor.Core.Abstractions;

/// <summary>Abstraction over cloud storage providers (OneDrive, Google Drive, Dropbox, …).</summary>
public interface ICloudStorageProvider
{
    /// <summary>Friendly name shown in the UI, e.g. "OneDrive" or "Google Drive".</summary>
    string ProviderName { get; }

    /// <summary>Whether the user is currently authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Prompt the user to sign in (OAuth flow, device code, etc.).</summary>
    Task AuthenticateAsync(CancellationToken ct = default);

    /// <summary>Sign the user out and clear cached tokens.</summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>List items in the given remote <paramref name="folderPath"/>. Use "" for root.</summary>
    Task<IReadOnlyList<CloudItem>> ListFolderAsync(string folderPath = "", CancellationToken ct = default);

    /// <summary>Download a file and return its bytes.</summary>
    Task<byte[]> DownloadAsync(string remotePath, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Upload bytes to <paramref name="remotePath"/>, creating parent folders as required.</summary>
    Task UploadAsync(string remotePath, byte[] data, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Delete the item at <paramref name="remotePath"/>.</summary>
    Task DeleteAsync(string remotePath, CancellationToken ct = default);
}

/// <summary>Represents a file or folder item returned by <see cref="ICloudStorageProvider.ListFolderAsync"/>.</summary>
public record CloudItem(
    string Name,
    string RemotePath,
    bool IsFolder,
    long SizeBytes,
    DateTimeOffset LastModified);
