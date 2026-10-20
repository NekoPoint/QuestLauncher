using System;
using System.Drawing;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;
using MinimalQuestLauncher;

namespace QuestLauncher.Tests
{
    internal static class TestRunner
    {
        private static int _passed;
        private static int _failed;
        private static string _tempRoot;
        private static string _builtExe;

        private const int PortableLegacySize = 31744;
        private const string PortableLegacyExpectedSha256 =
            "E1225B642DC861896FB0E318F3CFC7586128729956105799D21E937E73132585";

        private const string RealReleaseV12Resource = "QuestLauncher_v1.2_release.exe.gz";
        private const int RealReleaseV12Size = 18432;
        private const string RealReleaseV12Sha256 = "92E1978961F9D905DABDC6ACA921C63C51ED86F4E0AE0933053D78872A1DAAC6";

        private const string RealReleaseV13Resource = "QuestLauncher_v1.3_release.exe.gz";
        private const int RealReleaseV13Size = 31744;
        private const string RealReleaseV13Sha256 = "7DC0C100E16DABBC73871D6495DE2A90ABD0F798CAE04D2D291621E9D09764C1";

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args != null && args.Length >= 2 && args[0] == "--sign-release")
            {
                string exeToSign = Path.GetFullPath(args[1]);
                string keyXml = Environment.GetEnvironmentVariable("QL_RELEASE_SIGNING_KEY_XML");
                if (string.IsNullOrEmpty(keyXml))
                    throw new InvalidOperationException("QL_RELEASE_SIGNING_KEY_XML secret is not set.");

                byte[] raw = File.ReadAllBytes(exeToSign);
                byte[] signed = CopyIdentity.SignBuildBytes(raw, keyXml);
                File.WriteAllBytes(exeToSign, signed);
                return 0;
            }

            if (args != null && args.Length >= 5 && args[0] == "--crash-mid-replace")
            {
                return RunCrashMidReplaceChild(args[1], args[2], args[3], args[4]);
            }

            _builtExe =
                (args != null && args.Length > 0 && File.Exists(args[0]))
                    ? Path.GetFullPath(args[0])
                    : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuestLauncher.exe"));

