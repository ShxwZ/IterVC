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
    public async Task EnabledWorker_FormatsAndSendsMediaThenStopsIdempotently()
    {
        var sessions = new FakeMediaSessionService();
        var osc = new FakeOscMediaService();
        var worker = new OscChatboxWorker(sessions, osc, NullLogger<OscChatboxWorker>.Instance,
            TimeSpan.FromMilliseconds(5));
        worker.Configure(true, "{title}|{status}|{time}");

        await worker.StartAsync();
        await WaitUntilAsync(() => osc.Messages.Count > 0);
        await worker.StopAsync();
        await worker.StopAsync();

        Assert.AreEqual("Artist - Track|Playing|01:02 / 03:04", osc.Messages[0]);
    }

    [TestMethod]
    public async Task DisabledWorker_DoesNotReadSessionsOrSend()
    {
        var sessions = new FakeMediaSessionService();
        var osc = new FakeOscMediaService();
        var worker = new OscChatboxWorker(sessions, osc, NullLogger<OscChatboxWorker>.Instance,
            TimeSpan.FromMilliseconds(5));
        worker.Configure(false, "{title}");

        await worker.StartAsync();
        await Task.Delay(25);
        await worker.StopAsync();

        Assert.AreEqual(0, sessions.CallCount);
        Assert.AreEqual(0, osc.Messages.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.IsTrue(predicate());
    }

    private sealed class FakeMediaSessionService : IMediaSessionService
    {
        public int CallCount { get; private set; }
        public Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<MediaInfo?>(new MediaInfo
            {
                Title = "Artist - Track", Status = "Playing", TimeInfo = "01:02 / 03:04"
            });
        }
    }

    private sealed class FakeOscMediaService : IOscMediaService
    {
        public List<string> Messages { get; } = [];
        public void SendMediaInfo(string? title, string? status, string template) => Messages.Add(template);
        public void ClearChatbox() { }
    }
}
