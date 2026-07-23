using System.Net.Http.Headers;
using System.Text.Json;

namespace IterVC.Desktop.Services;

internal sealed class GitHubUpdateService : IUpdateService
{
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/ShxwZ/IterVC/releases/latest");
    private readonly HttpClient _httpClient;

    internal GitHubUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IterVC", "1.0"));
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.Failed;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
            var version = AppVersion.NormalizeSemantic(release?.TagName);
            return version is not null && TryValidateReleaseUrl(release?.HtmlUrl, out var url)
                ? new UpdateCheckResult(true, version, url)
                : UpdateCheckResult.Failed;
        }
        catch (Exception)
        {
            return UpdateCheckResult.Failed;
        }
    }

    public bool TryValidateReleaseUrl(string? value, out string url)
    {
        url = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        url = uri.AbsoluteUri;
        return true;
    }

    private sealed class GitHubRelease
    {
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
