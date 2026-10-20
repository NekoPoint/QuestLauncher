using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinimalQuestLauncher
{

    internal sealed class Settings
    {
        public const int MaxFileSize = 64 * 1024;

        public string Game = string.Empty;
        public string Folder = PathResolver.DefaultFolder;
        public string Filter = "all";

        public readonly List<KeyValuePair<string, string>> Extra = new List<KeyValuePair<string, string>>();

        public string LoadWarning;

        public static Settings Parse(string text, out int badLines)
        {
            var s = new Settings();
            badLines = 0;
            if (text == null)
                return s;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                int idx = line.IndexOf('=');
                if (idx <= 0)
                {
                    badLines++;
                    continue;
                }
                string key = line.Substring(0, idx).Trim();
                string val = line.Substring(idx + 1).Trim().Trim('"', '\'');
                bool control = false;
                foreach (char c in val)
                    if (char.IsControl(c))
                    {
                        control = true;
                        break;
                    }
                if (control || val.Length > 1024)
                {
                    badLines++;
                    continue;
                }

                if (string.Equals(key, "game", StringComparison.OrdinalIgnoreCase))
                    s.Game = val;
                else if (string.Equals(key, "folder", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.Length > 0)
                        s.Folder = val;
                }
                else if (string.Equals(key, "filter", StringComparison.OrdinalIgnoreCase))
                    s.Filter = val;
                else if (key.Length <= 64)
                    s.Extra.Add(new KeyValuePair<string, string>(key, val));
                else
                    badLines++;
            }
            return s;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("# Quest Launcher Configuration\r\n");
            sb.Append("game=").Append(OneLine(Game)).Append("\r\n");
            sb.Append("folder=")
                .Append(OneLine(string.IsNullOrEmpty(Folder) ? PathResolver.DefaultFolder : Folder))
                .Append("\r\n");
            sb.Append("filter=").Append(OneLine(Filter)).Append("\r\n");
            foreach (var kv in Extra)
                sb.Append(kv.Key).Append('=').Append(OneLine(kv.Value)).Append("\r\n");
            return sb.ToString();
        }

        private static string OneLine(string s)
        {
            if (s == null)
                return string.Empty;
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static Settings Load(string path, string fallbackPath)
        {
            string source = path;
            if (!File.Exists(path) && fallbackPath != null && File.Exists(fallbackPath))
                source = fallbackPath;

            bool quarantined = false;
            foreach (string candidate in new[] { source, source + ".bak" })
            {
                try
                {
                    string text = AtomicFile.ReadAllText(candidate, MaxFileSize);
                    if (text == null)
                    {
                        if (candidate == source && !File.Exists(source + ".bak"))
                            return new Settings();
                        continue;
                    }
                    int bad;
                    Settings s = Parse(text, out bad);
                    if (bad > 0)
                    {
                        Log.Warn("launcher.cfg: пропущено некорректных строк: " + bad);
                        if (candidate == source && !quarantined)
                        {
                            quarantined = true;
                            AtomicFile.Quarantine(candidate);
                        }
                        if (s.Game.Length == 0 && s.Extra.Count == 0 && text.Trim().Length > 0 &&
                            File.Exists(source + ".bak") && candidate == source)
                        {
                            continue;
                        }
                        s.LoadWarning = "В файле настроек пропущено некорректных строк: " + bad + ".";
                    }
                    if (candidate != source)
                    {
                        s.LoadWarning = "Файл настроек был повреждён, восстановлены настройки из резервной копии.";
                    }
                    return s;
                }
                catch (Exception ex)
                {
                    Log.Error("Чтение настроек " + candidate, ex);
                    if (candidate == source && !quarantined)
                    {
                        quarantined = true;
                        AtomicFile.Quarantine(candidate);
                    }
                }
            }
            var def = new Settings();
            if (File.Exists(source) || File.Exists(source + ".bak"))
                def.LoadWarning =
                    "Файл настроек повреждён — используются значения по умолчанию. Копия сохранена рядом с " +
                    "расширением .corrupt-….";
            return def;
        }

        public bool Save(string path)
        {
            try
            {
                AtomicFile.WriteAllText(path, Serialize());
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Сохранение настроек " + path, ex);
                return false;
            }
        }
    }
}
