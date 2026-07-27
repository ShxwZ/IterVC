using IterVC.Core.Models;
using IterVC.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class AudioMeterStateTests
{
    [TestMethod]
    public void Update_RisesFasterThanItFalls()
    {
        var state = new AudioMeterState();
        state.Update(new AudioLevelSnapshot(0, 0, true), 0);
        var rise = state.LevelDb - AudioLevelSnapshot.MinimumDb;

        state.Update(new AudioLevelSnapshot(-80, -80, true), 50);
        var fall = rise - (state.LevelDb - AudioLevelSnapshot.MinimumDb);

        Assert.IsTrue(rise > fall);
    }

    [TestMethod]
    public void Update_WhenStale_ResetsState()
    {
        var state = new AudioMeterState();
        state.Update(new AudioLevelSnapshot(-10, -5, true), 0);

        state.Update(AudioLevelSnapshot.Silence, 50);

        Assert.IsFalse(state.IsActive);
        Assert.AreEqual(AudioLevelSnapshot.MinimumDb, state.LevelDb);
        Assert.AreEqual(AudioLevelSnapshot.MinimumDb, state.PeakDb);
    }

    [TestMethod]
    public void Peak_HoldsThenDecays()
    {
        var state = new AudioMeterState();
        state.Update(new AudioLevelSnapshot(-20, -5, true), 0);
        state.Update(new AudioLevelSnapshot(-30, -30, true), 700);
        Assert.AreEqual(-5f, state.PeakDb);

        state.Update(new AudioLevelSnapshot(-30, -30, true), 800);
        Assert.IsTrue(state.PeakDb < -5f);
    }
}
