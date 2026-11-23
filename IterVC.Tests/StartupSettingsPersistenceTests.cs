using IterVC.Audio;
using IterVC.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class StartupSettingsPersistenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "IterVCStartupTests_" + Guid.NewGuid().ToString("N"));

    public StartupSettingsPersistenceTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    [TestMethod]
    public void AppSettings_DefaultsToHiddenWindowsStartup() =>
        Assert.IsTrue(new AppSettings().StartHiddenOnWindowsStartup);

    [TestMethod]
    public async Task UpdateAsync_RoundTripsHiddenWindowsStartupPreference()
    {
        var service = new SettingsService(NullLogger<SettingsService>.Instance, _folder);
        await service.LoadAsync();
        await service.UpdateAsync(settings => settings.StartHiddenOnWindowsStartup = false);

        var reloaded = await new SettingsService(NullLogger<SettingsService>.Instance, _folder).LoadAsync();

        Assert.IsFalse(reloaded.StartHiddenOnWindowsStartup);
        Assert.IsTrue(File.ReadAllText(Path.Combine(_folder, "settings.json")).Contains("StartHiddenOnWindowsStartup"));
    }
}
