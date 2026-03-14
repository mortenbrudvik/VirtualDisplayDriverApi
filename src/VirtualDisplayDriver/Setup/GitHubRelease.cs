using System.Text.Json.Serialization;

namespace VirtualDisplayDriver.Setup;

internal record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

internal record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
