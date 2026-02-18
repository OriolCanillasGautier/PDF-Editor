using NLog;
using PDFEditor.Core.Abstractions;

namespace PDFEditor.Core.Services.Cloud;

/// <summary>
/// Stub OneDrive provider. Needs the Microsoft.Graph and Azure.Identity NuGet packages
/// to be added and OAuth credentials configured before production use.
/// </summary>
public class OneDriveProvider : ICloudStorageProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string ProviderName => "OneDrive";
    public bool IsAuthenticated { get; private set; }

    public Task AuthenticateAsync(CancellationToken ct = default)
    {
        Log.Info("OneDrive: AuthenticateAsync — stub (Microsoft.Graph OAuth not yet wired).");
        // TODO: Use Microsoft.Graph SDK + Azure.Identity DeviceCodeCredential
        // var credential = new DeviceCodeCredential();
        // var graphClient = new GraphServiceClient(credential);
        IsAuthenticated = false;
        throw new NotImplementedException(
            "Install Microsoft.Graph and Azure.Identity, then implement OAuth sign-in here.");
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        IsAuthenticated = false;
        Log.Info("OneDrive: signed out.");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloudItem>> ListFolderAsync(string folderPath = "", CancellationToken ct = default)
        => throw new NotImplementedException("Implement with GraphServiceClient.Me.Drive.Root.Children.GetAsync()");

    public Task<byte[]> DownloadAsync(string remotePath, IProgress<double>? progress = null, CancellationToken ct = default)
        => throw new NotImplementedException("Implement with GraphServiceClient item download stream.");

    public Task UploadAsync(string remotePath, byte[] data, IProgress<double>? progress = null, CancellationToken ct = default)
        => throw new NotImplementedException("Implement with GraphServiceClient large-file upload session.");

    public Task DeleteAsync(string remotePath, CancellationToken ct = default)
        => throw new NotImplementedException("Implement with GraphServiceClient.Me.Drive.Items[id].DeleteAsync().");
}
