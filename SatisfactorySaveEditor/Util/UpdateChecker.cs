using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SatisfactorySaveEditor.Util
{
    public static class UpdateChecker
    {
        private const string ReleasesEndpoint = "https://api.github.com/repos/Goz3rr/SatisfactorySaveEditor/releases";

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
            var json = await httpClient.GetStringAsync(ReleasesEndpoint);
            var versions = JsonSerializer.Deserialize<IList<VersionInfo>>(json);
            return versions != null && versions.Count > 0 ? versions[0] : null;
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
                var version = Assembly.GetEntryAssembly().GetName().Version;
                var newVersion = new Version(TagName.TrimStart('v', 'V'));

                return newVersion > version;
            }
        }
    }
}
