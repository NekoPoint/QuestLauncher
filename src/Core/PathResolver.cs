using System;
using System.IO;

namespace MinimalQuestLauncher
{
    internal sealed class ResolvedTarget
    {
        public bool Ok;
        public string Error;

        public string FullPath;
        public string Directory;
        public string FileName;

        public string Note;

        public static ResolvedTarget Fail(string error)
        {
            return new ResolvedTarget { Ok = false, Error = error };
        }
    }

    internal static class PathResolver
    {
        public const string DefaultFolder = "games";
        public const int MaxPath = 259;

        private static readonly string[] ReservedNames = { "CON",  "PRN",  "AUX",  "NUL",  "CLOCK$", "COM1",
                                                           "COM2", "COM3", "COM4", "COM5", "COM6",   "COM7",
                                                           "COM8", "COM9", "LPT1", "LPT2", "LPT3",   "LPT4",
                                                           "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        public static string CleanInput(string s)
        {
            if (s == null)
                return string.Empty;
            s = s.Trim();
            while (s.Length >= 2)
            {
                char f = s[0], l = s[s.Length - 1];
                if ((f == '"' && l == '"') || (f == '\'' && l == '\''))
                    s = s.Substring(1, s.Length - 2).Trim();
                else
                    break;
            }
            return s.Replace('/', '\\');
        }

        public static bool IsDevicePath(string p)
        {
            return p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                   p.StartsWith(@"\??\", StringComparison.Ordinal);
        }

        public static bool IsFullyQualified(string p)
        {
            if (string.IsNullOrEmpty(p))
                return false;
            if (IsDevicePath(p))
                return false;
            if (p.Length >= 3 && IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\')
                return true;
            if (p.StartsWith(@"\\", StringComparison.Ordinal) && p.Length > 2 && p[2] != '\\')
                return true;
            return false;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        public static string ValidateSegment(string seg, bool isFileName)
        {
            if (seg.Length == 0)
                return "Пустое имя в пути.";
            if (!isFileName && (seg == "." || seg == ".."))
                return null;
            if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "Недопустимые символы в «" + seg + "» (\\ / : * ? \" < > |).";
            if (seg.EndsWith(".") || seg.EndsWith(" "))
                return "Имя «" + seg + "» не может заканчиваться точкой или пробелом.";
            if (seg.StartsWith(" "))
                return "Имя «" + seg + "» не может начинаться с пробела.";
            string stem = seg;
            int dot = stem.IndexOf('.');
            if (dot >= 0)
                stem = stem.Substring(0, dot);
            stem = stem.TrimEnd(' ');
            foreach (string r in ReservedNames)
            {
                if (string.Equals(stem, r, StringComparison.OrdinalIgnoreCase))
                    return "«" + seg + "» — зарезервированное имя Windows.";
            }
            if (seg.Length > 255)
                return "Слишком длинное имя «" + seg.Substring(0, 20) + "…».";
            return null;
        }

        private static string ValidateRelative(string rel)
        {
            foreach (string seg in rel.Split('\\'))
            {
                if (seg.Length == 0)
                    continue;
                string err = ValidateSegment(seg, false);
                if (err != null)
                    return err;
            }
            return null;
        }

        private static bool IsWithinRoot(string fullPath)
        {
            try
            {
                string root = Path.GetPathRoot(fullPath);
                return !string.IsNullOrEmpty(root) && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                       fullPath.Length > root.Length;
            }
            catch
            {
                return false;
            }
        }

        private static string ValidateFull(string full)
        {
            string rest;
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string[] parts = full.Substring(2).Split('\\');
                if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
                    return "Сетевой путь должен иметь вид \\\\сервер\\ресурс\\папка.";
                rest = string.Join("\\", parts, 2, parts.Length - 2);
                if (parts[0].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    parts[1].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return "Недопустимые символы в сетевом пути.";
            }
            else
            {
                rest = full.Substring(3);
            }
            return ValidateRelative(rest);
        }

        public static ResolvedTarget Resolve(string nameInput, string folderInput, string baseDir)
        {
            string name = CleanInput(nameInput);
            if (name.Length == 0)
                return ResolvedTarget.Fail("Введите имя исполняемого файла игры.");
            if (IsDevicePath(name))
                return ResolvedTarget.Fail("Пути вида \\\\?\\ и \\\\.\\ не поддерживаются.");

            string dir;
            string fileName;
            string note = null;

            bool nameHasPath = name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0;
            if (nameHasPath)
            {
                if (!IsFullyQualified(name))
                {
                    return ResolvedTarget.Fail(
                        "В поле имени укажите только имя файла (например, Endfield.exe) или " +
                        "полный путь (C:\\Games\\Endfield.exe). Подкаталог укажите в поле «Папка».");
                }
                string err = ValidateFull(name);
                if (err != null)
                    return ResolvedTarget.Fail(err);
                int idx = name.LastIndexOf('\\');
                dir = name.Substring(0, idx + 1);
                fileName = name.Substring(idx + 1);
                note = "В поле имени указан полный путь — поле «Папка» не используется.";
            }
            else
            {
                fileName = name;
                string folder = CleanInput(folderInput);
                if (folder.Length == 0)
                    folder = DefaultFolder;
                if (IsDevicePath(folder))
                    return ResolvedTarget.Fail("Пути вида \\\\?\\ и \\\\.\\ не поддерживаются.");

                if (IsFullyQualified(folder))
                {
                    string err = ValidateFull(folder);
                    if (err != null)
                        return ResolvedTarget.Fail(err);
                    dir = folder;
                }
                else if (folder.StartsWith("\\", StringComparison.Ordinal))
                {
                    return ResolvedTarget.Fail(
                        "Путь «" + folder +
                        "» зависит от текущего диска. Укажите полный путь (C:\\…) или путь относительно лаунчера.");
                }
                else if (folder.IndexOf(':') >= 0)
                {
                    return ResolvedTarget.Fail("Некорректная папка «" + folder +
                                               "». Используйте полный путь вида C:\\Папка.");
                }
                else
                {
                    string err = ValidateRelative(folder);
                    if (err != null)
                        return ResolvedTarget.Fail(err);
                    if (string.IsNullOrEmpty(baseDir))
                        return ResolvedTarget.Fail("Не удалось определить каталог лаунчера.");
                    dir = Path.Combine(baseDir, folder);
                }
            }

            if (fileName.Length == 0)
                return ResolvedTarget.Fail("Не указано имя файла.");
            if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName += ".exe";
            if (fileName.Length <= 4 || fileName.Substring(0, fileName.Length - 4).Trim().Length == 0)
                return ResolvedTarget.Fail("Имя файла не может состоять только из расширения.");
            string ferr = ValidateSegment(fileName, true);
            if (ferr != null)
                return ResolvedTarget.Fail(ferr);

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(dir, fileName));
            }
            catch (PathTooLongException)
            {
                return ResolvedTarget.Fail("Слишком длинный путь (ограничение Windows — 260 символов).");
            }
            catch (Exception ex)
            {
                return ResolvedTarget.Fail("Некорректный путь: " + ex.Message);
            }

            if (full.Length > MaxPath)
                return ResolvedTarget.Fail("Слишком длинный путь (" + full.Length + " символов, максимум " + MaxPath +
                                           ").");

            string finalName = Path.GetFileName(full);
            if (!string.Equals(finalName, fileName, StringComparison.Ordinal))
                return ResolvedTarget.Fail("Некорректное имя файла.");

            string finalDir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(finalDir) || !IsWithinRoot(full))
                return ResolvedTarget.Fail(
                    "Нельзя создать файл вне корневого каталога или в корне сетевого ресурса без папки.");

            return new ResolvedTarget { Ok = true, FullPath = full, Directory = finalDir, FileName = finalName,
                                        Note = note };
        }

        public static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return path.TrimEnd('\\');
            }
        }

        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null)
                return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string ToFolderInput(string fullDir, string baseDir)
        {
            string d = Normalize(fullDir);
            string b = Normalize(baseDir);
            if (string.Equals(d, b, StringComparison.OrdinalIgnoreCase))
                return ".";
            if (d.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase))
                return d.Substring(b.Length + 1);
            return d;
        }
    }
}
