using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services.Cloud;

/// <summary>
/// Stub Google Drive provider. Needs the Google.Apis.Drive.v3 NuGet package
/// and OAuth 2.0 credentials before production use.
/// </summary>
public class GoogleDriveProvider : ICloudStorageProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string ProviderName => "Google Drive";
    public bool IsAuthenticated { get; private set; }

    public Task AuthenticateAsync(CancellationToken ct = default)
    {
        Log.Info("Google Drive: AuthenticateAsync — stub (Google.Apis OAuth not yet wired).");
        // TODO: Use Google.Apis.Auth + Google.Apis.Drive.v3.DriveService
        // var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(...)
        IsAuthenticated = false;
        throw new NotImplementedException(
            "Install Google.Apis.Drive.v3 and configure credentials.json, then implement OAuth sign-in here.");
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        IsAuthenticated = false;
        Log.Info("GoogleDrive: signed out.");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloudItem>> ListFolderAsync(string folderPath = "", CancellationToken ct = default)
        => throw new NotImplementedException("Implement with DriveService.Files.List().");

    public Task<byte[]> DownloadAsync(string remotePath, IProgress<double>? progress = null, CancellationToken ct = default)
        => throw new NotImplementedException("Implement with DriveService.Files.Get() download stream.");

    public Task UploadAsync(string remotePath, byte[] data, IProgress<double>? progress = null, CancellationToken ct = default)
        => throw new NotImplementedException("Implement with ResumableUpload for large files.");

    public Task DeleteAsync(string remotePath, CancellationToken ct = default)
        => throw new NotImplementedException("Implement with DriveService.Files.Delete().");
}
