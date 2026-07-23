using System.Net;
using System.Text;
using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    public async Task CheckAsync_ReturnsValidatedLatestRelease()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{"tag_name":"v1.4.0","html_url":"https://github.com/ShxwZ/IterVC/releases/tag/v1.4.0"}"""));
        var service = new GitHubUpdateService(client);

        var result = await service.CheckAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual("1.4.0", result.Version);
        Assert.AreEqual("https://github.com/ShxwZ/IterVC/releases/tag/v1.4.0", result.ReleaseUrl);
    }

    [DataTestMethod]
    [DataRow("http://github.com/ShxwZ/IterVC/releases/tag/v1", false)]
    [DataRow("https://example.com/ShxwZ/IterVC/releases/tag/v1", false)]
    [DataRow("https://github.com/ShxwZ/IterVC/releases/tag/v1", true)]
    [DataRow("not-a-url", false)]
    public void TryValidateReleaseUrl_OnlyAllowsHttpsGitHub(string value, bool expected)
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        IUpdateService service = new GitHubUpdateService(client);
        Assert.AreEqual(expected, service.TryValidateReleaseUrl(value, out _));
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsFailureForOfflineResponse()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, "offline"));
        var result = await new GitHubUpdateService(client).CheckAsync();
        Assert.IsFalse(result.Success);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }
}
