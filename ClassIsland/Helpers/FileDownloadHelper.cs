using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ClassIsland.Helpers;

/// <summary>
/// Downloads a URL to a file with <see cref="HttpClient"/>.
/// Progress percentage is set only when the response includes Content-Length.
/// </summary>
public static class FileDownloadHelper
{
    public readonly struct DownloadProgressReport
    {
        public DownloadProgressReport(long receivedBytes, long? totalBytes)
        {
            ReceivedBytes = receivedBytes;
            TotalBytes = totalBytes;
        }

        public long ReceivedBytes { get; }

        public long? TotalBytes { get; }

        public double? ProgressPercentage
        {
            get
            {
                if (TotalBytes is long total && total > 0)
                {
                    return ReceivedBytes * 100.0 / total;
                }

                return null;
            }
        }
    }

    public static async Task<long> DownloadAsync(
        string url,
        string destinationPath,
        Action<DownloadProgressReport>? progress = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        using (var client = new HttpClient())
        {
            client.Timeout = timeout ?? Timeout.InfiniteTimeSpan;
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength;
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var httpStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    return await CopyToAsync(httpStream, file, cancellationToken, progress, totalBytes).ConfigureAwait(false);
                }
            }
        }
    }

    public static async Task<long> CopyToAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken = default,
        Action<DownloadProgressReport>? progress = null,
        long? totalBytes = null)
    {
        var buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Invoke(new DownloadProgressReport(received, totalBytes));
        }

        progress?.Invoke(new DownloadProgressReport(received, totalBytes));
        return received;
    }
}
