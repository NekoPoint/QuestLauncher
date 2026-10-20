using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MinimalQuestLauncher
{
    internal sealed class UpdateCheckResult
    {
        public bool Ok;
        public bool HasNewerVersion;
        public string CurrentVersion;
        public string LatestTag;
        public string LatestName;
        public string HtmlUrl;
        public string ErrorMessage;
    }

    internal static class UpdateChecker
    {
        public const int TimeoutMs = 4500;

        public static UpdateCheckResult CheckLatest()
        {
            var r = new UpdateCheckResult { CurrentVersion = AppInfo.Version, HtmlUrl = AppInfo.ReleasesUrl };

            try
            {

                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                }
                catch
                {
                }

                var req = (HttpWebRequest)WebRequest.Create(AppInfo.LatestReleaseApi);
                req.Method = "GET";
                req.UserAgent = "QuestLauncher/" + AppInfo.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;

                string body;
                using (var resp = (HttpWebResponse)req.GetResponse()) using (
                    var stream = resp.GetResponseStream()) using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
                }

                return ParseGitHubReleaseJson(body, AppInfo.Version);
            }
            catch (WebException wex)
            {
                Log.Warn("Проверка обновлений: " + wex.Message);
                r.Ok = false;
                r.ErrorMessage = "Не удалось связаться с GitHub (нет сети, превышен тайм-аут или лимит запросов). Вы " +
                                 "можете открыть страницу релизов вручную.";
                return r;
            }
            catch (Exception ex)
            {
                Log.Warn("Проверка обновлений: " + ex.Message);
                r.Ok = false;
                r.ErrorMessage = "Не удалось проверить обновления: " + ex.Message;
                return r;
            }
        }

        public static UpdateCheckResult ParseGitHubReleaseJson(string json, string currentVersion)
        {
            var r = new UpdateCheckResult { CurrentVersion = currentVersion, HtmlUrl = AppInfo.ReleasesUrl };
            if (string.IsNullOrEmpty(json))
            {
                r.Ok = false;
                r.ErrorMessage = "Пустой ответ от сервера GitHub.";
                return r;
            }

            string tag = ExtractJsonString(json, "tag_name");
            string name = ExtractJsonString(json, "name");
            string url = ExtractJsonString(json, "html_url");

            if (string.IsNullOrEmpty(tag))
            {
                r.Ok = false;
                r.ErrorMessage = "Не удалось разобрать сведения о релизе.";
                return r;
            }

            r.Ok = true;
            r.LatestTag = tag;
            r.LatestName = string.IsNullOrEmpty(name) ? tag : name;
            if (!string.IsNullOrEmpty(url) && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                r.HtmlUrl = url;

            r.HasNewerVersion = CompareVersions(tag, currentVersion) > 0;
            return r;
        }

        private static string ExtractJsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success)
                return null;
            return m.Groups[1].Value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        public static int CompareVersions(string a, string b)
        {
            bool aPre, bPre;
            int[] va = ParseParts(a, out aPre);
            int[] vb = ParseParts(b, out bPre);
            int len = Math.Max(va.Length, vb.Length);
            for (int i = 0; i < len; i++)
            {
                int xa = i < va.Length ? va[i] : 0;
                int xb = i < vb.Length ? vb[i] : 0;
                if (xa != xb)
                    return xa.CompareTo(xb);
            }
            if (aPre != bPre)
                return aPre ? -1 : 1;
            return 0;
        }

        private static int[] ParseParts(string v, out bool isPrerelease)
        {
            isPrerelease = false;
            if (string.IsNullOrEmpty(v))
                return new[] { 0 };
            v = v.Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash >= 0)
            {
                isPrerelease = true;
                v = v.Substring(0, dash);
            }
            string[] split = v.Split('.');
            var nums = new int[split.Length];
            for (int i = 0; i < split.Length; i++)
            {
                int n;
                nums[i] = int.TryParse(split[i], out n) ? n : 0;
            }
            return nums;
        }
    }
}
