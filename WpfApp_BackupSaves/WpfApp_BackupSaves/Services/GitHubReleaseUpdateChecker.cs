using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackupSaves.Core.Services;

namespace WpfApp_BackupSaves.Services;

public sealed class ReleaseInfo
{
    public required string Version { get; init; }
    public required string TagName { get; init; }
    public required string ZipUrl { get; init; }
    public required string ZipName { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? HtmlUrl { get; init; }
}

public interface IUpdateChecker
{
    Task<ReleaseInfo?> GetNewerReleaseAsync(CancellationToken ct = default);
}

public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    public const string Owner = "digitallez";
    public const string Repo = "BackupSaves";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BackupSaves", AppVersion.Numeric.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public async Task<ReleaseInfo?> GetNewerReleaseAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        AppLog.Default.Info("Update", $"Checking {url}");

        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            AppLog.Default.Info("Update", "No releases yet (404)");
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonOpts, ct);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return null;

        var asset = release.Assets?.FirstOrDefault(a =>
            a.Name is not null &&
            a.Name.StartsWith("BackupSaves-", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl));

        if (asset is null)
        {
            AppLog.Default.Warn("Update", $"Release {release.TagName} has no BackupSaves-*.zip asset");
            return null;
        }

        var version = AppVersion.ParseCore(release.TagName)?.ToString()
                      ?? AppVersion.ParseCore(
                             (asset.Name ?? "").Replace("BackupSaves-", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace(".zip", "", StringComparison.OrdinalIgnoreCase))
                         ?.ToString()
                      ?? release.TagName.TrimStart('v', 'V');

        if (!AppVersion.ShouldOfferUpdate(version, AppVersion.Numeric, AppVersion.IsDebug))
        {
            AppLog.Default.Info("Update",
                $"Up to date (local={AppVersion.Current}, remote={version})");
            return null;
        }

        if (AppVersion.IsDebug && !AppVersion.IsNewer(version, AppVersion.Numeric))
        {
            AppLog.Default.Info("Update",
                $"Debug build: offering same-number Release {version} to replace {AppVersion.Current}");
        }

        return new ReleaseInfo
        {
            Version = version,
            TagName = release.TagName,
            ZipUrl = asset.BrowserDownloadUrl!,
            ZipName = asset.Name!,
            ReleaseNotes = release.Body,
            HtmlUrl = release.HtmlUrl
        };
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
