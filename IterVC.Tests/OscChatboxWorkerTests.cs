using System.Collections.Concurrent;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class OscChatboxWorkerTests
{
    [TestMethod]
    public async Task EnabledWorker_FormatsAndSendsActiveMedia()
    {
        var sessions = new FakeMediaSessionService(new MediaInfo
        {
            Title = "Artist - Track", TimeInfo = "01:02 / 03:04"
        });
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}|{time}", true);

        await worker.StartAsync();
        try
        {
            Assert.AreEqual("Artist - Track|01:02 / 03:04",
                await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { await worker.StopAsync(); }

        await worker.StopAsync(); // Idempotent.
    }

    [TestMethod]
    public async Task NoActiveMedia_SendsStoppedPlaceholder()
    {
        var sessions = new FakeMediaSessionService(null);
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}|{time}", true);

        await worker.StartAsync();
        try
        {
            Assert.AreEqual("⏹️|00:00 / 00:00",
                await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task LegacyStatusToken_DoesNotAppearInSentMessage()
    {
        var sessions = new FakeMediaSessionService(new MediaInfo
        {
            Title = "Artist - Track", TimeInfo = "01:02 / 03:04"
        });
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}|{status}|{time}", true);

        await worker.StartAsync();
        try
        {
            var message = await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.AreEqual("Artist - Track||01:02 / 03:04", message);
            StringAssert.DoesNotContain(message, "{status}");
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task WhitespaceOnlyTitle_IsTreatedAsNoActiveMedia()
    {
        var sessions = new FakeMediaSessionService(new MediaInfo
        {
            Title = "   ", TimeInfo = "01:02 / 03:04"
        });
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}|{time}", true);

        await worker.StartAsync();
        try
        {
            Assert.AreEqual("⏹️|00:00 / 00:00",
                await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task PausedMedia_OptionEnabled_KeepsSongAndPausedIndicator()
    {
        var sessions = new FakeMediaSessionService(new MediaInfo
        {
            Title = "Artist - Track", TimeInfo = "01:02 / 03:04", IsPaused = true
        });
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}|{time}", true);

        await worker.StartAsync();
        try
        {
            Assert.AreEqual("Artist - Track ⏸️|01:02 / 03:04",
                await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.AreEqual(0, osc.ClearCount);
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task PausedMedia_OptionDisabled_ClearsOnceAndResumesPlayback()
    {
        var sessions = new FakeMediaSessionService(new MediaInfo
        {
            Title = "First track", TimeInfo = "01:02 / 03:04"
        });
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}", false);

        await worker.StartAsync();
        try
        {
            Assert.AreEqual("First track",
                await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1)));

            sessions.Current = new MediaInfo
            {
                Title = "First track", TimeInfo = "01:02 / 03:04", IsPaused = true
            };
            await WaitUntilAsync(() => osc.ClearCount == 1);
            await Task.Delay(30);
            Assert.AreEqual(1, osc.ClearCount);
            sessions.Current = new MediaInfo
            {
                Title = "Resumed track", TimeInfo = "01:03 / 03:04"
            };
            await WaitUntilAsync(() => osc.Messages.Contains("Resumed track"));
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task NoMedia_WithPauseHidingDisabled_StillSendsStoppedPlaceholder()
    {
        var sessions = new FakeMediaSessionService(null);
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(true, "{title}", false);

        await worker.StartAsync();
        try
        {
            Assert.AreEqual("⏹️",
                await osc.FirstMessage.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task DisabledWorker_DoesNotReadSessionsOrSendMessages()
    {
        var sessions = new FakeMediaSessionService(new MediaInfo
        {
            Title = "Artist - Track", TimeInfo = "01:02 / 03:04"
        });
        var osc = new FakeOscMediaService();
        var worker = CreateWorker(sessions, osc);
        worker.Configure(false, "{title}", true);

        await worker.StartAsync();
        try
        {
            await Task.Delay(25);
            Assert.AreEqual(0, sessions.CallCount);
            Assert.AreEqual(0, osc.Messages.Count);
            Assert.AreEqual(0, osc.ClearCount);
        }
        finally { await worker.StopAsync(); }
    }

    private static OscChatboxWorker CreateWorker(IMediaSessionService sessions, IOscMediaService osc) =>
        new(sessions, osc, NullLogger<OscChatboxWorker>.Instance, TimeSpan.FromMilliseconds(5));

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(1);
        while (!predicate() && DateTime.UtcNow < until)
            await Task.Delay(5);
        Assert.IsTrue(predicate(), "The expected OSC state was not reached.");
    }

    private sealed class FakeMediaSessionService : IMediaSessionService
    {
        private MediaInfo? _current;
        private int _callCount;

        public FakeMediaSessionService(MediaInfo? media) => _current = media;
        public MediaInfo? Current
        {
            get => Volatile.Read(ref _current);
            set => Volatile.Write(ref _current, value);
        }
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeOscMediaService : IOscMediaService
    {
        private readonly TaskCompletionSource<string> _firstMessage =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _clearCount;

        public ConcurrentQueue<string> Messages { get; } = new();
        public Task<string> FirstMessage => _firstMessage.Task;
        public int ClearCount => Volatile.Read(ref _clearCount);

        public void SendMessage(string message)
        {
            Messages.Enqueue(message);
            _firstMessage.TrySetResult(message);
        }

        public void ClearChatbox() => Interlocked.Increment(ref _clearCount);
    }
}
