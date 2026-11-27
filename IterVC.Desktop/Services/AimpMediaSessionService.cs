using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.Services;

internal sealed class AimpMediaSessionService(ILogger<AimpMediaSessionService> logger) : IMediaSessionService
{
    // AIMP 5.40 SDK, apiRemote.h: fixed-width packed header and UTF-16LE strings.
    // https://aimp.ru/?cat=sdk&do=download&os=windows
    private const string RemoteClass = "AIMP2_RemoteInfo";
    private const int MapSize = 2048;
    private const int HeaderSize = 88;
    private const int LengthsOffset = 40;
    private const uint PropertyMessage = 0x0477;
    private const uint PositionProperty = 0x20;
    private const uint DurationProperty = 0x30;
    private const uint StateProperty = 0x40;
    private const uint AbortIfHung = 0x0002;
    private const uint ErrorOnExit = 0x0020;
    private const uint TimeoutMs = 100;

    public Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadMediaInfo(cancellationToken), cancellationToken);

    private MediaInfo? ReadMediaInfo(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var window = FindWindow(RemoteClass, RemoteClass);
            if (window == IntPtr.Zero || !TryGetProperty(window, StateProperty, out var state)
                || state is not (1 or 2))
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            using var mapping = MemoryMappedFile.OpenExisting(RemoteClass, MemoryMappedFileRights.Read);
            using var view = mapping.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);
            var buffer = new byte[MapSize];
            if (view.ReadArray(0, buffer, 0, buffer.Length) != buffer.Length) return null;

            var title = ReadTrackTitle(buffer);
            if (title is null) return null;

            cancellationToken.ThrowIfCancellationRequested();
            TryGetProperty(window, PositionProperty, out var position);
            if (!TryGetProperty(window, DurationProperty, out var duration))
                duration = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16, 4));

            // Do not publish a stale track if playback stopped or changed state during the read.
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetProperty(window, StateProperty, out var latestState) || latestState != state)
                return null;

            return new MediaInfo
            {
                Title = title,
                TimeInfo = FormatPlaybackTime(position, duration),
                IsPaused = state == 1
            };
        }
        catch (FileNotFoundException)
        {
            // AIMP can close between finding its window and opening the shared mapping.
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Unable to read the AIMP remote media session");
            return null;
        }
    }

    internal static string? ReadTrackTitle(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize || buffer.Length > MapSize) return null;

        // Six character counts: album, artist, date, file name, genre, title.
        // Never interpret the obsolete pointer fields or trust an unchecked string length.
        var offset = HeaderSize;
        string artist = string.Empty, title = string.Empty, fileName = string.Empty;
        for (var index = 0; index < 6; index++)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(
                buffer.Slice(LengthsOffset + index * sizeof(uint), sizeof(uint)));
            if (length > (uint)((buffer.Length - offset) / 2)) return null;

            var byteLength = (int)length * 2;
            if (index is 1 or 3 or 5)
            {
                var value = Encoding.Unicode.GetString(buffer.Slice(offset, byteLength)).TrimEnd('\0').Trim();
                if (index == 1) artist = value;
                else if (index == 3) fileName = value;
                else title = value;
            }
            offset += byteLength;
        }

        // Untagged local tracks can use their file name, but never send a path or stream URL.
        if (string.IsNullOrWhiteSpace(title) && Path.IsPathFullyQualified(fileName))
            title = Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrWhiteSpace(title)) return null;
        return WindowsMediaSessionService.FormatTitle(artist, title);
    }

    internal static string FormatPlaybackTime(long positionMs, long durationMs)
    {
        // The remote properties are millisecond values. Negative/overflow values are unknown.
        var duration = durationMs is >= 0 and <= uint.MaxValue ? durationMs : 0;
        var position = positionMs is >= 0 and <= uint.MaxValue ? positionMs : 0;
        if (duration > 0) position = Math.Min(position, duration);

        return WindowsMediaSessionService.FormatTime(
            TimeSpan.FromMilliseconds(position), TimeSpan.FromMilliseconds(duration));
    }

    private static bool TryGetProperty(IntPtr window, uint property, out long value)
    {
        value = 0;
        if (SendMessageTimeout(window, PropertyMessage, (UIntPtr)property, IntPtr.Zero,
                AbortIfHung | ErrorOnExit, TimeoutMs, out var result) == IntPtr.Zero)
            return false;

        value = result.ToInt64();
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
