using System;
using System.IO;
using System.Text;

namespace MinimalQuestLauncher
{

    internal static class DataPaths
    {
        private static string _dataDir;

        public static string OverrideDir;

        public static string DataDirectory
        {
            get {
                if (OverrideDir != null)
                    return OverrideDir;
                if (_dataDir == null)
                {
                    string app = AppInfo.AppDirectory;
                    if (IsWritable(app))
                        _dataDir = app;
                    else
                    {
                        string local = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuestLauncher");
                        try
                        {
                            Directory.CreateDirectory(local);
                        }
                        catch
                        {
                        }
                        _dataDir = local;
                    }
                }
                return _dataDir;
            }
        }

        public static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".ql-write-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                                      FileOptions.DeleteOnClose))
                {
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ConfigPath
        {
            get {
                return Path.Combine(DataDirectory, "launcher.cfg");
            }
        }

        public static string LegacyConfigPath
        {
            get {
                return Path.Combine(AppInfo.AppDirectory, "launcher.cfg");
            }
        }
        public static string LibraryPath
        {
            get {
                return Path.Combine(DataDirectory, "QuestLauncher.library");
            }
        }
        public static string LogPath
        {
            get {
                return Path.Combine(DataDirectory, "QuestLauncher.log");
            }
        }
    }

    internal static class AtomicFile
    {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string text)
        {
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            byte[] data = Utf8NoBom.GetBytes(text);
            if (File.Exists(tmp))
                File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, bak, true);
                    }
                    catch (IOException)
                    {

                        File.Copy(path, bak, true);
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(path, bak, true);
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp);
                }
                catch
                {
                }
            }
        }

        public static string ReadAllText(string path, int maxBytes)
        {
            if (!File.Exists(path))
                return null;
            using (var fs =
                       new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length > maxBytes)
                    throw new InvalidDataException("Файл слишком большой (" + fs.Length + " байт): " + path);
                var buf = new byte[fs.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0)
                        break;
                    read += n;
                }

                if (read >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
                    return Encoding.Unicode.GetString(buf, 2, read - 2);
                if (read >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
                    return Encoding.UTF8.GetString(buf, 3, read - 3);
                for (int i = 0; i < read; i++)
                {
                    if (buf[i] == 0)
                        throw new InvalidDataException("Файл содержит двоичные данные: " + path);
                }
                return Encoding.UTF8.GetString(buf, 0, read);
            }
        }

        public static string Quarantine(string path)
        {
            try
            {
                string dest = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, dest, false);
                return dest;
            }
            catch (Exception ex)
            {
                Log.Warn("Не удалось сохранить копию повреждённого файла " + path + ": " + ex.Message);
                return null;
            }
        }
    }

    internal static class Log
    {
        public const long MaxSize = 256 * 1024;
        private static readonly object Sync = new object();
        public static string OverridePath;
        private static int _pid;

        private static string PathValue
        {
            get {
                return OverridePath ?? DataPaths.LogPath;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }
        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string context, Exception ex)
        {
            Write("ERROR",
                  context + ": " +
                      (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace));
        }

        public static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    string p = PathValue;
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > MaxSize)
                    {
                        string old = p + ".1";
                        if (File.Exists(old))
                            File.Delete(old);
                        File.Move(p, old);
                    }
                    if (_pid == 0)
                    {
                        using (var self = System.Diagnostics.Process.GetCurrentProcess()) _pid = self.Id;
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] v" +
                                  AppInfo.Version + " pid " + _pid + ": " + message + Environment.NewLine;
                    if (line.Length > 16 * 1024)
                        line = line.Substring(0, 16 * 1024) + Environment.NewLine;
                    File.AppendAllText(p, line, AtomicFile.Utf8NoBom);
                }
            }
            catch
            {

            }
        }
    }
}
