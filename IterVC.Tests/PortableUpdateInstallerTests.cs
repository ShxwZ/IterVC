using System.IO.Compression;
using System.Text.Json;
using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class PortableUpdateInstallerTests
{
    private const string Digest = "sha256:839738571549a81a28aca81e04cfad25d576bf76edc32b7bdbf7f18c35234d92";
    private const string AssetUrl = "https://github.com/ShxwZ/IterVC/releases/download/v1.4.0/IterVC-Windows.zip";

    private static string ReleaseJson(string url = AssetUrl, string? digest = Digest,
        string tag = "v1.4.0", long size = 123456, bool draft = false, bool prerelease = false) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            assets = new[] { new { name = "IterVC-Windows.zip", size, digest, browser_download_url = url } }
        });

    [TestMethod]
    public void OfficialRelease_DigestAndAssetValidated()
    {
        Assert.IsTrue(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(), "1.4.0", out var asset));
        Assert.AreEqual(AssetUrl, asset.Url.AbsoluteUri);
        Assert.AreEqual(123456L, asset.Size);
        Assert.AreEqual(Digest[7..], asset.Sha256);
    }

    [DataTestMethod]
    [DataRow("https://evil.example/release.zip")]
    [DataRow("http://github.com/ShxwZ/IterVC/releases/download/v1.4.0/IterVC-Windows.zip")]
    [DataRow("https://github.com/AnotherOwner/IterVC/releases/download/v1.4.0/IterVC-Windows.zip")]
    [DataRow("https://github.com/ShxwZ/IterVC/releases/download/v1.4.0/IterVC-Windows.zip?redirect=1")]
    [DataRow("https://github.com/ShxwZ/IterVC/releases/download/v1.5.0/IterVC-Windows.zip")]
    public void UnexpectedAssetUrl_IsRejected(string url) =>
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(url: url), "1.4.0", out _));

    [TestMethod]
    public void WrongVersionOrMissingDigest_IsRejected()
    {
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(tag: "v1.3.0"), "1.4.0", out _));
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(digest: null), "1.4.0", out _));
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(digest: "sha256:xyz"), "1.4.0", out _));
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(size: 500_000_000), "1.4.0", out _));
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(draft: true), "1.4.0", out _));
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset(ReleaseJson(prerelease: true), "1.4.0", out _));
        Assert.IsFalse(PortableUpdateInstaller.TrySelectVerifiedAsset("{}", "1.4.0", out _));
    }

    [TestMethod]
    public async Task ExtractVerifiedExecutable_RequiresSingleExactRootFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "IterVC_UpdateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var goodArchive = Path.Combine(directory, "good.zip");
            using (var archive = ZipFile.Open(goodArchive, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("IterVC.exe");
                await using var stream = entry.Open();
                await stream.WriteAsync(new byte[] { 77, 90, 0, 1 });
            }
            var output = Path.Combine(directory, "staged.exe");
            await PortableUpdateInstaller.ExtractVerifiedExecutableAsync(goodArchive, output);
            CollectionAssert.AreEqual(new byte[] { 77, 90, 0, 1 }, await File.ReadAllBytesAsync(output));

            var badArchive = Path.Combine(directory, "bad.zip");
            using (var archive = ZipFile.Open(badArchive, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../IterVC.exe");
                await using var stream = entry.Open();
                await stream.WriteAsync(new byte[] { 1 });
            }
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                PortableUpdateInstaller.ExtractVerifiedExecutableAsync(
                    badArchive, Path.Combine(directory, "bad.exe")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task MultipleZipEntries_AreRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "IterVC_UpdateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var archivePath = Path.Combine(directory, "two.zip");
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                zip.CreateEntry("IterVC.exe");
                zip.CreateEntry("malicious.dll");
            }
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                PortableUpdateInstaller.ExtractVerifiedExecutableAsync(
                    archivePath, Path.Combine(directory, "staged.exe")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
