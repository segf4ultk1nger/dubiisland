using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassIsland.Shared.Helpers;

/// <summary>
/// APIs that exist on .NET 6 but not on .NET Framework 4.7.2.
/// </summary>
public static class FrameworkCompat
{
    /// <summary>
    /// Full path of the running executable.
    /// </summary>
    public static string ProcessPath
    {
        get
        {
            // Entry assembly, not this helper's assembly. Shared.dll is not the process path.
            var entry = Assembly.GetEntryAssembly();
            var location = entry?.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return location;
            }

            return Process.GetCurrentProcess().MainModule?.FileName ?? "";
        }
    }

    /// <summary>
    /// Id of the current process.
    /// </summary>
    public static int ProcessId => Process.GetCurrentProcess().Id;

    /// <summary>
    /// Reads an entire file as text.
    /// </summary>
    public static Task<string> ReadAllTextAsync(string path)
    {
        return Task.Run(() => File.ReadAllText(path));
    }

    /// <summary>
    /// Reads an entire file as text. The token only cancels scheduling.
    /// </summary>
    public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() => File.ReadAllText(path), cancellationToken);
    }

    /// <summary>
    /// Writes text to a file, replacing any existing content.
    /// </summary>
    public static Task WriteAllTextAsync(string path, string contents)
    {
        return Task.Run(() => File.WriteAllText(path, contents));
    }

    /// <summary>
    /// Uppercase hex encoding of <paramref name="bytes"/>, without separators.
    /// </summary>
    public static string ToHexString(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    /// <summary>
    /// Computes an MD5 hash.
    /// </summary>
    public static byte[] Md5(byte[] data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data);
    }

    /// <summary>
    /// Computes a SHA-256 hash.
    /// </summary>
    public static byte[] Sha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    /// <summary>
    /// Fills a new buffer with cryptographically strong random bytes.
    /// </summary>
    public static byte[] GetRandomBytes(int count)
    {
        var bytes = new byte[count];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Relative path from <paramref name="relativeTo"/> to <paramref name="path"/>, using directory separators.
    /// </summary>
    public static string GetRelativePath(string relativeTo, string path)
    {
        var root = Path.GetFullPath(relativeTo);
        var full = Path.GetFullPath(path);
        var separator = Path.DirectorySeparatorChar;
        if (root.Length == 0 || (root[root.Length - 1] != separator && root[root.Length - 1] != Path.AltDirectorySeparatorChar))
        {
            root += separator;
        }

        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return full.Substring(root.Length);
        }

        var relative = Uri.UnescapeDataString(new Uri(root).MakeRelativeUri(new Uri(full)).ToString());
        return relative.Replace('/', separator);
    }

    /// <summary>
    /// net472 has no <c>string.Contains(string, StringComparison)</c>.
    /// </summary>
    public static bool Contains(this string source, string value, StringComparison comparison)
    {
        return source.IndexOf(value, comparison) >= 0;
    }

    /// <summary>
    /// net472 has no <c>string.Contains(char)</c>.
    /// </summary>
    public static bool Contains(this string source, char value)
    {
        return source.IndexOf(value) >= 0;
    }

    /// <summary>
    /// net472 <see cref="Dictionary{TKey,TValue}"/> has no TryAdd.
    /// </summary>
    public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        if (dictionary.ContainsKey(key))
        {
            return false;
        }

        dictionary.Add(key, value);
        return true;
    }

    /// <summary>
    /// net472 has no <c>string.Split(string)</c>.
    /// </summary>
    public static string[] Split(this string source, string separator)
    {
        return source.Split(new[] { separator }, StringSplitOptions.None);
    }

    /// <summary>
    /// Joins process arguments the way <c>ProcessStartInfo.ArgumentList</c> would, quoting spaces and quotes.
    /// </summary>
    public static string JoinArguments(params string[] arguments)
    {
        return string.Join(" ", arguments.Select(QuoteArgument));
    }

    /// <summary>
    /// GET a string. The token is honored by <see cref="HttpClient.GetAsync(string, CancellationToken)"/>.
    /// </summary>
    public static async Task<string> GetStringAsync(this HttpClient client, string requestUri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// GET a string. The token is honored by <see cref="HttpClient.GetAsync(Uri, CancellationToken)"/>.
    /// </summary>
    public static async Task<string> GetStringAsync(this HttpClient client, Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// GET a byte array. The token is honored by <see cref="HttpClient.GetAsync(Uri, CancellationToken)"/>.
    /// </summary>
    public static async Task<byte[]> GetByteArrayAsync(this HttpClient client, Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads content as a string. Callers cancel via <c>GetAsync</c>; net472 cannot pass the token into the read.
    /// </summary>
    public static Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
    }

    private static string QuoteArgument(string arg)
    {
        if (arg.Length != 0 && arg.IndexOf(' ') < 0 && arg.IndexOf('\t') < 0 && arg.IndexOf('"') < 0)
        {
            return arg;
        }

        var escaped = arg.Replace("\"", "\\\"");
        var trailingSlashes = 0;
        for (var i = arg.Length - 1; i >= 0 && arg[i] == '\\'; i--)
        {
            trailingSlashes++;
        }

        return "\"" + escaped + new string('\\', trailingSlashes) + "\"";
    }
}

/// <summary>
/// Locked stand-in for <c>Random.Shared</c>.
/// </summary>
public static class SharedRandom
{
    private static readonly Random Random = new Random();
    private static readonly object Gate = new object();

    /// <summary>
    /// Returns a random integer in [<paramref name="minValue"/>, <paramref name="maxValue"/>).
    /// </summary>
    public static int Next(int minValue, int maxValue)
    {
        lock (Gate)
        {
            return Random.Next(minValue, maxValue);
        }
    }
}
