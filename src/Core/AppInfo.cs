using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{

    internal static class AppInfo
    {
        public const string ProductName = "Quest Launcher";
        public const string RepoOwner = "NekoPoint";
        public const string RepoName = "QuestLauncher";
        public const string ReleasesUrl = "https://github.com/NekoPoint/QuestLauncher/releases";
        public const string LatestReleaseApi = "https://api.github.com/repos/NekoPoint/QuestLauncher/releases/latest";

        public static readonly string EmbeddedMarker = "QLMK1:L:00000000000000000000000000000000";

        public static readonly string EmbeddedBuildSignature =
            "QLSG1:" + "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000";

        private static string _version;
        private static string _exePath;
        private static string _appDir;

        public static string Version
        {
            get {
                if (_version == null)
                {
                    string v = null;
                    try
                    {
                        var attrs = typeof(AppInfo).Assembly.GetCustomAttributes(
                            typeof(AssemblyInformationalVersionAttribute), false);
                        if (attrs.Length > 0)
                            v = ((AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                    }
                    catch
                    {
                    }
                    if (string.IsNullOrEmpty(v))
                    {
                        Version av = typeof(AppInfo).Assembly.GetName().Version;
                        v = av.Major + "." + av.Minor + "." + av.Build;
                    }
                    _version = v;
                }
                return _version;
            }
        }

        public static string ExecutablePath
        {
            get {
                if (_exePath == null)
                {
                    string p;
                    try
                    {
                        p = Path.GetFullPath(Application.ExecutablePath);
                    }
                    catch
                    {
                        p = typeof(AppInfo).Assembly.Location;
                    }
                    _exePath = p;
                }
                return _exePath;
            }
        }

        public static string AppDirectory
        {
            get {
                if (_appDir == null)
                {
                    string dir = null;
                    try
                    {
                        dir = Path.GetDirectoryName(ExecutablePath);
                    }
                    catch
                    {
                    }
                    if (string.IsNullOrEmpty(dir))
                        dir = AppDomain.CurrentDomain.BaseDirectory;
                    _appDir = dir.TrimEnd('\\', '/');
                    if (_appDir.Length == 2 && _appDir[1] == ':')
                        _appDir += "\\";
                }
                return _appDir;
            }
        }
    }
}
