using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MinimalQuestLauncher
{
    internal sealed class LibraryEntry
    {
        public string Id;
        public string Name;
        public string Path;
        public string Version;

        public string Sha256;
        public string BuildHash;
        public long Size;
        public DateTime CreatedUtc;
        public DateTime LastLaunchUtc;
        public int LaunchCount;
        public bool Favorite;

        public string Origin;

        public LibraryEntry Clone()
        {
            return (LibraryEntry)MemberwiseClone();
        }
    }

    internal sealed class Library
    {
        public const string Header = "# QuestLauncher library v1";
        private const int MaxFileSize = 4 * 1024 * 1024;
        private readonly string _path;
        private readonly string _mutexName;

        public string LastWarning { get; private set; }

        public Library(string path)
        {
            _path = path;
            string key;
            using (var sha = SHA256.Create()) key =
                CopyIdentity
                    .ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(PathResolver.Normalize(path).ToUpperInvariant())))
                    .Substring(0, 16);
            _mutexName = "Local\\QuestLauncher.Library." + key;
        }

        public string FilePath
        {
            get {
                return _path;
            }
        }

        private static string Esc(string s)
        {
            if (s == null)
                return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string Unesc(string s)
        {
            if (s.IndexOf('\\') < 0)
                return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 't')
                        sb.Append('\t');
                    else if (n == 'n')
                        sb.Append('\n');
                    else if (n == 'r')
                        sb.Append('\r');
                    else
                        sb.Append(n);
                }
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string FmtDate(DateTime d)
        {
            return d == DateTime.MinValue ? string.Empty
                                          : d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseDate(string s)
        {
            DateTime d;
            if (string.IsNullOrEmpty(s))
                return DateTime.MinValue;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)
                       ? d.ToUniversalTime()
                       : DateTime.MinValue;
        }

        public static string Serialize(IEnumerable<LibraryEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            sb.Append(
                "# id\tname\tpath\tversion\tsha256\tbuildhash\tsize\tcreated\tlastlaunch\tlaunches\tfavorite\torigin\r\n");
            foreach (LibraryEntry e in entries)
            {
                sb.Append(Esc(e.Id))
                    .Append('\t')
                    .Append(Esc(e.Name))
                    .Append('\t')
                    .Append(Esc(e.Path))
                    .Append('\t')
                    .Append(Esc(e.Version))
                    .Append('\t')
                    .Append(Esc(e.Sha256))
                    .Append('\t')
                    .Append(Esc(e.BuildHash))
                    .Append('\t')
                    .Append(e.Size.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(FmtDate(e.CreatedUtc))
                    .Append('\t')
                    .Append(FmtDate(e.LastLaunchUtc))
                    .Append('\t')
                    .Append(e.LaunchCount.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(e.Favorite ? "1" : "0")
                    .Append('\t')
                    .Append(Esc(e.Origin))
                    .Append("\r\n");
            }
            return sb.ToString();
        }

        public static List<LibraryEntry> Parse(string text, out int badLines, out bool badHeader)
        {
            var list = new List<LibraryEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            badLines = 0;
            badHeader = false;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || !lines[0].Trim().Equals(Header, StringComparison.Ordinal))
            {
                badHeader = true;
                return list;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0 || line.StartsWith("#"))
                    continue;
                string[] f = line.Split('\t');
                if (f.Length < 12)
                {
                    badLines++;
                    continue;
                }
                try
                {
                    var e = new LibraryEntry { Id = Unesc(f[0]),
                                               Name = Unesc(f[1]),
                                               Path = Unesc(f[2]),
                                               Version = Unesc(f[3]),
                                               Sha256 = Unesc(f[4]),
                                               BuildHash = Unesc(f[5]),
                                               Size = long.Parse(f[6], CultureInfo.InvariantCulture),
                                               CreatedUtc = ParseDate(f[7]),
                                               LastLaunchUtc = ParseDate(f[8]),
                                               LaunchCount = int.Parse(f[9], CultureInfo.InvariantCulture),
                                               Favorite = f[10] == "1",
                                               Origin = Unesc(f[11]) };
                    if (string.IsNullOrEmpty(e.Id) || string.IsNullOrEmpty(e.Path) ||
                        !PathResolver.IsFullyQualified(e.Path) || string.IsNullOrEmpty(e.Sha256) ||
                        e.Sha256.Length != 64)
                    {
                        badLines++;
                        continue;
                    }
                    string norm = PathResolver.Normalize(e.Path);
                    if (!seen.Add(norm))
                    {
                        badLines++;
                        continue;
                    }
                    if (string.IsNullOrEmpty(e.Name))
                        e.Name = System.IO.Path.GetFileName(e.Path);
                    list.Add(e);
                }
                catch
                {
                    badLines++;
                }
            }
            return list;
        }

        private List<LibraryEntry> LoadUnlocked()
        {
            LastWarning = null;
            foreach (string candidate in new[] { _path, _path + ".bak" })
            {
                string text;
                try
                {
                    text = AtomicFile.ReadAllText(candidate, MaxFileSize);
                }
                catch (Exception ex)
                {
                    Log.Error("Чтение библиотеки " + candidate, ex);
                    if (candidate == _path)
                        QuarantineOnce(candidate);
                    continue;
                }
                if (text == null)
                {
                    if (candidate == _path && !File.Exists(_path + ".bak"))
                        return new List<LibraryEntry>();
                    continue;
                }
                int bad;
                bool badHeader;
                List<LibraryEntry> list = Parse(text, out bad, out badHeader);
                if (badHeader)
                {
                    Log.Warn("Неверный заголовок файла библиотеки: " + candidate);
                    if (candidate == _path)
                        QuarantineOnce(candidate);
                    continue;
                }
                if (bad > 0)
                {
                    Log.Warn("Библиотека: пропущено некорректных записей: " + bad + " (" + candidate + ")");
                    if (candidate == _path)
                        QuarantineOnce(candidate);
                    LastWarning =
                        "Некоторые записи библиотеки повреждены и пропущены (" + bad + "). Копия файла сохранена.";
                }
                if (candidate != _path)
                    LastWarning = "Файл библиотеки повреждён, загружена резервная копия.";
                return list;
            }
            if (File.Exists(_path))
                LastWarning = "Файл библиотеки повреждён. Начат новый список; копия старого файла сохранена.";
            return new List<LibraryEntry>();
        }

        private bool _quarantined;

        private void QuarantineOnce(string path)
        {
            if (_quarantined)
                return;
            _quarantined = true;
            AtomicFile.Quarantine(path);
        }

        private Mutex AcquireMutex()
        {
            var m = new Mutex(false, _mutexName);
            try
            {
                if (!m.WaitOne(5000))
                {
                    m.Dispose();
                    throw new TimeoutException("Библиотека занята другим окном лаунчера.");
                }
            }
            catch (AbandonedMutexException)
            {
            }
            return m;
        }

        public List<LibraryEntry> Load()
        {
            using (Mutex m = AcquireMutex())
            {
                try
                {
                    return LoadUnlocked();
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }
        }

        public List<LibraryEntry> Update(Action<List<LibraryEntry>> change)
        {
            using (Mutex m = AcquireMutex())
            {
                try
                {
                    List<LibraryEntry> list = LoadUnlocked();
                    change(list);
                    string dir = System.IO.Path.GetDirectoryName(_path);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    AtomicFile.WriteAllText(_path, Serialize(list));
                    return list;
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }
        }

        public static LibraryEntry FindByPath(List<LibraryEntry> list, string path)
        {
            string n = PathResolver.Normalize(path);
            foreach (LibraryEntry e in list)
                if (string.Equals(PathResolver.Normalize(e.Path), n, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        public static LibraryEntry FindById(List<LibraryEntry> list, string id)
        {
            foreach (LibraryEntry e in list)
                if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }
    }
}
