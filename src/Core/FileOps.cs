using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MinimalQuestLauncher
{
    internal enum FileOpStatus
    {
        Ok,

        Busy,
        AccessDenied,

        Modified,
        NotFound,

        Exists,
        Failed
    }

    internal sealed class FileOpResult
    {
        public FileOpStatus Status;
        public string Message;
        public string Sha256;

        public bool Ok
        {
            get {
                return Status == FileOpStatus.Ok;
            }
        }

        public static FileOpResult Success(string sha)
        {
            return new FileOpResult { Status = FileOpStatus.Ok, Sha256 = sha };
        }

        public static FileOpResult Fail(FileOpStatus s, string msg)
        {
            return new FileOpResult { Status = s, Message = msg };
        }
    }

    internal static class FileOps
    {
        private const int ERROR_SHARING_VIOLATION = 32;
        private const int ERROR_LOCK_VIOLATION = 33;
        private const int ERROR_FILE_EXISTS = 80;
        private const int ERROR_ALREADY_EXISTS = 183;
        private const int ERROR_DISK_FULL = 112;
        private const int ERROR_HANDLE_DISK_FULL = 39;
        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_PATH_NOT_FOUND = 3;

        private const uint GENERIC_READ = 0x80000000;
        private const uint DELETE_ACCESS = 0x00010000;
        private const uint FILE_SHARE_NONE = 0;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const int FileDispositionInfoClass = 4;

        internal static Action<FileStream, string> MidWriteTestHook;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool DeleteFile;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
                                                         IntPtr lpSecurityAttributes, uint dwCreationDisposition,
                                                         uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle hFile, int FileInformationClass,
                                                              ref FILE_DISPOSITION_INFO lpFileInformation,
                                                              uint dwBufferSize);

        public static int Win32Code(Exception ex)
        {
            return Marshal.GetHRForException(ex) & 0xFFFF;
        }

        public static FileOpResult Classify(Exception ex, string path, string action)
        {
            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                return FileOpResult.Fail(
                    FileOpStatus.AccessDenied,
                    "Нет прав на " + action + ": " + path +
                        "\nФайл может быть только для чтения, или папка защищена (например, Program Files).");
            if (ex is FileNotFoundException)
                return FileOpResult.Fail(FileOpStatus.NotFound, "Файл не найден: " + path);
            if (ex is DirectoryNotFoundException)
                return FileOpResult.Fail(FileOpStatus.NotFound, "Папка не найдена: " + Path.GetDirectoryName(path));
            if (ex is PathTooLongException)
                return FileOpResult.Fail(FileOpStatus.Failed, "Слишком длинный путь: " + path);
            if (ex is IOException)
            {
                int code = Win32Code(ex);
                if (code == ERROR_SHARING_VIOLATION || code == ERROR_LOCK_VIOLATION)
                    return FileOpResult.Fail(FileOpStatus.Busy,
                                             "Файл занят другим процессом (вероятно, окно-заглушка ещё открыто):\n" +
                                                 path + "\nЗакройте его и повторите.");
                if (code == ERROR_FILE_EXISTS || code == ERROR_ALREADY_EXISTS)
                    return FileOpResult.Fail(FileOpStatus.Exists, "Файл уже существует: " + path);
                if (code == ERROR_DISK_FULL || code == ERROR_HANDLE_DISK_FULL)
                    return FileOpResult.Fail(FileOpStatus.Failed, "Недостаточно места на диске: " + path);
            }
            return FileOpResult.Fail(FileOpStatus.Failed,
                                     "Не удалось выполнить " + action + ": " + path + "\n" + ex.Message);
        }

        private static FileOpResult ClassifyWin32Error(int err, string path, string action)
        {
            if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND)
                return FileOpResult.Fail(FileOpStatus.NotFound, "Файл не найден: " + path);
            if (err == ERROR_SHARING_VIOLATION || err == ERROR_LOCK_VIOLATION)
                return FileOpResult.Fail(FileOpStatus.Busy,
                                         "Файл занят другим процессом (вероятно, окно-заглушка ещё открыто):\n" + path +
                                             "\nЗакройте его и повторите.");
            if (err == ERROR_ACCESS_DENIED)
                return FileOpResult.Fail(FileOpStatus.AccessDenied,
                                         "Нет прав на " + action + ": " + path +
                                             "\nФайл может быть только для чтения или защищён правами доступа.");
            return FileOpResult.Fail(FileOpStatus.Failed,
                                     "Не удалось выполнить " + action + " (код Win32 " + err + "): " + path);
        }

        private static string TempNameFor(string target, string tag)
        {
            string dir = Path.GetDirectoryName(target);
            string name =
                "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + "." + tag;
            return Path.Combine(dir, name);
        }

        public static string GetBackupPath(string target)
        {
            return target + ".qlbak";
        }

        public static void CommitBackup(string target)
        {
            TryDelete(GetBackupPath(target));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Не удалось удалить временный файл " + path + ": " + ex.Message);
            }
        }

        public static bool RecoverIfInterrupted(string target, string expectedSha = null,
                                                string trustedBuildHash = null)
        {
            if (string.IsNullOrEmpty(target))
                return false;
            string backupPath = GetBackupPath(target);
            if (!File.Exists(backupPath))
                return false;
            if (!File.Exists(target))
                return false;

            try
            {
                byte[] bakBytes;
                using (var bakStream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    bakBytes = ReadStreamAllBytes(bakStream, CopyIdentity.MaxInspectSize);
                }

                if (bakBytes == null || bakBytes.Length == 0)
                    return false;
                FileInspection bakFi = CopyIdentity.InspectBytes(target, bakBytes, null, trustedBuildHash);
                if (!bakFi.IsVerifiedOwnedCopy)
                    return false;

                byte[] targetBytes;
                using (var targetStream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    if (targetStream.Length <= 0 || targetStream.Length > CopyIdentity.MaxInspectSize)
                        return false;
                    targetBytes = ReadStreamAllBytes(targetStream, CopyIdentity.MaxInspectSize);
                }

                string bakSha = bakFi.Sha256;
                string targetSha = CopyIdentity.Sha256(targetBytes);
                FileInspection targetFi = CopyIdentity.InspectBytes(target, targetBytes, null, trustedBuildHash);

                if (targetFi.IsVerifiedOwnedCopy)
                {
                    bool matchesExpected =
                        string.IsNullOrEmpty(expectedSha)
                            ? string.Equals(targetSha, bakSha, StringComparison.OrdinalIgnoreCase)
                            : string.Equals(targetSha, expectedSha, StringComparison.OrdinalIgnoreCase);

                    if (matchesExpected)
                    {
                        TryDelete(backupPath);
                        Log.Info("Удалена остаточная резервная копия .qlbak (целевой файл цел и согласован): " +
                                 target);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warn("Проверка остаточной резервной копии .qlbak для " + target + ": " + ex.Message);
                return false;
            }
        }

        public static bool InspectPendingBackup(string target, string expectedSha, string trustedBuildHash,
                                                out FileInspection backupInspection)
        {
            backupInspection = null;
            if (string.IsNullOrEmpty(target))
                return false;
            string backupPath = GetBackupPath(target);
            if (!File.Exists(backupPath))
                return false;

            try
            {
                byte[] bakBytes;
                using (var bakStream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bakBytes = ReadStreamAllBytes(bakStream, CopyIdentity.MaxInspectSize);
                }
                if (bakBytes == null || bakBytes.Length == 0)
                    return false;

                FileInspection fi = CopyIdentity.InspectBytes(target, bakBytes, null, trustedBuildHash);
                if (!fi.IsVerifiedOwnedCopy)
                    return false;
                if (!string.IsNullOrEmpty(expectedSha) &&
                    !string.Equals(fi.Sha256, expectedSha, StringComparison.OrdinalIgnoreCase))
                    return false;

                backupInspection = fi;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static FileOpResult RestoreFromBackupConfirmed(string target, string expectedSha,
                                                              string trustedBuildHash = null,
                                                              bool archiveCurrentTarget = true)
        {
            if (string.IsNullOrEmpty(target))
                return FileOpResult.Fail(FileOpStatus.Failed, "Не указан путь к файлу.");
            if (string.IsNullOrEmpty(expectedSha))
                return FileOpResult.Fail(FileOpStatus.Modified, "Отсутствует ожидаемый хеш записи реестра.");

            string backupPath = GetBackupPath(target);
            if (!File.Exists(backupPath))
                return FileOpResult.Fail(FileOpStatus.NotFound, "Резервная копия не найдена:\n" + backupPath);

            try
            {
                byte[] bakBytes;
                using (var bakStream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    bakBytes = ReadStreamAllBytes(bakStream, CopyIdentity.MaxInspectSize);
                }

                string bakSha = CopyIdentity.Sha256(bakBytes);
                if (!string.Equals(bakSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    return FileOpResult.Fail(FileOpStatus.Modified,
                                             "Хеш резервной копии .qlbak не совпадает с записью библиотеки:\n" +
                                                 backupPath);
                }

                FileInspection bakFi = CopyIdentity.InspectBytes(target, bakBytes, null, trustedBuildHash);
                if (!bakFi.IsVerifiedOwnedCopy)
                {
                    return FileOpResult.Fail(
                        FileOpStatus.Modified,
                        "Содержимое резервной копии .qlbak не является подтверждённой копией QuestLauncher:\n" +
                            backupPath);
                }

                using (var fs = new FileStream(target, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    if (archiveCurrentTarget && fs.Length > 0 && fs.Length <= CopyIdentity.MaxInspectSize)
                    {
                        byte[] displaced = ReadStreamAllBytes(fs, CopyIdentity.MaxInspectSize);
                        string displacedSha = CopyIdentity.Sha256(displaced);
                        if (!string.Equals(displacedSha, bakSha, StringComparison.OrdinalIgnoreCase))
                        {
                            string archivePath =
                                target + ".displaced-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
                            using (var arch =
                                       new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                arch.Write(displaced, 0, displaced.Length);
                                arch.Flush(true);
                            }
                        }
                    }

                    fs.Position = 0;
                    fs.SetLength(0);
                    fs.Write(bakBytes, 0, bakBytes.Length);
                    fs.Flush(true);

                    fs.Position = 0;
                    string restoredSha = CopyIdentity.Sha256(fs);
                    if (!string.Equals(restoredSha, bakSha, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Хеш файла после восстановления из .qlbak не совпал.");
                }

                TryDelete(backupPath);
                Log.Info("Выполнено подтверждённое восстановление из .qlbak: " + target);
                return FileOpResult.Success(bakSha);
            }
            catch (Exception ex)
            {
                return Classify(ex, target, "восстановление из резервной копии");
            }
        }

        public static FileOpResult CreateNew(string target, byte[] content)
        {
            string dir = Path.GetDirectoryName(target);
            string expected = CopyIdentity.Sha256(content);
            try
            {
                if (File.Exists(target) || Directory.Exists(target))
                    return FileOpResult.Fail(FileOpStatus.Exists, "Файл уже существует: " + target);
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                return Classify(ex, dir, "создание папки");
            }

            string temp = TempNameFor(target, "qltmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    fs.Write(content, 0, content.Length);
                    fs.Flush(true);
                    fs.Position = 0;
                    string written = CopyIdentity.Sha256(fs);
                    if (!string.Equals(written, expected, StringComparison.OrdinalIgnoreCase))
                        return FileOpResult.Fail(FileOpStatus.Failed,
                                                 "Проверка записанного файла не прошла (хеш не совпадает): " + target);
                }

                File.Move(temp, target);
                temp = null;
                return FileOpResult.Success(expected);
            }
            catch (Exception ex)
            {
                return Classify(ex, target, "создание файла");
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static byte[] ReadStreamAllBytes(Stream fs, long maxSize)
        {
            if (fs.Length > maxSize)
                throw new IOException("Файл слишком большой (" + fs.Length + " байт).");
            fs.Position = 0;
            var buf = new byte[fs.Length];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0)
                    throw new IOException("Неожиданный конец файла при чтении.");
                read += n;
            }
            return buf;
        }

        public static FileOpResult ReplaceOwned(string target, string expectedSha, byte[] newContent,
                                                string trustedBuildHash = null, bool keepBackupUntilCommit = false)
        {
            if (string.IsNullOrEmpty(expectedSha))
                return FileOpResult.Fail(FileOpStatus.Modified,
                                         "Нет сохранённого хеша — принадлежность файла не подтверждена.");
            if (newContent == null || newContent.Length == 0)
                return FileOpResult.Fail(FileOpStatus.Failed, "Пустое содержимое обновления.");

            string backup = GetBackupPath(target);
            if (File.Exists(backup))
            {

                if (!RecoverIfInterrupted(target, expectedSha, trustedBuildHash) && File.Exists(backup))
                {
                    return FileOpResult.Fail(
                        FileOpStatus.Modified,
                        "Обнаружена незавершённая резервная копия (.qlbak), а целевой файл отличается от ожидаемого:\n" +
                            target +
                            ("\nОба файла сохранены. Выполните явное восстановление из резервной копии перед " +
                             "обновлением."));
                }
            }

            string expectedNewSha = CopyIdentity.Sha256(newContent);
            bool backupCreated = false;

            try
            {
                if (!File.Exists(target))
                    return FileOpResult.Fail(FileOpStatus.NotFound, "Файл не найден: " + target);

                using (var fs = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] oldBytes = ReadStreamAllBytes(fs, CopyIdentity.MaxInspectSize);
                    string actualSha = CopyIdentity.Sha256(oldBytes);
                    if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        return FileOpResult.Fail(
                            FileOpStatus.Modified,
                            "Файл был изменён или подменён после создания и больше не совпадает с записью библиотеки:\n" +
                                target + "\nОн не будет изменён.");
                    }

                    FileInspection fi = CopyIdentity.InspectBytes(target, oldBytes, null, trustedBuildHash);
                    if (!fi.IsVerifiedOwnedCopy)
                    {
                        return FileOpResult.Fail(
                            FileOpStatus.Modified,
                            "Файл не является криптографически подтверждённой копией QuestLauncher (" +
                                CopyIdentity.Describe(fi.Kind) + "):\n" + target +
                                "\nАвтоматическая замена запрещена.");
                    }

                    using (var bakStream =
                               new FileStream(backup, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        bakStream.Write(oldBytes, 0, oldBytes.Length);
                        bakStream.Flush(true);
                        bakStream.Position = 0;
                        string bakSha = CopyIdentity.Sha256(bakStream);
                        if (!string.Equals(bakSha, actualSha, StringComparison.OrdinalIgnoreCase))
                            throw new IOException(
                                "Не удалось подтвердить целостность резервной копии .qlbak перед обновлением.");
                    }
                    backupCreated = true;

                    try
                    {
                        fs.Position = 0;
                        fs.SetLength(0);

                        if (MidWriteTestHook != null)
                        {
                            int partial = Math.Min(1024, newContent.Length);
                            fs.Write(newContent, 0, partial);
                            fs.Flush(true);
                            MidWriteTestHook(fs, target);
                            fs.Position = 0;
                            fs.SetLength(0);
                        }

                        fs.Write(newContent, 0, newContent.Length);
                        fs.Flush(true);

                        fs.Position = 0;
                        string writtenSha = CopyIdentity.Sha256(fs);
                        if (!string.Equals(writtenSha, expectedNewSha, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Контрольная сумма после записи не совпала.");
                    }
                    catch (Exception writeEx)
                    {

                        try
                        {
                            fs.Position = 0;
                            fs.SetLength(0);
                            fs.Write(oldBytes, 0, oldBytes.Length);
                            fs.Flush(true);
                            fs.Position = 0;
                            string rollbackSha = CopyIdentity.Sha256(fs);
                            if (string.Equals(rollbackSha, actualSha, StringComparison.OrdinalIgnoreCase))
                            {
                                TryDelete(backup);
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            Log.Error("Откат замены внутри дескриптора " + target +
                                          " (резервная копия .qlbak сохранена для восстановления)",
                                      rollbackEx);
                        }
                        return Classify(writeEx, target, "обновление файла");
                    }
                }

                if (!keepBackupUntilCommit)
                {
                    CommitBackup(target);
                }
                return FileOpResult.Success(expectedNewSha);
            }
            catch (Exception ex)
            {
                if (!backupCreated)
                {
                    TryDelete(backup);
                }
                return Classify(ex, target, "изменение файла");
            }
        }

        public static FileOpResult DeleteOwned(string target, string expectedSha, string trustedBuildHash = null)
        {
            if (string.IsNullOrEmpty(expectedSha))
                return FileOpResult.Fail(FileOpStatus.Modified,
                                         "Нет сохранённого хеша — принадлежность файла не подтверждена.");

            string backup = GetBackupPath(target);
            if (File.Exists(backup))
            {
                if (!RecoverIfInterrupted(target, expectedSha, trustedBuildHash) && File.Exists(backup))
                {
                    return FileOpResult.Fail(
                        FileOpStatus.Modified,
                        "Обнаружена незавершённая резервная копия (.qlbak), а целевой файл отличается от ожидаемого:\n" +
                            target + "\nУдаление файла с диска заблокировано. Оба файла сохранены.");
                }
            }

            SafeFileHandle handle = CreateFileW(target, GENERIC_READ | DELETE_ACCESS, FILE_SHARE_NONE, IntPtr.Zero,
                                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                handle.Dispose();
                return ClassifyWin32Error(err, target, "удаление файла");
            }

            try
            {
                string actualSha;
                using (var fs = new FileStream(handle, FileAccess.Read, 4096, false))
                {
                    byte[] bytes = ReadStreamAllBytes(fs, CopyIdentity.MaxInspectSize);
                    actualSha = CopyIdentity.Sha256(bytes);
                    if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        return FileOpResult.Fail(
                            FileOpStatus.Modified,
                            "Файл был изменён или подменён после создания и больше не совпадает с записью библиотеки:\n" +
                                target + "\nОн не будет удалён.");
                    }

                    FileInspection fi = CopyIdentity.InspectBytes(target, bytes, null, trustedBuildHash);
                    if (!fi.IsVerifiedOwnedCopy)
                    {
                        return FileOpResult.Fail(
                            FileOpStatus.Modified,
                            "Файл не является криптографически подтверждённой копией QuestLauncher (" +
                                CopyIdentity.Describe(fi.Kind) + "):\n" + target +
                                "\nУдаление файла с диска запрещено.");
                    }

                    var disp = new FILE_DISPOSITION_INFO { DeleteFile = true };
                    if (!SetFileInformationByHandle(handle, FileDispositionInfoClass, ref disp,
                                                    (uint)Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO))))
                    {
                        int err = Marshal.GetLastWin32Error();
                        return ClassifyWin32Error(err, target, "удаление файла");
                    }
                }
                return FileOpResult.Success(actualSha);
            }
            catch (Exception ex)
            {
                return Classify(ex, target, "удаление файла");
            }
            finally
            {
                if (!handle.IsClosed)
                    handle.Dispose();
            }
        }

        public static bool IsBusy(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return false;
            }
            catch (IOException ex)
            {
                int code = Win32Code(ex);
                return code == ERROR_SHARING_VIOLATION || code == ERROR_LOCK_VIOLATION;
            }
            catch
            {
                return false;
            }
        }
    }
}
