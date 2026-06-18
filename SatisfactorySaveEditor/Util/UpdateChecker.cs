using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SuperLightLogger;

namespace SatisfactorySaveEditor.Util
{
    public static class UpdateChecker
    {
        private const string ReleasesEndpoint = "https://api.github.com/repos/Goz3rr/SatisfactorySaveEditor/releases";

        private static readonly ILog log = LogManager.GetCurrentClassLogger();
        private static readonly HttpClient httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            // GitHub API は User-Agent ヘッダーを必須とする
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SatisfactorySaveEditor");
            return client;
        }

        public static async Task<VersionInfo> GetLatestReleaseInfo()
        {
            try
            {
                var json = await httpClient.GetStringAsync(ReleasesEndpoint);
                var versions = JsonSerializer.Deserialize<IList<VersionInfo>>(json);
                return versions != null && versions.Count > 0 ? versions[0] : null;
            }
            catch (Exception ex)
            {
                // ネットワーク断・API レート制限・JSON 不正で更新チェックが落ちても、アプリ本体は動き続けるべき。
                // 呼び出し側は null を「更新情報なし」として扱う（手動チェックでも無反応にならない）。
                log.Error(ex);
                return null;
            }
        }

        public class VersionInfo
        {
            [JsonPropertyName("html_url")]
            public string ReleaseUrl { get; set; }

            [JsonPropertyName("tag_name")]
            public string TagName { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("body")]
            public string Changelog { get; set; }

            [JsonPropertyName("published_at")]
            public DateTime ReleaseDateTime { get; set; }

            public bool IsNewer()
            {
                var version = Assembly.GetEntryAssembly()?.GetName().Version;
                if (version == null) return false;

                // タグが "v1.2.3" 等の数値以外（"nightly" 等）でも FormatException を投げず「新しくない」とみなす。
                if (string.IsNullOrWhiteSpace(TagName) ||
                    !Version.TryParse(TagName.TrimStart('v', 'V'), out var newVersion))
                    return false;

                return newVersion > version;
            }
        }
    }
}
