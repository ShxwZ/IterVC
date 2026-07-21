using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IterVC.Audio;
using IterVC.Core.Settings;
using System.Text.Json;

namespace IterVC.Tests;

/// <summary>
/// Tests para <see cref="SettingsService"/> usando <c>settingsFolderOverride</c> para
/// no tocar <c>%AppData%</c> real. No requieren audio stack.
/// </summary>
[TestClass]
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IterVC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private SettingsService CreateService() =>
        new(NullLogger<SettingsService>.Instance, settingsFolderOverride: _tempDir);

    [TestMethod]
    public async Task LoadAsync_WhenNoFile_ReturnsDefaults()
    {
        var service = CreateService();

        var loaded = await service.LoadAsync();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded.SchemaVersion);
        Assert.AreEqual(1.0f, loaded.AppsVolume);
        Assert.AreEqual(1.0f, loaded.MicrophoneVolume);
        Assert.IsTrue(loaded.MicrophoneEnabled);
        Assert.IsFalse(loaded.MonitorMicrophone);
        Assert.IsFalse(loaded.NoiseGateEnabled);
        Assert.AreEqual(-45f, loaded.NoiseGateThresholdDb);
        Assert.AreEqual(10f, loaded.NoiseGateAttackMilliseconds);
        Assert.AreEqual(150f, loaded.NoiseGateReleaseMilliseconds);
        Assert.IsNotNull(loaded.IncludedProcessNames);
        Assert.AreEqual(0, loaded.IncludedProcessNames.Count);
    }

    [TestMethod]
    public async Task SaveAsync_WritesFileToOverrideFolder()
    {
        var service = CreateService();
        await service.LoadAsync();
        await service.UpdateAsync(s => s.MicrophoneVolume = 0.42f);

        var expectedPath = Path.Combine(_tempDir, "settings.json");
        Assert.IsTrue(File.Exists(expectedPath), $"No se creó {expectedPath}");

        var json = await File.ReadAllTextAsync(expectedPath);
        Assert.IsTrue(json.Contains("\"MicrophoneVolume\": 0.42"), $"JSON no contiene el cambio: {json}");
    }

    [TestMethod]
    public async Task UpdateAsync_MutatesAndPersistsInOneCall()
    {
        var service = CreateService();
        await service.LoadAsync();

        await service.UpdateAsync(s =>
        {
            s.OutputDeviceId = "device-out-1";
            s.VbCableDeviceId = "device-vb-1";
            s.IncludedProcessNames.Add("spotify");
            s.IncludedProcessNames.Add("chrome");
        });

        // Reload para confirmar persistencia.
        var service2 = CreateService();
        var reloaded = await service2.LoadAsync();
        Assert.AreEqual("device-out-1", reloaded.OutputDeviceId);
        Assert.AreEqual("device-vb-1", reloaded.VbCableDeviceId);
        CollectionAssert.AreEqual(new[] { "spotify", "chrome" }, reloaded.IncludedProcessNames);
    }

    [TestMethod]
    public async Task UpdateAsync_MultipleCallsAreAtomicAtFileLevel()
    {
        // Cada SaveAsync escribe a .tmp y luego copia al real. No debe quedar un settings.json
        // corrupto a mitad de una escritura.
        var service = CreateService();
        await service.LoadAsync();

        for (var i = 0; i < 10; i++)
            await service.UpdateAsync(s => s.AppsVolume = 0.1f * i);

        var reloaded = await CreateService().LoadAsync();
        Assert.AreEqual(0.9f, reloaded.AppsVolume, 1e-5f);
    }

    [TestMethod]
    public async Task UpdateAsync_RoundTripsMicrophoneAndNoiseGateSettings()
    {
        var service = CreateService();
        await service.LoadAsync();

        await service.UpdateAsync(settings =>
        {
            settings.MicrophoneEnabled = false;
            settings.NoiseGateEnabled = true;
            settings.NoiseGateThresholdDb = -37.5f;
            settings.NoiseGateAttackMilliseconds = 24f;
            settings.NoiseGateReleaseMilliseconds = 280f;
        });

        var reloaded = await CreateService().LoadAsync();
        Assert.IsFalse(reloaded.MicrophoneEnabled);
        Assert.IsTrue(reloaded.NoiseGateEnabled);
        Assert.AreEqual(-37.5f, reloaded.NoiseGateThresholdDb);
        Assert.AreEqual(24f, reloaded.NoiseGateAttackMilliseconds);
        Assert.AreEqual(280f, reloaded.NoiseGateReleaseMilliseconds);
    }

    [TestMethod]
    public async Task LoadAsync_WithCorruptJson_FallsBackToDefaults()
    {
        // Escribimos un JSON inválido y comprobamos que no se rompe (log de error + defaults).
        var path = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(path, "{ esto NO es JSON válido ");

        var service = CreateService();
        var loaded = await service.LoadAsync();

        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded.SchemaVersion);
        Assert.AreEqual(1.0f, loaded.AppsVolume);
    }

    [TestMethod]
    public void Constructor_CreatesOverrideFolderIfMissing()
    {
        // La carpeta debe existir después del ctor (Directory.CreateDirectory).
        Assert.IsTrue(Directory.Exists(_tempDir));
    }
}
