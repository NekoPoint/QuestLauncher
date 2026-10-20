using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MinimalQuestLauncher
{

    internal static class ProcessTools
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName,
                                                             ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        public static string GetImagePath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
                return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        private static string Key(string path)
        {
            return PathResolver.Normalize(path).ToUpperInvariant();
        }

        public static Dictionary<string, List<int>> FindRunning(IEnumerable<string> paths)
        {
            var result = new Dictionary<string, List<int>>();
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in paths)
            {
                string name;
                try
                {
                    name = Path.GetFileNameWithoutExtension(p);
                }
                catch
                {
                    continue;
                }
                if (string.IsNullOrEmpty(name))
                    continue;
                List<string> l;
                if (!byName.TryGetValue(name, out l))
                    byName[name] = l = new List<string>();
                l.Add(Key(p));
            }
            if (byName.Count == 0)
                return result;

            Process[] all;
            try
            {
                all = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                Log.Error("Получение списка процессов", ex);
                return result;
            }

            int selfPid;
            using (Process self = Process.GetCurrentProcess()) selfPid = self.Id;
            foreach (Process pr in all)
            {
                try
                {
                    List<string> candidates;
                    if (pr.Id == selfPid || !byName.TryGetValue(pr.ProcessName, out candidates))
                        continue;
                    string image = GetImagePath(pr.Id);
                    if (image == null)
                        continue;
                    string k = Key(image);
                    if (!candidates.Contains(k))
                        continue;
                    List<int> pids;
                    if (!result.TryGetValue(k, out pids))
                        result[k] = pids = new List<int>();
                    pids.Add(pr.Id);
                }
                catch
                {
                }
                finally
                {
                    pr.Dispose();
                }
            }
            return result;
        }

        public static List<int> FindRunning(string path)
        {
            Dictionary<string, List<int>> d = FindRunning(new[] { path });
            List<int> pids;
            return d.TryGetValue(Key(path), out pids) ? pids : new List<int>();
        }

        public static int CountFor(Dictionary<string, List<int>> map, string path)
        {
            List<int> pids;
            return map != null && map.TryGetValue(Key(path), out pids) ? pids.Count : 0;
        }

        public static int StartCopy(string path)
        {
            var psi = new ProcessStartInfo { FileName = path, Arguments = "--dummy",
                                             WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = false };
            using (Process p = Process.Start(psi))
            {
                return p == null ? 0 : p.Id;
            }
        }

        public static void OpenFolder(string filePath)
        {
            string explorer =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (File.Exists(filePath))
            {
                using (Process.Start(
                    new ProcessStartInfo(explorer, "/select,\"" + filePath + "\"") { UseShellExecute = false }))
                {
                }
                return;
            }
            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Папка не найдена: " + dir);
            using (Process.Start(new ProcessStartInfo(explorer, "\"" + dir + "\"") { UseShellExecute = false }))
            {
            }
        }

        public static void OpenUrl(string url)
        {
            if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Недопустимый адрес: " + url);
            using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
            {
            }
        }
    }
}
