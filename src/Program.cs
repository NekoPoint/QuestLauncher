using System;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            WinApi.EnableHighDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (s, e) =>
            {
                Log.Error("Необработанное исключение UI-потока", e.Exception);
                MessageBox.Show("Произошла непредвиденная ошибка:\n" + e.Exception.Message +
                                    "\n\nПодробности записаны в QuestLauncher.log.",
                                "Quest Launcher — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            { Log.Error("Необработанное исключение домена", e.ExceptionObject as Exception); };

            bool isDummyFlag = false;
            bool isForceLauncher = false;
            bool isSelfCheck = false;

            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "--dummy", StringComparison.OrdinalIgnoreCase))
                        isDummyFlag = true;
                    else if (string.Equals(a, "--launcher", StringComparison.OrdinalIgnoreCase))
                        isForceLauncher = true;
                    else if (string.Equals(a, "--self-check", StringComparison.OrdinalIgnoreCase))
                        isSelfCheck = true;
                }
            }

            if (isSelfCheck)
            {
                Console.Out.WriteLine("ROLE=" + CopyIdentity.CurrentRole + ";VERSION=" + AppInfo.Version +
                                      ";MARKER=" + AppInfo.EmbeddedMarker);
                return 0;
            }

            bool runAsDummy = !isForceLauncher && (isDummyFlag || CopyIdentity.CurrentRole == 'D');

            if (runAsDummy)
            {
                Application.Run(new DummyForm());
            }
            else
            {
                Application.Run(new LauncherForm());
            }
            return 0;
        }
    }
}
