using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ambysto.Power.Core.Licensing;

public enum UpdateState { Current, Available, Failed }
public sealed record UpdateResult(UpdateState State, Version? Version, Uri? DownloadPage);

public sealed class UpdateChecker : IDisposable
{
    public static Uri FeedUri { get; } = new("https://power.ambysto.com/windows/latest.json");
    public const int MaxManifestBytes = 16384;
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;

    public UpdateChecker()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        };
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = RequestTimeout
        };
    }

    public UpdateChecker(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _httpClient = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = RequestTimeout
        };
    }

    public async Task<UpdateResult> CheckAsync(Version installedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);

        try
        {
            using var response = await _httpClient
                .GetAsync(FeedUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateResult(UpdateState.Failed, null, null);
            }

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxManifestBytes)
            {
                return new UpdateResult(UpdateState.Failed, null, null);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[MaxManifestBytes + 1];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead > MaxManifestBytes)
            {
                return new UpdateResult(UpdateState.Failed, null, null);
            }

            string json = Encoding.UTF8.GetString(buffer, 0, totalRead);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("version", out JsonElement versionEl)
                || versionEl.GetString() is not string versionStr
                || !Version.TryParse(versionStr, out Version? remoteVersion))
            {
                return new UpdateResult(UpdateState.Failed, null, null);
            }

            Uri? downloadUri = null;
            if (root.TryGetProperty("url", out JsonElement urlEl) && urlEl.GetString() is string urlStr)
            {
                if (!Uri.TryCreate(urlStr, UriKind.Absolute, out downloadUri)
                    || !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return new UpdateResult(UpdateState.Failed, null, null);
                }
            }

            if (remoteVersion > installedVersion)
            {
                if (downloadUri is null)
                {
                    return new UpdateResult(UpdateState.Failed, null, null);
                }

                return new UpdateResult(UpdateState.Available, remoteVersion, downloadUri);
            }

            return new UpdateResult(UpdateState.Current, remoteVersion, downloadUri);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new UpdateResult(UpdateState.Failed, null, null);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