            _tempRoot =
                Path.Combine(Path.GetTempPath(), "QuestLauncherTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempRoot);
            DataPaths.OverrideDir = Path.Combine(_tempRoot, "appdata");
            Directory.CreateDirectory(DataPaths.OverrideDir);
            Log.OverridePath = Path.Combine(DataPaths.OverrideDir, "test.log");

            Console.WriteLine("=== Автоматические проверки QuestLauncher v" + AppInfo.Version + " ===");
            Console.WriteLine("Временный каталог: " + _tempRoot);
            Console.WriteLine("Проверяемая сборка: " + _builtExe);

            try
            {
                Run("1. Разбор путей (относительные, абсолютные, UNC, кириллица, пробелы, кавычки, ошибки)",
                    TestPathResolution);
                Run("2. Маркер роли и автономное определение роли без подстроки 'Launcher'",
                    TestMarkerAndRoleIndependence);
                Run("3. Безопасность файлов: запрет перезаписи чужого EXE и защита самого лаунчера",
                    TestForeignExeAndSelfProtection);
                Run("4. Модель доверия маркера и подписи: чужой EXE с маркером QLMK1 отклоняется как Foreign",
                    TestForeignExeWithValidMarkerRejected);
                Run("5. Разделение LegacyLikely и подтверждённой копии: запрет обновления и удаления с диска",
                    TestLegacyLikelyCannotUpdateOrDeleteDiskFile);
                Run("6. Реестр записей: подменённый файл в реестре блокируется при обновлении и удалении",
                    TestTamperedRegistryEntryBlocked);
                Run("7. Разделение тестовых и релизных ключей + обновление между сборками 1.4+ (v1.4.0 -> v1.4.1)",
                    TestUpgradeBetweenTwoDistinct14PlusBuilds);
                Run("8. Эксклюзивная блокировка дескриптора (FileShare.None / защита от гонок и FileShare.Delete)",
                    TestExclusiveHandleAndRaceProtection);
                Run("9. Отрицательный тест RecoverIfInterrupted: чужой EXE рядом с подлинной .qlbak НЕ перезаписывается",
                    TestRecoverIfInterruptedNeverOverwritesForeignTarget);
                Run("10. Аварийное прерывание процесса (Process.Kill), явное восстановление из .qlbak и согласованность при сбоях до/после сохранения библиотеки",
                    TestCrashRecoveryAndStateConsistency);
                Run("11. Заблокированный (запущенный) файл и отсутствие прав записи (ReadOnly / ACL)",
                    TestLockedFileAndAccessDenied);
                Run("12. Устойчивость настроек (launcher.cfg) и реестра к повреждению и прерыванию записи",
                    TestCorruptConfigAndLibraryRecovery);
                Run("13. Настоящие исторические релизы v1.2 и v1.3 (зафиксированный SHA-256): импорт и обновление до 1.4.0",
                    TestRealLegacyReleaseImportAndUpdate);
                Run("14. Синтетические тесты сканирования и импорта (LegacyKnown, LegacyLikely, OwnCopy 1.4+)",
                    TestSyntheticLegacyAndModernImport);
                Run("15. Синтетический тест безопасного обновления устаревшей копии до 1.4.0",
                    TestSyntheticSafeUpdateOutdatedCopy);
                Run("16. Отслеживание запущенных экземпляров по полному пути (а не имени)",
                    TestProcessTrackingByFullPath);
                Run("17. Ограничение размера и ротация журнала ошибок", TestLogRotation);
                Run("18. Разбор версий и ответа GitHub Releases API", TestUpdateCheckerParsing);
                Run("19. Проверка UI-форм и масштабирования DPI (100%, 150%, 200%)", TestUiFormsAndDpiScaling);
                Run("20. Измерение памяти и отсутствия утечек GDI/дескрипторов", TestMemoryAndHandleMeasurement);
                Run("21. Диалог импорта: начальные отметки, переключение и массовый выбор", TestImportDialogSelection);
                Run("22. Массовое обновление: актуальные, старые, занятые, подменённые и отсутствующие файлы",
                    TestBulkUpdateCopies);
            }
            finally
            {
                try
                {
                    CleanupTempDir(_tempRoot);
                }
                catch
                {
                }
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("ИТОГО: пройдено " + _passed + ", ошибок " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static int RunCrashMidReplaceChild(string targetPath, string expectedSha, string builtExe,
                                                   string signalFile)
        {
            byte[] launcherRaw = File.ReadAllBytes(builtExe);
            string launcherBuildHash =
                CopyIdentity.ComputeBuildHash(launcherRaw, CopyIdentity.FindMarkers(launcherRaw));
            byte[] newBytes = CopyIdentity.BuildCopyBytes(launcherRaw, CopyIdentity.NewCopyId());
            FileOps.MidWriteTestHook = (fs, target) =>
            {
                File.WriteAllText(signalFile, "READY", Encoding.UTF8);
                Thread.Sleep(15000);
            };
            FileOps.ReplaceOwned(targetPath, expectedSha, newBytes, launcherBuildHash, true);
            return 0;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Assertion failed: " + message);
        }

        private static byte[] LoadEmbeddedResourceBytes(string resourceName)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (s == null)
                    throw new InvalidOperationException("Embedded resource not found: " + resourceName);
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static byte[] LoadVerifiedRealReleaseFixture(string resourceName, int expectedSize,
                                                             string expectedSha256)
        {
            byte[] gzBytes = LoadEmbeddedResourceBytes(resourceName);
            byte[] rawBytes;
            using (var inMs = new MemoryStream(gzBytes)) using (
                var gz = new GZipStream(inMs, CompressionMode.Decompress)) using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                rawBytes = outMs.ToArray();
            }

            Assert(rawBytes.Length == expectedSize,
                   "Fixture " + resourceName + " size mismatch: expected " + expectedSize + ", got " + rawBytes.Length);
            string actualSha = CopyIdentity.Sha256(rawBytes);
            Assert(string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase),
                   "Fixture " + resourceName + " SHA-256 mismatch: expected " + expectedSha256 + ", got " + actualSha);
            return rawBytes;
        }

        private static byte[] CreateVerifiedPortableLegacyBytes()
        {
            var buf = new byte[PortableLegacySize];
            buf[0] = (byte)'M';
            buf[1] = (byte)'Z';
            byte[] headerTag = Encoding.ASCII.GetBytes("PORTABLE_LEGACY_QUESTLAUNCHER_TEST_FIXTURE_V1_3");
            Buffer.BlockCopy(headerTag, 0, buf, 64, headerTag.Length);
            uint state = 0x514C3133u;
            for (int i = 128; i < buf.Length; i++)
            {
                state = state * 1664525u + 1013904223u;
                buf[i] = (byte)(state >> 24);
            }
            string sha = CopyIdentity.Sha256(buf);
            Assert(string.Equals(sha, PortableLegacyExpectedSha256, StringComparison.OrdinalIgnoreCase),
                   "Portable legacy fixture SHA-256 mismatch: " + sha);
            return buf;
        }

        private static string CompileUnverifiedLegacyLikelyExe(string outputPath)
        {
            string code = @"
using System.Reflection;
[assembly: AssemblyTitle(""QuestLauncher"")]
[assembly: AssemblyVersion(""1.2.9.0"")]
namespace MinimalQuestLauncher {
    public class DummyForm {}
    public class LauncherForm {
        public static void Main() {}
    }
}";
            using (var provider = new CSharpCodeProvider())
            {
                var cp = new CompilerParameters { GenerateExecutable = true, OutputAssembly = outputPath,
                                                  CompilerOptions = "/target:exe /optimize+" };
                CompilerResults cr = provider.CompileAssemblyFromSource(cp, code);
                if (cr.Errors.HasErrors)
                    throw new InvalidOperationException("Failed to compile LegacyLikely test assembly: " +
                                                        cr.Errors[0].ErrorText);
            }
            return outputPath;
        }

        private static string CompileDistinct14PlusBuild(string outputPath, string version,
                                                         string ephemeralPrivateKeyXml)
        {
            string code = @"
using System;
using System.Reflection;

[assembly: AssemblyTitle(""QuestLauncher"")]
[assembly: AssemblyProduct(""Quest Launcher"")]
[assembly: AssemblyCompany(""vaelit"")]
[assembly: AssemblyVersion(""" +
                          version + @".0"")]
[assembly: AssemblyFileVersion(""" +
                          version + @".0"")]
[assembly: AssemblyInformationalVersion(""" +
                          version + @""")]

namespace MinimalQuestLauncher
{
    internal static class DistinctBuildInfo
    {
        public static readonly string EmbeddedMarker = ""QLMK1:L:00000000000000000000000000000000"";
        public static readonly string EmbeddedBuildSignature = ""QLSG1:"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"" +
            ""0000000000000000000000000000000000000000000000000000000000000000"";
        public static readonly string BuildDistinguisher = ""DISTINCT_BUILD_PAYLOAD_VERSION_" +
                          version + @"_"" + Guid.NewGuid().ToString();
    }

    public class DummyForm {}
    public class LauncherForm
    {
        public static int Main(string[] args)
        {
            string m = DistinctBuildInfo.EmbeddedMarker;
            char role = (m != null && m.Length == 40) ? m[6] : 'L';
            if (args != null && args.Length > 0 && args[0] == ""--self-check"")
            {
                Console.WriteLine(""ROLE="" + role + "";MARKER="" + m + "";VER=" +
                          version + @";SIG="" + DistinctBuildInfo.EmbeddedBuildSignature.Substring(0, 14));
                return 0;
            }
            return 0;
        }
    }
}";
            using (var provider = new CSharpCodeProvider())
            {
                var cp = new CompilerParameters { GenerateExecutable = true, OutputAssembly = outputPath,
                                                  CompilerOptions = "/target:exe /optimize+" };
                CompilerResults cr = provider.CompileAssemblyFromSource(cp, code);
                if (cr.Errors.HasErrors)
                    throw new InvalidOperationException("Failed to compile distinct 1.4+ build: " +
                                                        cr.Errors[0].ErrorText);
            }

            if (!string.IsNullOrEmpty(ephemeralPrivateKeyXml))
            {
                byte[] raw = File.ReadAllBytes(outputPath);
                byte[] signed = CopyIdentity.SignBuildBytes(raw, ephemeralPrivateKeyXml);
                File.WriteAllBytes(outputPath, signed);
            }
            return outputPath;
        }

        private static void TestPathResolution()
        {
            string baseDir = @"C:\Users\Тест Пользователь\Quest Launcher";

            var r1 = PathResolver.Resolve("Endfield", "", baseDir);
            Assert(r1.Ok && r1.FullPath == baseDir + @"\games\Endfield.exe",
                   "Default folder resolution failed: " + r1.FullPath);

            var r2 = PathResolver.Resolve("  \"Мои Игры.exe\" ", " \"подпапка/Win 64/клиент\" ", baseDir);
            Assert(r2.Ok && r2.FullPath == baseDir + @"\подпапка\Win 64\клиент\Мои Игры.exe",
                   "Cyrillic nested resolution failed: " + r2.FullPath);

            var r3 = PathResolver.Resolve("\"D:\\Игры\\Киберпанк 2077\\bin\\x64\\Cyberpunk2077\"", "games", baseDir);
            Assert(r3.Ok && r3.FullPath == @"D:\Игры\Киберпанк 2077\bin\x64\Cyberpunk2077.exe" &&
                       !string.IsNullOrEmpty(r3.Note),
                   "Full path in name field failed");

            var r4 = PathResolver.Resolve("cod.exe", @"\\fileserver\shared_games\Call of Duty HQ", baseDir);
            Assert(r4.Ok && r4.FullPath == @"\\fileserver\shared_games\Call of Duty HQ\cod.exe",
                   "UNC folder failed: " + r4.FullPath);

            var r5 = PathResolver.Resolve(@"\\nas\каталог\игра\test", "ignored", baseDir);
            Assert(r5.Ok && r5.FullPath == @"\\nas\каталог\игра\test.exe", "UNC name failed: " + r5.FullPath);

            var r6 = PathResolver.Resolve(@"sub\game.exe", "games", baseDir);
            Assert(!r6.Ok, "Relative path in name input must be rejected");

            Assert(!PathResolver.Resolve("game", @"C:relative", baseDir).Ok, "Drive-relative path must be rejected");
            Assert(!PathResolver.Resolve("game", @"\rootrel", baseDir).Ok, "Root-relative path must be rejected");

            Assert(!PathResolver.Resolve("CON.exe", "games", baseDir).Ok, "Reserved name CON must be rejected");
            Assert(!PathResolver.Resolve("bad*name", "games", baseDir).Ok, "Invalid char * must be rejected");
            Assert(!PathResolver.Resolve(".exe", "games", baseDir).Ok, "Empty stem .exe must be rejected");
        }

        private static void TestMarkerAndRoleIndependence()
        {
            byte[] launcherBytes = File.ReadAllBytes(_builtExe);
            List<int> markers = CopyIdentity.FindMarkers(launcherBytes);
            Assert(markers.Count == 1, "Built launcher must have exactly 1 role marker, found: " + markers.Count);
            string marker = CopyIdentity.ReadMarker(launcherBytes, markers[0]);
            Assert(marker == CopyIdentity.LauncherMarker, "Built launcher marker must be L: " + marker);

            List<int> sigSlots = CopyIdentity.FindSignatureSlots(launcherBytes);
            Assert(sigSlots.Count == 1, "Built launcher must have exactly 1 QLSG1 signature slot");

            string[] embeddedNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            foreach (string resName in embeddedNames)
            {
                Assert(resName.IndexOf("key", StringComparison.OrdinalIgnoreCase) < 0,
                       "Test assembly must NOT contain any embedded key resources, found: " + resName);
            }

            string dir = Path.Combine(_tempRoot, "roles");
            Directory.CreateDirectory(dir);
            string renamedLauncher = Path.Combine(dir, "DiscordQuestHelper.exe");
            File.WriteAllBytes(renamedLauncher, launcherBytes);

            string outLauncher = RunSelfCheck(renamedLauncher);
            Assert(outLauncher.Contains("ROLE=L;"),
                   "Renamed launcher without 'Launcher' substring must still have ROLE=L, got: " + outLauncher);

            string copyId = CopyIdentity.NewCopyId();
            byte[] copyBytes = CopyIdentity.BuildCopyBytes(launcherBytes, copyId);
            string copyWithLauncherInName = Path.Combine(dir, "EpicGamesLauncher.exe");
            File.WriteAllBytes(copyWithLauncherInName, copyBytes);

            string outCopy = RunSelfCheck(copyWithLauncherInName);
            Assert(outCopy.Contains("ROLE=D;") && outCopy.Contains(copyId),
                   "Copy named EpicGamesLauncher.exe must have ROLE=D and its copyId, got: " + outCopy);
        }

        private static string RunSelfCheck(string exePath)
        {
            var psi = new ProcessStartInfo { FileName = exePath, Arguments = "--self-check", UseShellExecute = false,
                                             RedirectStandardOutput = true, CreateNoWindow = true };
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return output.Trim();
            }
        }

        private static void TestForeignExeAndSelfProtection()
        {
            string dir = Path.Combine(_tempRoot, "foreign");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            CopyServiceResult selfRes = svc.CreateCopy(_builtExe, "Self");
            Assert(!selfRes.Ok, "Must refuse to overwrite launcher itself");

            string foreignExe = Path.Combine(dir, "RealGame.exe");
            byte[] foreignContent = Encoding.ASCII.GetBytes("MZ_FOREIGN_EXECUTABLE_PAYLOAD_DO_NOT_TOUCH_1234567890");
            File.WriteAllBytes(foreignExe, foreignContent);
            string foreignShaBefore = CopyIdentity.Sha256(foreignContent);

            FileInspection fi = CopyIdentity.Inspect(foreignExe, svc.CurrentBuildHash);
            Assert(fi.Kind == FileKind.Foreign, "Must classify arbitrary MZ file as Foreign, got: " + fi.Kind);

            CopyServiceResult createOverForeign = svc.CreateCopy(foreignExe, "RealGame.exe");
            Assert(!createOverForeign.Ok && createOverForeign.OpStatus == FileOpStatus.Exists,
                   "Must refuse to overwrite existing foreign EXE");

            string foreignShaAfter = CopyIdentity.Sha256(File.ReadAllBytes(foreignExe));
            Assert(foreignShaBefore == foreignShaAfter, "Foreign EXE bytes must remain untouched");
        }

        private static void TestForeignExeWithValidMarkerRejected()
        {
            string dir = Path.Combine(_tempRoot, "spoofed_marker");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);
            string trustedBuildHash = svc.CurrentBuildHash;

            string fakeCopyId = CopyIdentity.NewCopyId();
            string validCopyMarker = CopyIdentity.MakeCopyMarker(fakeCopyId);
            Assert(CopyIdentity.IsValidMarker(validCopyMarker), "Marker itself must be syntactically valid");

            var spoofedBytes = new List<byte>();
            spoofedBytes.AddRange(Encoding.ASCII.GetBytes("MZ_THIRD_PARTY_GAME_OR_TOOL_WITH_EMBEDDED_MARKER_"));
            spoofedBytes.AddRange(Encoding.Unicode.GetBytes(validCopyMarker));
            spoofedBytes.AddRange(Encoding.Unicode.GetBytes(CopyIdentity.EmptySignatureSlot));
            spoofedBytes.AddRange(Encoding.ASCII.GetBytes("_TRAILING_PAYLOAD_BYTES_NOT_FROM_QUEST_LAUNCHER"));

            byte[] spoofedArray = spoofedBytes.ToArray();
            List<int> foundMarkers = CopyIdentity.FindMarkers(spoofedArray);
            Assert(foundMarkers.Count == 1, "Spoofed payload must contain 1 valid marker");

            string spoofedPath = Path.Combine(dir, "SpoofedWithMarker.exe");
            File.WriteAllBytes(spoofedPath, spoofedArray);
            string spoofedSha = CopyIdentity.Sha256(spoofedArray);

            FileInspection fi = CopyIdentity.Inspect(spoofedPath, trustedBuildHash);
            Assert(fi.Kind == FileKind.Foreign && !fi.IsVerifiedOwnedCopy,
                   "Foreign file with valid QLMK1 marker MUST be classified as Foreign, got: " + fi.Kind);

            List<ImportCandidate> scan = svc.ScanForCopies(
                dir, 2, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None, null);
            Assert(scan.Count == 0, "Scanner must ignore foreign files with spoofed marker, found: " + scan.Count);

            var injectedEntry =
                new LibraryEntry { Id = fakeCopyId,
                                   Name = "SpoofedWithMarker.exe",
                                   Path = spoofedPath,
                                   Version = "1.4.0",
                                   Sha256 = spoofedSha,
                                   BuildHash = CopyIdentity.ComputeBuildHash(spoofedArray, foundMarkers),
                                   Size = spoofedArray.Length,
                                   CreatedUtc = DateTime.UtcNow,
                                   Origin = "imported" };
            lib.Update(list => list.Add(injectedEntry));

            EntryView ev = svc.Evaluate(injectedEntry, null);
            Assert(ev.Status == EntryStatus.Modified,
                   "Registry entry pointing to foreign file with valid marker must be evaluated as Modified, got: " +
                       ev.Status);

            CopyServiceResult upd = svc.UpdateCopy(injectedEntry);
            Assert(!upd.Ok && upd.OpStatus == FileOpStatus.Modified,
                   "UpdateCopy must refuse to overwrite foreign file with valid marker");
            Assert(CopyIdentity.Sha256(File.ReadAllBytes(spoofedPath)) == spoofedSha,
                   "Spoofed foreign file bytes must remain untouched after rejected UpdateCopy");

            CopyServiceResult del = svc.DeleteCopy(injectedEntry, true);
            Assert(!del.Ok && del.OpStatus == FileOpStatus.Modified && File.Exists(spoofedPath),
                   "DeleteCopy(deleteFileFromDisk: true) must refuse to delete foreign file with valid marker");
        }

        private static void TestLegacyLikelyCannotUpdateOrDeleteDiskFile()
        {
            string dir = Path.Combine(_tempRoot, "legacy_likely");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string buildTmp = Path.Combine(dir, "build_tmp");
            Directory.CreateDirectory(buildTmp);
            string compiledPath = CompileUnverifiedLegacyLikelyExe(Path.Combine(buildTmp, "QuestLauncher.exe"));
            string candidatePath = Path.Combine(dir, "HeuristicGame.exe");
            File.Copy(compiledPath, candidatePath);
            Directory.Delete(buildTmp, true);

            string originalSha = CopyIdentity.Sha256(File.ReadAllBytes(candidatePath));

            FileInspection fi = CopyIdentity.Inspect(candidatePath, svc.CurrentBuildHash);
            Assert(fi.Kind == FileKind.LegacyLikely, "Expected LegacyLikely for heuristic assembly, got: " + fi.Kind);
            Assert(!fi.IsVerifiedOwnedCopy, "LegacyLikely MUST NOT have IsVerifiedOwnedCopy == true");

            List<ImportCandidate> found = svc.ScanForCopies(
                dir, 2, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None, null);
            Assert(found.Count == 1 && found[0].Kind == FileKind.LegacyLikely && !found[0].Selected,
                   "Scanner must find LegacyLikely with Selected == false by default");

            found[0].Selected = true;
            int imported = svc.ImportSelected(found);
            Assert(imported == 1, "Manual import of LegacyLikely candidate into registry should succeed");

            LibraryEntry entry = lib.Load()[0];
            EntryView ev = svc.Evaluate(entry, null);
            Assert(ev.Status == EntryStatus.Unverified,
                   "Imported LegacyLikely entry must have EntryStatus.Unverified (NOT Outdated/Ready), got: " +
                       ev.Status);

            CopyServiceResult upd = svc.UpdateCopy(entry);
            Assert(!upd.Ok && upd.OpStatus == FileOpStatus.Modified,
                   "UpdateCopy MUST refuse to overwrite LegacyLikely file");
            Assert(CopyIdentity.Sha256(File.ReadAllBytes(candidatePath)) == originalSha,
                   "LegacyLikely file content on disk must remain untouched");

            CopyServiceResult delDisk = svc.DeleteCopy(entry, true);
            Assert(!delDisk.Ok && delDisk.OpStatus == FileOpStatus.Modified && File.Exists(candidatePath),
                   "DeleteCopy(deleteFileFromDisk: true) MUST refuse to delete LegacyLikely file from disk");

            byte[] newCopyBytes = CopyIdentity.BuildCopyBytes(File.ReadAllBytes(_builtExe), CopyIdentity.NewCopyId());
            FileOpResult lowReplace =
                FileOps.ReplaceOwned(candidatePath, originalSha, newCopyBytes, svc.CurrentBuildHash);
            Assert(!lowReplace.Ok && lowReplace.Status == FileOpStatus.Modified,
                   "FileOps.ReplaceOwned must reject LegacyLikely even if expectedSha matches");
            FileOpResult lowDelete = FileOps.DeleteOwned(candidatePath, originalSha, svc.CurrentBuildHash);
            Assert(!lowDelete.Ok && lowDelete.Status == FileOpStatus.Modified && File.Exists(candidatePath),
                   "FileOps.DeleteOwned must reject LegacyLikely even if expectedSha matches");

            CopyServiceResult delRegOnly = svc.DeleteCopy(entry, false);
            Assert(delRegOnly.Ok && File.Exists(candidatePath) && lib.Load().Count == 0,
                   "Removing only the registry record for LegacyLikely must succeed without touching disk file");
        }

        private static void TestTamperedRegistryEntryBlocked()
        {
            string dir = Path.Combine(_tempRoot, "tamper");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string copyPath = Path.Combine(dir, "GameCopy.exe");
            CopyServiceResult cr = svc.CreateCopy(copyPath, "GameCopy.exe");
            Assert(cr.Ok && File.Exists(copyPath), "CreateCopy must succeed");

            byte[] replacement = Encoding.ASCII.GetBytes("MZ_REPLACED_BY_REAL_GAME_BINARY_AFTER_REGISTRATION");
            File.WriteAllBytes(copyPath, replacement);
            string replacementSha = CopyIdentity.Sha256(replacement);

            EntryView ev = svc.Evaluate(cr.Entry, null);
            Assert(ev.Status == EntryStatus.Modified,
                   "Tampered file must have EntryStatus.Modified, got: " + ev.Status);

            CopyServiceResult upd = svc.UpdateCopy(cr.Entry);
            Assert(!upd.Ok && upd.OpStatus == FileOpStatus.Modified, "UpdateCopy must block tampered file");
            Assert(CopyIdentity.Sha256(File.ReadAllBytes(copyPath)) == replacementSha,
                   "Tampered file must not be modified by UpdateCopy");

            CopyServiceResult del = svc.DeleteCopy(cr.Entry, true);
            Assert(!del.Ok && del.OpStatus == FileOpStatus.Modified && File.Exists(copyPath),
                   "DeleteCopy(deleteFileFromDisk: true) must refuse to delete tampered file");
        }

        private static void TestUpgradeBetweenTwoDistinct14PlusBuilds()
        {
            string dir = Path.Combine(_tempRoot, "upgrade_14_to_141");
            Directory.CreateDirectory(dir);

            string revokedPubXml =
                "<RSAKeyValue><Modulus>wFmjR/rQmTieu6WWWGdGg2HvdKJdK81lqA7xJaNeYUC2J7fXYALf1UhHDZECq62kMAtIvTqFn2Woe7Pmyuw8b1tzkDLTFYnoBfczts0A9HZBqcz8eoBRELn4nIPVX5Q6dw+GfamV2Nl4PZbmuHEmT4tNddpdnMUCe38TYN3XRNpX07SfFrMhMZACamj30YReBJvq9dKkY2vg8osPYg5+9f94d0Q13PObu+sFaum8wrZkDcw1BNIicNi38VGBbCi3hleEcWtetjh0GjTuFYpXEIcboAHuFQYugqxbIIQ0OwhJgLh7oGlAZeZmNlU57IGXHUWOr+UK7zT9SWocoycoqQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
            byte[] builtBytes = File.ReadAllBytes(_builtExe);
            Assert(!CopyIdentity.VerifyBuildSignatureWithKey(builtBytes, CopyIdentity.FindMarkers(builtBytes),
                                                             revokedPubXml),
                   "Revoked key must NEVER be accepted");

            string ephemeralPrivXml;
            string ephemeralPubXml;
            using (var rsa = new RSACng(2048))
            {
                ephemeralPrivXml = rsa.ToXmlString(true);
                ephemeralPubXml = rsa.ToXmlString(false);
            }

            string buildADir = Path.Combine(dir, "launcher_v140");
            string buildBDir = Path.Combine(dir, "launcher_v141");
            Directory.CreateDirectory(buildADir);
            Directory.CreateDirectory(buildBDir);

            string buildAPath =
                CompileDistinct14PlusBuild(Path.Combine(buildADir, "QuestLauncher_140.exe"), "1.4.0", ephemeralPrivXml);
            string buildBPath =
                CompileDistinct14PlusBuild(Path.Combine(buildBDir, "QuestLauncher_141.exe"), "1.4.1", ephemeralPrivXml);

            CopyIdentity.TestHarnessPublicKeyXml = null;
            byte[] buildABytes = File.ReadAllBytes(buildAPath);
            Assert(!CopyIdentity.VerifyOfficialBuildSignature(buildABytes, CopyIdentity.FindMarkers(buildABytes)),
                   "Production VerifyOfficialBuildSignature MUST reject test-key-signed binaries");

            string testSignedCopyProbe = Path.Combine(dir, "ProbeTestSignedCopy.exe");
            File.WriteAllBytes(testSignedCopyProbe, CopyIdentity.BuildCopyBytes(buildABytes, CopyIdentity.NewCopyId()));
            FileInspection prodInspection = CopyIdentity.Inspect(
                testSignedCopyProbe, "0000000000000000000000000000000000000000000000000000000000000000");
            Assert(
                !prodInspection.IsVerifiedOwnedCopy,
                "Production Inspect (when TestHarnessPublicKeyXml is null) MUST NOT trust test-key-signed copy as OwnCopy");

            var savedHashes =
                new Dictionary<string, string>(CopyIdentity.KnownOfficialBuildHashes, StringComparer.OrdinalIgnoreCase);
            CopyIdentity.KnownOfficialBuildHashes.Clear();
            CopyIdentity.TestHarnessPublicKeyXml = ephemeralPubXml;
            try
            {
                var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
                var svcA = new CopyService(lib, buildAPath, "1.4.0");
                var svcB = new CopyService(lib, buildBPath, "1.4.1");

                Assert(!string.Equals(svcA.CurrentBuildHash, svcB.CurrentBuildHash, StringComparison.OrdinalIgnoreCase),
                       "Build A (1.4.0) and Build B (1.4.1) must have genuinely different BuildHashes");

                string gameCopyPath = Path.Combine(dir, "games", "DistinctGame.exe");
                CopyServiceResult cr = svcA.CreateCopy(gameCopyPath, "DistinctGame.exe");
                Assert(cr.Ok && File.Exists(gameCopyPath), "Build A (1.4.0) CreateCopy failed");

                string unregCopyPath = Path.Combine(dir, "games", "UnregFrom140.exe");
                File.WriteAllBytes(unregCopyPath, CopyIdentity.BuildCopyBytes(buildABytes, CopyIdentity.NewCopyId()));

                FileInspection fiFromB = CopyIdentity.Inspect(gameCopyPath, svcB.CurrentBuildHash);
                Assert(fiFromB.Kind == FileKind.OwnCopy && fiFromB.IsVerifiedOwnedCopy,
                       "Newer 1.4.1 build MUST recognize signed 1.4.0 copy as verified OwnCopy (NOT Foreign), got: " +
                           fiFromB.Kind);
                Assert(string.Equals(fiFromB.BuildHash, svcA.CurrentBuildHash, StringComparison.OrdinalIgnoreCase),
                       "Inspected BuildHash must match 1.4.0 BuildHash");

                EntryView evBefore = svcB.Evaluate(cr.Entry, null);
                Assert(evBefore.Status == EntryStatus.Outdated,
                       "Newer 1.4.1 build MUST evaluate 1.4.0 copy as Outdated (NOT Modified/Foreign), got: " +
                           evBefore.Status);

                var existingSet =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PathResolver.Normalize(gameCopyPath) };
                List<ImportCandidate> scannedByB =
                    svcB.ScanForCopies(Path.Combine(dir, "games"), 2, existingSet, CancellationToken.None, null);
                Assert(scannedByB.Count == 1 && scannedByB[0].Kind == FileKind.OwnCopy && scannedByB[0].Selected,
                       "Newer 1.4.1 scanner must detect unregistered 1.4.0 copy as verified OwnCopy");

                string unsignedExePath =
                    CompileDistinct14PlusBuild(Path.Combine(dir, "UnsignedFake14.exe"), "1.4.0", null);
                byte[] unsignedCopyBytes =
                    CopyIdentity.BuildCopyBytes(File.ReadAllBytes(unsignedExePath), CopyIdentity.NewCopyId());
                File.WriteAllBytes(unsignedExePath, unsignedCopyBytes);
                FileInspection fiUnsigned = CopyIdentity.Inspect(unsignedExePath, svcB.CurrentBuildHash);
                Assert(!fiUnsigned.IsVerifiedOwnedCopy, "Unsigned 1.4+ binary must NOT have IsVerifiedOwnedCopy");

                CopyServiceResult upd = svcB.UpdateCopy(cr.Entry);
                Assert(upd.Ok, "UpdateCopy from v1.4.0 to v1.4.1 failed: " + upd.Message);

                EntryView evAfter = svcB.Evaluate(upd.Entry, null);
                Assert(evAfter.Status == EntryStatus.Ready,
                       "After UpdateCopy to v1.4.1, status must be Ready, got: " + evAfter.Status);
                Assert(string.Equals(upd.Entry.BuildHash, svcB.CurrentBuildHash, StringComparison.OrdinalIgnoreCase),
                       "Updated copy must now have Build B (1.4.1) BuildHash");

                string selfCheckOut = RunSelfCheck(gameCopyPath);
                Assert(selfCheckOut.Contains("ROLE=D;") && selfCheckOut.Contains("VER=1.4.1"),
                       "Updated 1.4.1 copy must run autonomously as ROLE=D with VER=1.4.1, got: " + selfCheckOut);
            }
            finally
            {
                CopyIdentity.TestHarnessPublicKeyXml = null;
                CopyIdentity.KnownOfficialBuildHashes.Clear();
                foreach (var kv in savedHashes)
                    CopyIdentity.KnownOfficialBuildHashes[kv.Key] = kv.Value;
            }
        }

        private static void TestExclusiveHandleAndRaceProtection()
        {
            string dir = Path.Combine(_tempRoot, "race_lock");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string copyPath = Path.Combine(dir, "ExclusiveTarget.exe");
            CopyServiceResult cr = svc.CreateCopy(copyPath, "ExclusiveTarget.exe");
            Assert(cr.Ok && File.Exists(copyPath), "CreateCopy failed");

            byte[] updatedBytes = CopyIdentity.BuildCopyBytes(File.ReadAllBytes(_builtExe), CopyIdentity.NewCopyId());

            using (var externalHandle =
                       new FileStream(copyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                FileOpResult repWhileShared =
                    FileOps.ReplaceOwned(copyPath, cr.Entry.Sha256, updatedBytes, svc.CurrentBuildHash);
                Assert(
                    !repWhileShared.Ok && repWhileShared.Status == FileOpStatus.Busy,
                    "ReplaceOwned MUST reject file when another handle is open (even with FileShare.ReadWrite|Delete), got: " +
                        repWhileShared.Status);

                FileOpResult delWhileShared = FileOps.DeleteOwned(copyPath, cr.Entry.Sha256, svc.CurrentBuildHash);
                Assert(
                    !delWhileShared.Ok && delWhileShared.Status == FileOpStatus.Busy && File.Exists(copyPath),
                    "DeleteOwned MUST reject file when another handle is open (even with FileShare.ReadWrite|Delete), got: " +
                        delWhileShared.Status);
            }

            FileOpResult repOk = FileOps.ReplaceOwned(copyPath, cr.Entry.Sha256, updatedBytes, svc.CurrentBuildHash);
            Assert(repOk.Ok && repOk.Sha256 == CopyIdentity.Sha256(updatedBytes),
                   "ReplaceOwned should succeed when no external handles exist: " + repOk.Message);

            FileOpResult delOk = FileOps.DeleteOwned(copyPath, repOk.Sha256, svc.CurrentBuildHash);
            Assert(delOk.Ok && !File.Exists(copyPath),
                   "DeleteOwned via SetFileInformationByHandle should remove file cleanly: " + delOk.Message);
        }

        private static void TestRecoverIfInterruptedNeverOverwritesForeignTarget()
        {
            string dir = Path.Combine(_tempRoot, "foreign_with_qlbak");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string target = Path.Combine(dir, "UserRealGame.exe");
            string backupPath = FileOps.GetBackupPath(target);

            byte[] foreignBytes =
                Encoding.ASCII.GetBytes("MZ_ARBITRARY_FOREIGN_USER_GAME_EXECUTABLE_DO_NOT_OVERWRITE_EVER_1234567890");
            File.WriteAllBytes(target, foreignBytes);
            string foreignSha = CopyIdentity.Sha256(foreignBytes);

            byte[] genuineLegacyBytes =
                LoadVerifiedRealReleaseFixture(RealReleaseV13Resource, RealReleaseV13Size, RealReleaseV13Sha256);
            File.WriteAllBytes(backupPath, genuineLegacyBytes);
            string expectedSha = RealReleaseV13Sha256;

            bool recovered = FileOps.RecoverIfInterrupted(target, expectedSha, svc.CurrentBuildHash);
            Assert(!recovered, "RecoverIfInterrupted MUST return false when target contains a foreign file");
            Assert(File.Exists(target) && File.Exists(backupPath),
                   "Both target and target.qlbak must be preserved on disk");
            Assert(string.Equals(CopyIdentity.Sha256(File.ReadAllBytes(target)), foreignSha,
                                 StringComparison.OrdinalIgnoreCase),
                   "Foreign target file MUST NOT be overwritten by RecoverIfInterrupted");

            var entry = new LibraryEntry { Id = CopyIdentity.NewCopyId(),
                                           Name = "UserRealGame.exe",
                                           Path = target,
                                           Version = "1.3",
                                           Sha256 = expectedSha,
                                           Size = genuineLegacyBytes.Length,
                                           CreatedUtc = DateTime.UtcNow,
                                           Origin = "imported-legacy" };
            lib.Update(list => list.Add(entry));

            EntryView ev = svc.Evaluate(entry, null);
            Assert(ev.Status == EntryStatus.RecoveryPending,
                   "Evaluate must report EntryStatus.RecoveryPending (preserving both files), got: " + ev.Status);
            Assert(string.Equals(CopyIdentity.Sha256(File.ReadAllBytes(target)), foreignSha,
                                 StringComparison.OrdinalIgnoreCase),
                   "Evaluate MUST NOT overwrite foreign target file");

            CopyServiceResult upd = svc.UpdateCopy(entry);
            Assert(!upd.Ok && upd.OpStatus == FileOpStatus.Modified,
                   "UpdateCopy MUST refuse to overwrite target while unresolved .qlbak conflict exists");
            Assert(string.Equals(CopyIdentity.Sha256(File.ReadAllBytes(target)), foreignSha,
                                 StringComparison.OrdinalIgnoreCase),
                   "UpdateCopy MUST NOT modify foreign target file");

            byte[] newCopyBytes = CopyIdentity.BuildCopyBytes(File.ReadAllBytes(_builtExe), CopyIdentity.NewCopyId());
            FileOpResult lowRep = FileOps.ReplaceOwned(target, expectedSha, newCopyBytes, svc.CurrentBuildHash);
            Assert(!lowRep.Ok && lowRep.Status == FileOpStatus.Modified,
                   "ReplaceOwned MUST refuse to overwrite target when .qlbak conflict exists");
            Assert(string.Equals(CopyIdentity.Sha256(File.ReadAllBytes(target)), foreignSha,
                                 StringComparison.OrdinalIgnoreCase),
                   "ReplaceOwned MUST NOT modify foreign target file");

            CopyServiceResult del = svc.DeleteCopy(entry, true);
            Assert(!del.Ok && del.OpStatus == FileOpStatus.Modified && File.Exists(target),
                   "DeleteCopy(deleteFileFromDisk: true) MUST refuse to delete foreign target file");
        }

        private static void TestCrashRecoveryAndStateConsistency()
        {
            string dir = Path.Combine(_tempRoot, "crash_recovery");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string targetExe = Path.Combine(dir, "InterruptedGame.exe");
            CopyServiceResult cr = svc.CreateCopy(targetExe, "InterruptedGame.exe");
            Assert(cr.Ok && File.Exists(targetExe), "Initial copy creation failed");

            string originalSha = cr.Entry.Sha256;
            long originalSize = new FileInfo(targetExe).Length;
            string backupPath = FileOps.GetBackupPath(targetExe);
            string signalFile = Path.Combine(dir, "mid_write_ready.signal");

            string testRunnerExe = Assembly.GetExecutingAssembly().Location;
            var psi =
                new ProcessStartInfo { FileName = testRunnerExe,
                                       Arguments = string.Format("--crash-mid-replace \"{0}\" \"{1}\" \"{2}\" \"{3}\"",
                                                                 targetExe, originalSha, _builtExe, signalFile),
                                       UseShellExecute = false, CreateNoWindow = true };

            using (Process child = Process.Start(psi))
            {
                Stopwatch sw = Stopwatch.StartNew();
                while (!File.Exists(signalFile) && !child.HasExited && sw.ElapsedMilliseconds < 10000)
                {
                    Thread.Sleep(25);
                }
                Assert(File.Exists(signalFile), "Child process did not reach mid-write hook in time");
                child.Kill();
                child.WaitForExit(5000);
            }

            long corruptedSize = new FileInfo(targetExe).Length;
            Assert(corruptedSize == 1024 && corruptedSize != originalSize,
                   "Target EXE must be truncated (1024 bytes) after mid-write Process.Kill, got: " + corruptedSize);
            Assert(File.Exists(backupPath),
                   "Backup file (.qlbak) MUST remain on disk when process is killed mid-write");

            EntryView pendingView = svc.Evaluate(cr.Entry, null);
            Assert(pendingView.Status == EntryStatus.RecoveryPending,
                   "Evaluate after mid-write crash must preserve both files and return RecoveryPending, got: " +
                       pendingView.Status);
            Assert(new FileInfo(targetExe).Length == 1024 && File.Exists(backupPath),
                   "Both truncated EXE and .qlbak must remain untouched before explicit user confirmation");

            CopyServiceResult explicitRestore = svc.RestoreFromBackupConfirmed(cr.Entry, true);
            Assert(explicitRestore.Ok, "Explicit RestoreFromBackupConfirmed failed: " + explicitRestore.Message);
            Assert(new FileInfo(targetExe).Length == originalSize &&
                       string.Equals(CopyIdentity.Sha256(File.ReadAllBytes(targetExe)), originalSha,
                                     StringComparison.OrdinalIgnoreCase),
                   "Target EXE must be restored to exact original bytes after explicit confirmation");
            Assert(!File.Exists(backupPath), "Backup .qlbak must be removed after explicit restore");
            string[] displacedArchives = Directory.GetFiles(dir, "InterruptedGame.exe.displaced-*.bak");
            Assert(displacedArchives.Length == 1 && new FileInfo(displacedArchives[0]).Length == 1024,
                   "Displaced file bytes must be archived to .displaced-*.bak on explicit restore");

            File.WriteAllBytes(backupPath, File.ReadAllBytes(targetExe));
            EntryView evCrashBeforeWrite = svc.Evaluate(cr.Entry, null);
            Assert(evCrashBeforeWrite.Status == EntryStatus.Ready && !File.Exists(backupPath),
                   "Crash before EXE modification must clean up redundant .qlbak without altering EXE");

            byte[] oldBytesForBak = File.ReadAllBytes(targetExe);
            File.WriteAllBytes(backupPath, oldBytesForBak);

            byte[] v13Bytes =
                LoadVerifiedRealReleaseFixture(RealReleaseV13Resource, RealReleaseV13Size, RealReleaseV13Sha256);
            File.WriteAllBytes(backupPath, v13Bytes);
            lib.Update(list =>
                       {
                           LibraryEntry t = Library.FindByPath(list, targetExe);
                           t.Version = "1.3";
                           t.Sha256 = RealReleaseV13Sha256;
                       });
            LibraryEntry preLibCrashEntry = lib.Load()[0];
            EntryView evCrashBeforeLibSave = svc.Evaluate(preLibCrashEntry, null);
            Assert(
                evCrashBeforeLibSave.Status == EntryStatus.Ready && !File.Exists(backupPath),
                "Crash after full EXE write but before library save must reconcile library to the new EXE and clean up .qlbak, got: " +
                    evCrashBeforeLibSave.Status);
            Assert(string.Equals(lib.Load()[0].Sha256, originalSha, StringComparison.OrdinalIgnoreCase),
                   "Library entry Sha256 must be updated to match the completed EXE without overwriting the EXE");

            File.WriteAllBytes(backupPath, v13Bytes);
            LibraryEntry postLibCrashEntry = lib.Load()[0];
            EntryView evCrashAfterLibSave = svc.Evaluate(postLibCrashEntry, null);
            Assert(
                evCrashAfterLibSave.Status == EntryStatus.Ready && !File.Exists(backupPath),
                "Crash after library save but before .qlbak removal must clean up leftover .qlbak without altering EXE");
        }

        private static void TestLockedFileAndAccessDenied()
        {
            string dir = Path.Combine(_tempRoot, "locked");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string copyPath = Path.Combine(dir, "BusyGame.exe");
            CopyServiceResult cr = svc.CreateCopy(copyPath, "BusyGame.exe");
            Assert(cr.Ok, "Initial copy creation failed");

            using (var lockStream = new FileStream(copyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert(FileOps.IsBusy(copyPath), "FileOps.IsBusy must detect locked file");
                CopyServiceResult updBusy = svc.UpdateCopy(cr.Entry);
                Assert(!updBusy.Ok && updBusy.OpStatus == FileOpStatus.Busy,
                       "UpdateCopy on locked file must return Busy status, got: " + updBusy.OpStatus);
            }

            File.SetAttributes(copyPath, FileAttributes.ReadOnly);
            try
            {
                CopyServiceResult updRo = svc.UpdateCopy(cr.Entry);
                Assert(!updRo.Ok &&
                           (updRo.OpStatus == FileOpStatus.AccessDenied || updRo.OpStatus == FileOpStatus.Failed),
                       "UpdateCopy on ReadOnly file must fail with AccessDenied");
            }
            finally
            {
                File.SetAttributes(copyPath, FileAttributes.Normal);
            }

            string aclDir = Path.Combine(dir, "no_write_dir");
            Directory.CreateDirectory(aclDir);
            IdentityReference currentUser = WindowsIdentity.GetCurrent().User;
            DirectorySecurity ds = Directory.GetAccessControl(aclDir);
            var denyRule = new FileSystemAccessRule(currentUser, FileSystemRights.Write | FileSystemRights.CreateFiles,
                                                    AccessControlType.Deny);
            ds.AddAccessRule(denyRule);
            Directory.SetAccessControl(aclDir, ds);
            try
            {
                CopyServiceResult aclRes = svc.CreateCopy(Path.Combine(aclDir, "Denied.exe"), "Denied.exe");
                Assert(!aclRes.Ok && aclRes.OpStatus == FileOpStatus.AccessDenied,
                       "CreateCopy in write-denied directory must return AccessDenied, got: " + aclRes.OpStatus);
            }
            finally
            {
                ds.RemoveAccessRule(denyRule);
                Directory.SetAccessControl(aclDir, ds);
            }
        }

        private static void TestCorruptConfigAndLibraryRecovery()
        {
            string dir = Path.Combine(_tempRoot, "corrupt");
            Directory.CreateDirectory(dir);

            string cfgPath = Path.Combine(dir, "launcher.cfg");
            var s1 = new Settings { Game = "FirstGame", Folder = "Win64", Filter = "fav" };
            Assert(s1.Save(cfgPath), "First settings save failed");
            var s2 = new Settings { Game = "SecondGame", Folder = "games", Filter = "recent" };
            Assert(s2.Save(cfgPath), "Second settings save failed");
            Assert(File.Exists(cfgPath + ".bak"), "Backup launcher.cfg.bak must exist");

            File.WriteAllBytes(cfgPath, new byte[] { 0, 1, 2, 0, 255 });
            Settings recovered = Settings.Load(cfgPath, null);
            Assert(recovered.Game == "FirstGame" && recovered.Folder == "Win64" &&
                       !string.IsNullOrEmpty(recovered.LoadWarning),
                   "Settings must recover from .bak when main file is corrupted with binary data");

            string libPath = Path.Combine(dir, "QuestLauncher.library");
            var lib = new Library(libPath);
            lib.Update(list =>
                       {
                           list.Add(new LibraryEntry { Id = CopyIdentity.NewCopyId(), Name = "SavedGame.exe",
                                                       Path = @"C:\Games\SavedGame.exe", Version = "1.4.0",
                                                       Sha256 = new string('A', 64), BuildHash = new string('B', 64),
                                                       Size = 12345, CreatedUtc = DateTime.UtcNow, Favorite = true,
                                                       Origin = "created" });
                       });
            lib.Update(list =>
                       { list[0].LaunchCount = 5; });
            File.WriteAllText(libPath, "CORRUPT_HEADER_AND_CONTENT", Encoding.UTF8);

            List<LibraryEntry> restoredEntries = lib.Load();
            Assert(restoredEntries.Count == 1 && restoredEntries[0].Name == "SavedGame.exe" &&
                       !string.IsNullOrEmpty(lib.LastWarning),
                   "Library must recover from .bak and set warning when main file is corrupted");
        }

        private static void TestRealLegacyReleaseImportAndUpdate()
        {
            string dir = Path.Combine(_tempRoot, "real_legacy_releases");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            byte[] realV12Bytes =
                LoadVerifiedRealReleaseFixture(RealReleaseV12Resource, RealReleaseV12Size, RealReleaseV12Sha256);
            byte[] realV13Bytes =
                LoadVerifiedRealReleaseFixture(RealReleaseV13Resource, RealReleaseV13Size, RealReleaseV13Sha256);

            string sub12 = Path.Combine(dir, "Release_1_2");
            string sub13 = Path.Combine(dir, "Release_1_3");
            Directory.CreateDirectory(sub12);
            Directory.CreateDirectory(sub13);

            string exe12 = Path.Combine(sub12, "GameFromRelease12.exe");
            string exe13 = Path.Combine(sub13, "GameFromRelease13.exe");
            File.WriteAllBytes(exe12, realV12Bytes);
            File.WriteAllBytes(exe13, realV13Bytes);

            FileInspection fi12 = CopyIdentity.Inspect(exe12, svc.CurrentBuildHash);
            FileInspection fi13 = CopyIdentity.Inspect(exe13, svc.CurrentBuildHash);
            Assert(fi12.Kind == FileKind.LegacyKnown && fi12.Version == "1.2" && fi12.IsVerifiedOwnedCopy,
                   "Real v1.2 release EXE must be recognized as LegacyKnown v1.2, got: " + fi12.Kind +
                       " v=" + fi12.Version);
            Assert(fi13.Kind == FileKind.LegacyKnown && fi13.Version == "1.3" && fi13.IsVerifiedOwnedCopy,
                   "Real v1.3 release EXE must be recognized as LegacyKnown v1.3, got: " + fi13.Kind +
                       " v=" + fi13.Version);

            List<ImportCandidate> found = svc.ScanForCopies(
                dir, 3, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None, null);
            Assert(found.Count == 2 && found[0].Selected && found[1].Selected,
                   "Scanner must find and pre-select both real legacy release copies (v1.2 and v1.3)");

            int imported = svc.ImportSelected(found);
            Assert(imported == 2, "Both real legacy release copies must be imported");

            foreach (LibraryEntry entry in lib.Load())
            {
                EntryView before = svc.Evaluate(entry, null);
                Assert(before.Status == EntryStatus.Outdated,
                       "Real legacy release copy " + entry.Name +
                           " must have status Outdated before update, got: " + before.Status);

                CopyServiceResult upd = svc.UpdateCopy(entry);
                Assert(upd.Ok, "Safe update of real legacy release copy " + entry.Name + " failed: " + upd.Message);

                EntryView after = svc.Evaluate(upd.Entry, null);
                Assert(after.Status == EntryStatus.Ready,
                       "Updated real legacy copy " + entry.Name + " must have status Ready, got: " + after.Status);

                string check = RunSelfCheck(entry.Path);
                Assert(check.Contains("ROLE=D;"),
                       "Updated real legacy copy must execute autonomously as ROLE=D, got: " + check);
            }
        }

        private static void TestSyntheticLegacyAndModernImport()
        {
            string dir = Path.Combine(_tempRoot, "scan_synthetic");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            byte[] portableLegacyBytes = CreateVerifiedPortableLegacyBytes();
            CopyIdentity.KnownLegacy[PortableLegacyExpectedSha256] = "1.3-portable-test";
            try
            {
                string sub = Path.Combine(dir, "AION 2");
                Directory.CreateDirectory(sub);
                string legacyCopy = Path.Combine(sub, "AION2.exe");
                File.WriteAllBytes(legacyCopy, portableLegacyBytes);

                File.WriteAllBytes(Path.Combine(sub, "UnrelatedApp.exe"),
                                   Encoding.ASCII.GetBytes("MZ_NOT_QUEST_LAUNCHER_AT_ALL"));

                string modernUnregistered = Path.Combine(sub, "ModernUnreg.exe");
                byte[] modernBytes =
                    CopyIdentity.BuildCopyBytes(File.ReadAllBytes(_builtExe), CopyIdentity.NewCopyId());
                File.WriteAllBytes(modernUnregistered, modernBytes);

                string buildTmp = Path.Combine(dir, "tmp_build");
                Directory.CreateDirectory(buildTmp);
                string compiledLikely = CompileUnverifiedLegacyLikelyExe(Path.Combine(buildTmp, "QuestLauncher.exe"));
                string likelyCandidate = Path.Combine(sub, "LikelyCandidate.exe");
                File.Copy(compiledLikely, likelyCandidate);
                Directory.Delete(buildTmp, true);

                List<ImportCandidate> found = svc.ScanForCopies(
                    dir, 3, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None, null);
                Assert(
                    found.Count == 3,
                    "Scanner must find 3 candidates (1 LegacyKnown + 1 OwnCopy + 1 LegacyLikely) and ignore foreign EXE, found: " +
                        found.Count);

                int imported = svc.ImportSelected(found);
                Assert(imported == 2, "Must import only the 2 verified candidates by default, imported: " + imported);

                List<LibraryEntry> entries = lib.Load();
                Assert(entries.Count == 2, "Library must contain 2 verified imported entries");
            }
            finally
            {
                CopyIdentity.KnownLegacy.Remove(PortableLegacyExpectedSha256);
            }
        }

        private static void TestSyntheticSafeUpdateOutdatedCopy()
        {
            string dir = Path.Combine(_tempRoot, "update_synthetic");
            Directory.CreateDirectory(dir);
            var lib = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            byte[] portableLegacyBytes = CreateVerifiedPortableLegacyBytes();
            CopyIdentity.KnownLegacy[PortableLegacyExpectedSha256] = "1.3-portable-test";
            try
            {
                string gameExe = Path.Combine(dir, "OldGame.exe");
                File.WriteAllBytes(gameExe, portableLegacyBytes);

                List<ImportCandidate> found = svc.ScanForCopies(
                    dir, 2, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None, null);
                Assert(found.Count == 1 && found[0].Kind == FileKind.LegacyKnown,
                       "Must detect portable legacy copy as LegacyKnown");
                svc.ImportSelected(found);

                LibraryEntry entry = lib.Load()[0];
                EntryView beforeView = svc.Evaluate(entry, null);
                Assert(beforeView.Status == EntryStatus.Outdated, "Imported legacy copy must be evaluated as Outdated");

                CopyServiceResult upd = svc.UpdateCopy(entry);
                Assert(upd.Ok, "Safe update of imported legacy copy failed: " + upd.Message);

                EntryView afterView = svc.Evaluate(upd.Entry, null);
                Assert(afterView.Status == EntryStatus.Ready,
                       "Updated copy must be evaluated as Ready, got: " + afterView.Status);

                string selfCheck = RunSelfCheck(gameExe);
                Assert(selfCheck.Contains("ROLE=D;"),
                       "Updated copy must run autonomously as ROLE=D, got: " + selfCheck);
            }
            finally
            {
                CopyIdentity.KnownLegacy.Remove(PortableLegacyExpectedSha256);
            }
        }

        private static void TestProcessTrackingByFullPath()
        {
            string dir1 = Path.Combine(_tempRoot, "proc1");
            string dir2 = Path.Combine(_tempRoot, "proc2");
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);

            var lib = new Library(Path.Combine(_tempRoot, "proc.library"));
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);

            string exe1 = Path.Combine(dir1, "SameNameGame.exe");
            string exe2 = Path.Combine(dir2, "SameNameGame.exe");
            Assert(svc.CreateCopy(exe1, "SameNameGame.exe").Ok, "Create exe1 failed");
            Assert(svc.CreateCopy(exe2, "SameNameGame.exe").Ok, "Create exe2 failed");

            int pid1 = 0;
            try
            {
                pid1 = ProcessTools.StartCopy(exe1);
                Thread.Sleep(400);

                List<int> run1 = ProcessTools.FindRunning(exe1);
                List<int> run2 = ProcessTools.FindRunning(exe2);

                Assert(run1.Count == 1 && run1[0] == pid1, "Must detect running instance of exe1 by full path");
                Assert(run2.Count == 0,
                       "Must NOT falsely report exe2 as running even though it has the same file name");
            }
            finally
            {
                if (pid1 > 0)
                {
                    try
                    {
                        using (Process p = Process.GetProcessById(pid1))
                        {
                            p.Kill();
                            p.WaitForExit(2000);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void TestLogRotation()
        {
            string logFile = Path.Combine(_tempRoot, "rotation.log");
            Log.OverridePath = logFile;
            string chunk = new string('X', 8000);
            for (int i = 0; i < 40; i++)
                Log.Info(chunk);

            Assert(File.Exists(logFile), "Main log file must exist");
            Assert(File.Exists(logFile + ".1"), "Rotated log file .1 must exist after exceeding 256 KB");
            Assert(new FileInfo(logFile).Length <= Log.MaxSize + 16384, "Main log file must respect size limit");
        }

        private static void TestUpdateCheckerParsing()
        {
            Assert(UpdateChecker.CompareVersions("v1.5.0", "1.4.0") > 0, "v1.5.0 > 1.4.0");
            Assert(UpdateChecker.CompareVersions("1.3-test", "1.4.0") < 0, "1.3-test < 1.4.0");
            Assert(UpdateChecker.CompareVersions("1.4.0", "1.4.0-test") > 0, "Release > prerelease");

            string sampleJson =
                "{\"tag_name\":\"v1.5.0\",\"name\":\"Release 1.5.0\",\"html_url\":\"https://github.com/NekoPoint/QuestLauncher/releases/tag/v1.5.0\"}";
            UpdateCheckResult res = UpdateChecker.ParseGitHubReleaseJson(sampleJson, "1.4.0");
            Assert(res.Ok && res.HasNewerVersion && res.LatestTag == "v1.5.0", "GitHub JSON parsing failed");
        }

        private static void TestUiFormsAndDpiScaling()
        {
            string shotDir = Environment.GetEnvironmentVariable("QL_UI_SHOTS_DIR");
            if (string.IsNullOrEmpty(shotDir))
                shotDir = Path.Combine(_tempRoot, "ui_shots");
            Directory.CreateDirectory(shotDir);

            string previousDataDir = DataPaths.OverrideDir;
            DataPaths.OverrideDir = Path.Combine(_tempRoot, "ui-review");
            Directory.CreateDirectory(DataPaths.OverrideDir);
            using (var empty = new LauncherForm(_builtExe))
            {
                empty.ShowInTaskbar = false;
                empty.Show();
                empty.Opacity = 1;
                Application.DoEvents();
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    empty.ApplyLayout(dpi);
                    Application.DoEvents();
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var run = (Control) typeof(LauncherForm).GetField("_btnLibRun", flags).GetValue(empty);
                    var more = (Control) typeof(LauncherForm).GetField("_btnMore", flags).GetValue(empty);
                    var import = (Control) typeof(LauncherForm).GetField("_btnEmptyImport", flags).GetValue(empty);
                    Assert(!run.Visible && !more.Visible && import.Visible,
                           "Empty library must expose import, not selection actions");
                    using (var bmp = new Bitmap(empty.Width, empty.Height))
                    {
                        empty.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(Path.Combine(shotDir, "empty_dpi_" + dpi + ".png"));
                    }
                }
                empty.Hide();
            }
            var lib = new Library(DataPaths.LibraryPath);
            var svc = new CopyService(lib, _builtExe, AppInfo.Version);
            string demo1 = Path.Combine(_tempRoot, "ui_demo", "games", "Endfield.exe");
            string demo2 = Path.Combine(_tempRoot, "ui_demo", "Win64", "Endfield.exe");
            svc.CreateCopy(demo1, "Endfield.exe");
            svc.CreateCopy(demo2, "Endfield.exe");
            svc.ToggleFavorite(demo1);

            using (var lf = new LauncherForm(_builtExe))
            {
                lf.ShowInTaskbar = false;
                lf.Show();
                lf.Opacity = 1.0;
                Application.DoEvents();
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    lf.ApplyLayout(dpi);
                    Application.DoEvents();
                    float scale = dpi / 96f;
                    Assert(lf.ClientSize.Width == (int)Math.Round(900 * scale),
                           "LauncherForm width at DPI " + dpi + " mismatch");
                    Assert(lf.ClientSize.Height == (int)Math.Round(520 * scale),
                           "LauncherForm height at DPI " + dpi + " mismatch");

                    using (var bmp = new System.Drawing.Bitmap(lf.Width, lf.Height))
                    {
                        lf.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(Path.Combine(shotDir, "launcher_dpi_" + dpi + ".png"),
                                 System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                lf.ApplyLayout(96);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var query = (AnimatedInputBox) typeof(LauncherForm).GetField("_inputSearch", flags).GetValue(lf);
                query.InputText = "no-matching-game";
                Application.DoEvents();
                var import = (Control) typeof(LauncherForm).GetField("_btnEmptyImport", flags).GetValue(lf);
                Assert(!import.Visible, "Filtered empty state must not suggest duplicate import");
                using (var bmp = new Bitmap(lf.Width, lf.Height))
                {
                    lf.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(Path.Combine(shotDir, "filtered.png"));
                }
                query.InputText = "";

                typeof(LauncherForm)
                    .GetMethod("OnLibraryListMouseUp", flags)
                    .Invoke(lf, new object[] { null, new MouseEventArgs(MouseButtons.Right, 1, -1, 200, 0) });
                var menu = (ContextMenuStrip) typeof(LauncherForm).GetField("_entryContextMenu", flags).GetValue(lf);
                Assert(menu.Items.Count >= 8 && menu.Items[0].Image != null,
                       "Overflow actions must remain available with packaged icons");
                menu.Close();
                for (int i = 0; i < 16; i++)
                    Assert(svc.CreateCopy(Path.Combine(_tempRoot, "ui_demo", "More", "Game" + i + ".exe"),
                                          "Game" + i + ".exe")
                               .Ok,
                           "Create scrolling demo entry");
                typeof(LauncherForm).GetMethod("ReloadLibraryView", flags).Invoke(lf, null);
                var list = (LibraryListBox) typeof(LauncherForm).GetField("_listLibrary", flags).GetValue(lf);
                var scroll = (LibraryScrollBar) typeof(LauncherForm).GetField("_libraryScrollBar", flags).GetValue(lf);
                var more = (AnimatedButton) typeof(LauncherForm).GetField("_btnMore", flags).GetValue(lf);
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    lf.ApplyLayout(dpi);
                    Application.DoEvents();
                    Assert(scroll.Visible && scroll.ThumbBounds.Height < scroll.Height,
                           "Long list must show a scrollable thumb");
                    list.TopIndex = 0;
                    SendMessage(list.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                    if (SystemInformation.MouseWheelScrollLines != 0)
                        Assert(list.TopIndex > 0, "Mouse wheel must scroll with the native scrollbar hidden");
                    list.TopIndex = 5;
                    Assert(scroll.ThumbBounds.Top > 2, "Thumb must follow keyboard/programmatic scrolling");
                    var mouseFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    typeof(LibraryScrollBar)
                        .GetMethod("OnMouseDown", mouseFlags)
                        .Invoke(scroll, new object[] { new MouseEventArgs(MouseButtons.Left, 1, scroll.Width / 2,
                                                                          scroll.Height - 2, 0) });
                    typeof(LibraryScrollBar)
                        .GetMethod("OnMouseUp", mouseFlags)
                        .Invoke(scroll, new object[] { new MouseEventArgs(MouseButtons.Left, 1, scroll.Width / 2,
                                                                          scroll.Height - 2, 0) });
                    Assert(list.TopIndex > 5, "Dragging/clicking scrollbar must move the list");
                    list.TopIndex = 0;
                    more.PerformClick();
                    Application.DoEvents();
                    Point anchor = more.PointToScreen(new Point(more.Width, 0));
                    bool bulk = false;
                    foreach (ToolStripItem item in menu.Items)
                        if (item.Text == "Обновить все")
                            bulk = true;
                    Assert(bulk, "Overflow must contain bulk update action");
                    using (var bmp = new Bitmap(lf.Width, lf.Height)) using (var popup =
                                                                                 new Bitmap(menu.Width, menu.Height))
                    {
                        lf.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        menu.DrawToBitmap(popup, new Rectangle(0, 0, popup.Width, popup.Height));
                        using (var graphics = Graphics.FromImage(bmp))
                            graphics.DrawImageUnscaled(popup, menu.Left - lf.Left, menu.Top - lf.Top);
                        bmp.Save(Path.Combine(shotDir, "library_menu_" + dpi + ".png"));
                    }
                    Rectangle workingArea = Screen.FromPoint(anchor).WorkingArea;
                    int expectedRight = Math.Min(workingArea.Right, anchor.X);
                    Assert(Math.Abs(menu.Right - expectedRight) <= 3 &&
                               menu.Bottom <= Math.Min(anchor.Y, workingArea.Bottom) + 3,
                           "Overflow must stay anchored and within screen bounds at DPI " + dpi);
                    menu.Close();
                    Application.DoEvents();
                    Assert((GetWindowLong(list.Handle, -16) & 0x00200000) == 0,
                           "Native white scrollbar must remain hidden");
                    using (var bmp = new Bitmap(lf.Width, lf.Height))
                    {
                        lf.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(Path.Combine(shotDir, "library_scroll_" + dpi + ".png"));
                    }
                }
                lf.Hide();
            }

            DataPaths.OverrideDir = previousDataDir;
            using (var df = new DummyForm())
            {
                df.Opacity = 1.0;
                df.CreateControl();
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    df.ApplyLayout(dpi);
                    float scale = dpi / 96f;
                    Assert(df.ClientSize.Width == (int)Math.Round(480 * scale),
                           "DummyForm width at DPI " + dpi + " mismatch");
                    Assert(df.ClientSize.Height == (int)Math.Round(250 * scale),
                           "DummyForm height at DPI " + dpi + " mismatch");

                    using (var bmp = new System.Drawing.Bitmap(df.Width, df.Height))
                    {
                        df.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(Path.Combine(shotDir, "dummy_dpi_" + dpi + ".png"),
                                 System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        private static void TestBulkUpdateCopies()
        {
            string dir = Path.Combine(_tempRoot, "bulk-update");
            Directory.CreateDirectory(dir);
            var library = new Library(Path.Combine(dir, "QuestLauncher.library"));
            var service = new CopyService(library, _builtExe, AppInfo.Version);
            byte[] legacy =
                LoadVerifiedRealReleaseFixture(RealReleaseV13Resource, RealReleaseV13Size, RealReleaseV13Sha256);
            foreach (string name in new[] { "Old.exe", "Locked.exe", "Modified.exe", "Missing.exe" })
                File.WriteAllBytes(Path.Combine(dir, name), legacy);
            var found = service.ScanForCopies(dir, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                                              CancellationToken.None, null);
            Assert(service.ImportSelected(found) == 4, "Import all bulk-test candidates");
            Assert(service.CreateCopy(Path.Combine(dir, "Current.exe"), "Current.exe").Ok, "Create current version");
            File.WriteAllText(Path.Combine(dir, "Modified.exe"), "FOREIGN FILE");
            string foreignHash = CopyIdentity.Sha256(File.ReadAllBytes(Path.Combine(dir, "Modified.exe")));
            File.Delete(Path.Combine(dir, "Missing.exe"));
            using (var locked =
                       new FileStream(Path.Combine(dir, "Locked.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BulkUpdateResult result = service.UpdateOutdatedCopies();
                Assert(result.Updated == 1 && result.Skipped == 4 && result.Errors.Count == 0,
                       "Bulk update must update only the free outdated copy");
            }
            Assert(CopyIdentity.Sha256(File.ReadAllBytes(Path.Combine(dir, "Modified.exe"))) == foreignHash,
                   "Bulk update must preserve modified files");
            Assert(!File.Exists(Path.Combine(dir, "Missing.exe")), "Bulk update must not recreate absent copies");
            BulkUpdateResult retry = service.UpdateOutdatedCopies();
            Assert(retry.Updated == 1 && retry.Skipped == 4 && retry.Errors.Count == 0,
                   "Released locked copy must update on the next run");
            BulkUpdateResult repeat = service.UpdateOutdatedCopies();
            Assert(repeat.Updated == 0 && repeat.Errors.Count == 0,
                   "Repeating bulk update must not rewrite current copies");
        }

        private static void TestImportDialogSelection()
        {
            var candidates = new List<ImportCandidate> {
                new ImportCandidate { Name = "First.exe", Path = Path.Combine(_tempRoot, "First.exe"),
                                      Kind = FileKind.LegacyKnown, Selected = true },
                new ImportCandidate { Name = "Second.exe", Path = Path.Combine(_tempRoot, "Second.exe"),
                                      Kind = FileKind.OwnCopy, Selected = true },
                new ImportCandidate { Name = "Unknown.exe", Path = Path.Combine(_tempRoot, "Unknown.exe"),
                                      Kind = FileKind.LegacyLikely, Selected = false }
            };

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            using (var dialog = new ImportDialog(candidates, _tempRoot))
            {
                var list = (CheckedListBox) typeof(ImportDialog).GetField("_listBox", flags).GetValue(dialog);
                var import = (AnimatedButton) typeof(ImportDialog).GetField("_btnImport", flags).GetValue(dialog);
                var clear = (AnimatedButton) typeof(ImportDialog).GetField("_btnSelectNone", flags).GetValue(dialog);
                var verified =
                    (AnimatedButton) typeof(ImportDialog).GetField("_btnSelectVerified", flags).GetValue(dialog);

                Assert(!dialog.IsHandleCreated, "Construction must not require a window handle");
                Assert(import.Enabled && import.Text == "Импорт (2)", "Initial selected count is incorrect");
                list.SetItemChecked(0, false);
                Assert(import.Text == "Импорт (1)" && !candidates[0].Selected,
                       "Selection must update synchronously before showing");

                dialog.ShowInTaskbar = false;
                dialog.Show();
                Application.DoEvents();
                clear.PerformClick();
                Assert(!import.Enabled && import.Text == "Импорт (0)" && list.CheckedItems.Count == 0,
                       "Clearing selection must disable import");
                verified.PerformClick();
                Assert(import.Enabled && import.Text == "Импорт (2)" && list.CheckedItems.Count == 2,
                       "Verified selection must select only verified candidates");
                list.SetItemChecked(2, true);
                Assert(import.Text == "Импорт (3)" && candidates[2].Selected,
                       "Checking a row must immediately update the count");
                list.SetItemChecked(2, false);
                Assert(import.Text == "Импорт (2)" && !candidates[2].Selected,
                       "Unchecking a row must immediately update the count");
                dialog.Close();
                Application.DoEvents();
            }

            using (var empty = new ImportDialog(new List<ImportCandidate>(), _tempRoot))
            {
                var import = (AnimatedButton) typeof(ImportDialog).GetField("_btnImport", flags).GetValue(empty);
                Assert(!import.Enabled && import.Text == "Импорт (0)", "An empty import must be disabled");
            }
        }

        private static void TestMemoryAndHandleMeasurement()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long managedBefore = GC.GetTotalMemory(true);
            int handlesBefore;
            long privateBefore;
            using (Process self = Process.GetCurrentProcess())
            {
                handlesBefore = self.HandleCount;
                privateBefore = self.PrivateMemorySize64;
            }

            for (int i = 0; i < 15; i++)
            {
                using (var f = new DummyForm())
                {
                    f.CreateControl();
                    f.ApplyLayout(144);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long managedAfter = GC.GetTotalMemory(true);
            int handlesAfter;
            long privateAfter;
            using (Process self = Process.GetCurrentProcess())
            {
                handlesAfter = self.HandleCount;
                privateAfter = self.PrivateMemorySize64;
            }

            Console.WriteLine(string.Format(
                "   [Метрики памяти] Managed: {0} КБ -> {1} КБ | PrivateBytes: {2} КБ -> {3} КБ | Handles: {4} -> {5}",
                managedBefore / 1024, managedAfter / 1024, privateBefore / 1024, privateAfter / 1024, handlesBefore,
                handlesAfter));

            Assert(handlesAfter - handlesBefore < 40, "Handle leak detected: delta=" + (handlesAfter - handlesBefore));
        }

        private static void CleanupTempDir(string dir)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }
                catch
                {
                }
            }
            Directory.Delete(dir, true);
        }
    }
}
