using System.Buffers.Binary;
using System.Text;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class AimpMediaSessionServiceTests
{
    [TestMethod]
    public void ReadTrackTitle_DecodesUnicodeAndSkipsUnusedFields()
    {
        var buffer = MakeBuffer("Album", "Artísta 🎵", "2026", @"C:\Music\track.flac", "Rock", "Canción 日本語");
        Assert.AreEqual("Artísta 🎵 - Canción 日本語", AimpMediaSessionService.ReadTrackTitle(buffer));
    }

    [TestMethod]
    public void ReadTrackTitle_UsesLocalFileNameWithoutExposingPath()
    {
        var buffer = MakeBuffer("", "", "", @"C:\Music\Private\Track.flac", "", "");
        Assert.AreEqual("Track", AimpMediaSessionService.ReadTrackTitle(buffer));
    }

    [TestMethod]
    public void ReadTrackTitle_DoesNotUseStreamUrlAsTitle()
    {
        var buffer = MakeBuffer("", "", "", "https://example.com/stream?token=private", "", "");
        Assert.IsNull(AimpMediaSessionService.ReadTrackTitle(buffer));
    }

    [TestMethod]
    public void ReadTrackTitle_RejectsInvalidLengths()
    {
        var buffer = MakeBuffer("", "Artist", "", "", "", "Track");
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(44), uint.MaxValue);
        Assert.IsNull(AimpMediaSessionService.ReadTrackTitle(buffer));
        Assert.IsNull(AimpMediaSessionService.ReadTrackTitle(new byte[87]));
    }

    [TestMethod]
    public void FormatPlaybackTime_ClampsPositionAndHandlesUnknownDuration()
    {
        Assert.AreEqual("02:00 / 02:00", AimpMediaSessionService.FormatPlaybackTime(130000, 120000));
        Assert.AreEqual("01:02 / 00:00", AimpMediaSessionService.FormatPlaybackTime(62000, 0));
        Assert.AreEqual("00:00 / 00:00", AimpMediaSessionService.FormatPlaybackTime(-1, -1));
    }

    [TestMethod]
    public async Task Composite_PrefersPlayingWindowsMedia()
    {
        var windows = new FakeProvider(Media("Windows"));
        var aimp = new FakeProvider(Media("AIMP"));
        var service = new CompositeMediaSessionService(windows, aimp);

        Assert.AreEqual("Windows", (await service.GetActiveMediaInfoAsync())?.Title);
        Assert.AreEqual(0, aimp.Calls);
    }

    [TestMethod]
    public async Task Composite_PrefersPlayingAimpOverPausedWindows()
    {
        var service = new CompositeMediaSessionService(
            new FakeProvider(Media("Windows", true)), new FakeProvider(Media("AIMP")));
        Assert.AreEqual("AIMP", (await service.GetActiveMediaInfoAsync())?.Title);
    }

    [TestMethod]
    public async Task Composite_FallsBackToPausedAimpWhenWindowsHasNoTitle()
    {
        var service = new CompositeMediaSessionService(
            new FakeProvider(Media(" ")), new FakeProvider(Media("AIMP", true)));
        var result = await service.GetActiveMediaInfoAsync();
        Assert.AreEqual("AIMP", result?.Title);
        Assert.IsTrue(result!.IsPaused);
    }

    [TestMethod]
    public async Task Composite_PreservesPausedWindowsAndReturnsNullWhenBothUnavailable()
    {
        var service = new CompositeMediaSessionService(
            new FakeProvider(Media("Windows", true)), new FakeProvider(null));
        Assert.IsTrue((await service.GetActiveMediaInfoAsync())!.IsPaused);

        service = new CompositeMediaSessionService(new FakeProvider(null), new FakeProvider(null));
        Assert.IsNull(await service.GetActiveMediaInfoAsync());
    }

    private static MediaInfo Media(string title, bool paused = false) =>
        new() { Title = title, TimeInfo = "00:00 / 01:00", IsPaused = paused };

    private static byte[] MakeBuffer(params string[] fields)
    {
        var buffer = new byte[2048];
        var offset = 88;
        for (var i = 0; i < fields.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(40 + i * 4), (uint)fields[i].Length);
            var bytes = Encoding.Unicode.GetBytes(fields[i]);
            bytes.CopyTo(buffer, offset);
            offset += bytes.Length;
        }
        return buffer;
    }

    private sealed class FakeProvider(MediaInfo? media) : IMediaSessionService
    {
        public int Calls { get; private set; }

        public Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(media);
        }
    }
}
