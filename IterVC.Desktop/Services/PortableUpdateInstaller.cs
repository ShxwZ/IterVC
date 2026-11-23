using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.Services;

/// <summary>
/// Downloads only the official GitHub release asset, validates the digest advertised by
/// GitHub, and runs a separate, verified executable to replace the portable app on exit.
/// Never runs an update automatically: the user must invoke the Install command.
/// </summary>
internal sealed class PortableUpdateInstaller : IPortableUpdateInstaller
{
    private const long MaxDownloadBytes = 300L * 1024 * 1024;
    private const long MaxExecutableBytes = 300L * 1024 * 1024;
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/ShxwZ/IterVC/releases/latest");
    private readonly HttpClient _client;
    private readonly ILogger<PortableUpdateInstaller> _logger;

    internal PortableUpdateInstaller(HttpClient client, ILogger<PortableUpdateInstaller> logger)
    {
        _client = client;
        _logger = logger;
        if (!_client.DefaultRequestHeaders.UserAgent.Any())
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IterVC", "1.0"));
    }

    public async Task<bool> DownloadAndLaunchAsync(string version, CancellationToken cancellationToken = default)
    {
        string? temporaryDirectory = null;
        string? stagedExecutable = null;
        var launched = false;
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            var currentExecutable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExecutable) ||
                !string.Equals(Path.GetFileName(currentExecutable), "IterVC.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            var currentVersion = AppVersion.CurrentSemanticVersion;
            if (currentVersion is null || !AppVersion.IsNewer(version, currentVersion)) return false;

            using var metadataResponse = await _client.GetAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
            if (!metadataResponse.IsSuccessStatusCode) return false;
            var metadata = await metadataResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!TrySelectVerifiedAsset(metadata, version, out var asset)) return false;

            temporaryDirectory = Path.Combine(Path.GetTempPath(), "IterVC-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            var archivePath = Path.Combine(temporaryDirectory, "release.zip");

            using (var response = await _client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentLength is > MaxDownloadBytes)
                    return false;

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    131072, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = ArrayPool<byte>.Shared.Rent(131072);
                try
                {
                    long bytes = 0;
                    int read;
                    while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                               .ConfigureAwait(false)) != 0)
                    {
                        bytes += read;
                        if (bytes > MaxDownloadBytes) return false;
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                    if (bytes != asset.Size ||
                        !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }

            var directory = Path.GetDirectoryName(currentExecutable)!;
            stagedExecutable = Path.Combine(directory, "IterVC.update-" + Guid.NewGuid().ToString("N") + ".exe");
            await ExtractVerifiedExecutableAsync(archivePath, stagedExecutable, cancellationToken).ConfigureAwait(false);

            // The helper runs from another path, so Windows can replace the actual portable .exe.
            var helperExecutable = Path.Combine(temporaryDirectory, "IterVC.exe");
            File.Copy(stagedExecutable, helperExecutable);
            await using (var file = File.OpenRead(stagedExecutable))
            {
                var executableSha = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)
                    .ConfigureAwait(false));
                var processInfo = new ProcessStartInfo(helperExecutable)
                {
                    UseShellExecute = false,
                    WorkingDirectory = temporaryDirectory
                };
                processInfo.ArgumentList.Add(PortableUpdateBootstrap.Command);
                processInfo.ArgumentList.Add(currentExecutable);
                processInfo.ArgumentList.Add(stagedExecutable);
                processInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                processInfo.ArgumentList.Add(executableSha);
                using var process = Process.Start(processInfo);
                if (process is null) return false;
            }
            launched = true;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Could not prepare the portable update");
            return false;
        }
        finally
        {
            if (!launched)
            {
                TryDeleteFile(stagedExecutable);
                if (temporaryDirectory is not null)
                {
                    try { Directory.Delete(temporaryDirectory, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    internal static bool TrySelectVerifiedAsset(string json, string expectedVersion, out ReleaseAsset asset)
    {
        asset = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return false;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return false;
            var tag = root.GetProperty("tag_name").GetString();
            var normalized = AppVersion.NormalizeSemantic(tag);
            if (string.IsNullOrEmpty(tag) ||
                !string.Equals(normalized, AppVersion.NormalizeSemantic(expectedVersion), StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var candidate in root.GetProperty("assets").EnumerateArray())
            {
                if (candidate.GetProperty("name").GetString() != "IterVC-Windows.zip") continue;
                var size = candidate.GetProperty("size").GetInt64();
                var digest = candidate.GetProperty("digest").GetString();
                if (size is <= 0 or > MaxDownloadBytes ||
                    digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    return false;

                var sha256 = digest[7..];
                if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) return false;
                var rawUrl = candidate.GetProperty("browser_download_url").GetString();
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Port != 443 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                    !string.Equals(uri.AbsolutePath, "/ShxwZ/IterVC/releases/download/" +
                        Uri.EscapeDataString(tag) + "/IterVC-Windows.zip", StringComparison.Ordinal))
                    return false;

                asset = new ReleaseAsset(uri, size, sha256);
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
                                          or KeyNotFoundException or FormatException or ArgumentException)
        {
            return false;
        }
        return false;
    }

    internal static async Task ExtractVerifiedExecutableAsync(string archivePath, string targetPath,
        CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count != 1 || archive.Entries[0].FullName != "IterVC.exe" ||
            archive.Entries[0].Length is <= 0 or > MaxExecutableBytes)
            throw new InvalidDataException("Unexpected portable update archive contents");

        var entry = archive.Entries[0];
        await using var input = entry.Open();
        await using var destination = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            131072, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(131072);
        try
        {
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > MaxExecutableBytes || total > entry.Length)
                    throw new InvalidDataException("Portable executable exceeds expected size");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            if (total != entry.Length) throw new InvalidDataException("Incomplete portable executable");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static void TryDeleteFile(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal readonly record struct ReleaseAsset(Uri Url, long Size, string Sha256);
}
