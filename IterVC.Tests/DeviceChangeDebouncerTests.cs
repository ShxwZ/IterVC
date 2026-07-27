using IterVC.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class DeviceChangeDebouncerTests
{
    [TestMethod]
    public async Task Notify_WhenCalledInBurst_RaisesOneSettledChange()
    {
        var invocationCount = 0;
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var debouncer = new DeviceChangeDebouncer(() =>
        {
            Interlocked.Increment(ref invocationCount);
            invoked.TrySetResult();
        }, TimeSpan.FromMilliseconds(80));

        for (var index = 0; index < 8; index++)
        {
            debouncer.Notify();
            await Task.Delay(10);
        }

        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(120);

        Assert.AreEqual(1, Volatile.Read(ref invocationCount));
    }

    [TestMethod]
    public async Task Notify_AfterDispose_DoesNotRaiseChange()
    {
        var invocationCount = 0;
        var debouncer = new DeviceChangeDebouncer(
            () => Interlocked.Increment(ref invocationCount),
            TimeSpan.FromMilliseconds(30));

        debouncer.Dispose();
        debouncer.Notify();
        await Task.Delay(100);

        Assert.AreEqual(0, Volatile.Read(ref invocationCount));
    }
}
