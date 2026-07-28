using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class MicrophoneRoutingCoordinatorTests
{
    [TestMethod]
    public void Coordinator_ForwardsValidCountAndUnsubscribesOnDispose()
    {
        var microphone = new Mock<IMicrophoneService>();
        var router = new Mock<IAudioRouterService>();
        var coordinator = new MicrophoneRoutingCoordinator(microphone.Object, router.Object);
        var buffer = new byte[32];

        microphone.Raise(service => service.DataAvailable += null,
            new AudioDataEventArgs(buffer, 12));
        router.Verify(service => service.FeedMicrophoneSamples(buffer, 12), Times.Once);

        coordinator.Dispose();
        microphone.Raise(service => service.DataAvailable += null,
            new AudioDataEventArgs(buffer, 12));
        router.Verify(service => service.FeedMicrophoneSamples(buffer, 12), Times.Once);
    }
}
