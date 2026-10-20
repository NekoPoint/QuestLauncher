using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MinimalQuestLauncher
{
    internal sealed class BulkUpdateResult
    {
        public int Updated;
        public int Skipped;
        public readonly List<string> Errors = new List<string>();
    }
    internal enum EntryStatus
    {

        Ready,

        Outdated,

        Unverified,

        RecoveryPending,

        Missing,

        Modified,

        Unreadable
    }

    internal sealed class EntryView
    {
        public LibraryEntry Entry;
        public EntryStatus Status;
        public int RunningCount;
        public string StatusText;
    }

    internal sealed class CopyServiceResult
    {
        public bool Ok;
        public string Message;
        public LibraryEntry Entry;
        public FileOpStatus OpStatus;

        public static CopyServiceResult Success(LibraryEntry e, string msg)
        {
            return new CopyServiceResult { Ok = true, Entry = e, Message = msg, OpStatus = FileOpStatus.Ok };
        }

        public static CopyServiceResult Fail(string msg, FileOpStatus status)
        {
            return new CopyServiceResult { Ok = false, Message = msg, OpStatus = status };
        }
    }

    internal sealed class ImportCandidate
    {
        public string Path;
        public string Name;
        public FileKind Kind;
        public string Version;
        public string Sha256;
        public string BuildHash;
        public string CopyId;
        public long Size;
        public DateTime LastWriteUtc;
        public bool Selected;

        public string KindDescription
        {
            get {
                if (Kind == FileKind.OwnCopy)
                    return "Копия QuestLauncher " + (Version ?? "1.4+");
                if (Kind == FileKind.LegacyKnown)
                    return "Старая копия (сборка " + Version + ")";
                if (Kind == FileKind.LegacyLikely)
                    return "Неподтверждённый кандидат (по сигнатуре, без права замены/удаления)";
                return CopyIdentity.Describe(Kind);
            }
        }
    }

    internal sealed class CopyService
    {
        private readonly Library _library;
        private readonly string _launcherExePath;
        private byte[] _launcherBytes;
        private string _launcherBuildHash;
        private readonly string _launcherVersion;

        public CopyService(Library library, string launcherExePath, string launcherVersion)
        {
            _library = library;
            _launcherExePath = PathResolver.Normalize(launcherExePath);
            _launcherVersion = launcherVersion ?? AppInfo.Version;
        }

        public Library Library
        {
            get {
                return _library;
            }
        }
        public string LauncherExePath
        {
            get {
                return _launcherExePath;
            }
        }
        public string LauncherVersion
        {
            get {
                return _launcherVersion;
            }
        }

        private void EnsureLauncherLoaded()
        {
            if (_launcherBytes != null)
                return;
            byte[] raw = CopyIdentity.ReadAllBytesShared(_launcherExePath, CopyIdentity.MaxInspectSize);
            List<int> markers = CopyIdentity.FindMarkers(raw);
            if (markers.Count != 1)
                throw new InvalidOperationException("Исполняемый файл лаунчера не содержит корректного маркера роли (" +
                                                    markers.Count + ").");
            _launcherBytes = raw;
            _launcherBuildHash = CopyIdentity.ComputeBuildHash(raw, markers);
        }

        private string TryGetBuildHash()
        {
            try
            {
                return CurrentBuildHash;
            }
            catch
            {
                return null;
            }
        }

        public string CurrentBuildHash
        {
            get {
                EnsureLauncherLoaded();
                return _launcherBuildHash;
            }
        }

        public EntryView Evaluate(LibraryEntry e, Dictionary<string, List<int>> runningMap)
        {
            var v = new EntryView { Entry = e, RunningCount = ProcessTools.CountFor(runningMap, e.Path) };

            string curBuild = TryGetBuildHash();
            if (!string.IsNullOrEmpty(e.Path))
            {
                string fullPath = PathResolver.Normalize(e.Path);
                string bakPath = FileOps.GetBackupPath(fullPath);
                if (File.Exists(bakPath))
                {
                    FileOps.RecoverIfInterrupted(fullPath, e.Sha256, curBuild);
                    if (File.Exists(bakPath))
                    {
                        FileInspection bakFi;
                        if (FileOps.InspectPendingBackup(fullPath, e.Sha256, curBuild, out bakFi))
                        {
                            FileInspection curFi = CopyIdentity.Inspect(fullPath, curBuild);
                            string expectedCopyId = (e.Id != null && e.Id.Length == 32) ? NormalizeHexId(e.Id) : null;

                            if (curFi.Kind == FileKind.OwnCopy && curFi.IsVerifiedOwnedCopy &&
                                !string.IsNullOrEmpty(curBuild) &&
                                string.Equals(curFi.BuildHash, curBuild, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(expectedCopyId) &&
                                string.Equals(curFi.CopyId, expectedCopyId, StringComparison.OrdinalIgnoreCase))
                            {
                                e.Version = _launcherVersion;
                                e.Sha256 = curFi.Sha256;
                                e.BuildHash = curFi.BuildHash;
                                e.Size = curFi.Size;
                                _library.Update(list =>
                                                {
                                                    LibraryEntry t = Library.FindByPath(list, fullPath);
                                                    if (t != null)
                                                    {
                                                        t.Version = _launcherVersion;
                                                        t.Sha256 = curFi.Sha256;
                                                        t.BuildHash = curFi.BuildHash;
                                                        t.Size = curFi.Size;
                                                    }
                                                });
                                FileOps.CommitBackup(fullPath);
                                Log.Info("Согласована запись библиотеки после прерванного сохранения: " + fullPath);
                            }
                            else
                            {

                                v.Status = EntryStatus.RecoveryPending;
                                v.StatusText = "Требуется подтверждение восстановления (.qlbak)";
                                return v;
                            }
                        }
                    }
                }
            }
            FileInspection fi = CopyIdentity.Inspect(e.Path, curBuild);
            if (fi.Kind == FileKind.Missing)
            {
                v.Status = EntryStatus.Missing;
                v.StatusText = "Файл отсутствует";
                return v;
            }
            if (fi.Kind == FileKind.Unreadable)
            {
                v.Status = EntryStatus.Unreadable;
                v.StatusText = "Недоступен: " + fi.Error;
                return v;
            }

            if (string.IsNullOrEmpty(e.Sha256) ||
                !string.Equals(fi.Sha256, e.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                v.Status = EntryStatus.Modified;
                v.StatusText = "Изменён извне (заблокирован)";
                return v;
            }

            if (fi.Kind == FileKind.LegacyLikely)
            {
                v.Status = EntryStatus.Unverified;
                v.StatusText = (v.RunningCount > 0 ? "Запущено • " : "") + "Неподтверждена (только запуск)";
                return v;
            }

            if (!fi.IsVerifiedOwnedCopy)
            {
                v.Status = EntryStatus.Modified;
                v.StatusText = "Посторонний файл";
                return v;
            }

            bool sameBuild = !string.IsNullOrEmpty(curBuild) && !string.IsNullOrEmpty(fi.BuildHash) &&
                             string.Equals(curBuild, fi.BuildHash, StringComparison.OrdinalIgnoreCase);

            if (sameBuild)
            {
                v.Status = EntryStatus.Ready;
                v.StatusText = v.RunningCount > 0 ? "Запущено (" + v.RunningCount + ")"
                                                  : "Готово • v" + (e.Version ?? _launcherVersion);
            }
            else
            {
                v.Status = EntryStatus.Outdated;
                string ver = string.IsNullOrEmpty(e.Version) ? (fi.Version ?? "старая") : e.Version;
                v.StatusText =
                    (v.RunningCount > 0 ? "Запущено • " : "") + "Устарела (" + ver + " → " + _launcherVersion + ")";
            }
            return v;
        }

        public CopyServiceResult CreateCopy(string targetPath, string displayName)
        {
            string full = PathResolver.Normalize(targetPath);
            if (PathResolver.SamePath(full, _launcherExePath))
                return CopyServiceResult.Fail("Нельзя создать копию поверх самого исполняемого файла лаунчера.",
                                              FileOpStatus.Failed);

            if (File.Exists(full) || Directory.Exists(full))
            {
                FileInspection existing = CopyIdentity.Inspect(full, TryGetBuildHash());
                return CopyServiceResult.Fail("По указанному пути уже существует файл (" +
                                                  CopyIdentity.Describe(existing.Kind) + "):\n" + full +
                                                  "\nПерезапись существующих файлов при создании запрещена.",
                                              FileOpStatus.Exists);
            }

            try
            {
                EnsureLauncherLoaded();
            }
            catch (Exception ex)
            {
                Log.Error("Подготовка образа лаунчера", ex);
                return CopyServiceResult.Fail("Ошибка чтения образа лаунчера: " + ex.Message, FileOpStatus.Failed);
            }

            string id = CopyIdentity.NewCopyId();
            byte[] copyBytes;
            try
            {
                copyBytes = CopyIdentity.BuildCopyBytes(_launcherBytes, id);
            }
            catch (Exception ex)
            {
                Log.Error("Формирование маркера копии", ex);
                return CopyServiceResult.Fail(ex.Message, FileOpStatus.Failed);
            }

            FileOpResult op = FileOps.CreateNew(full, copyBytes);
            if (!op.Ok)
            {
                Log.Warn("Создание копии не удалось (" + op.Status + "): " + op.Message);
                return CopyServiceResult.Fail(op.Message, op.Status);
            }

            var entry =
                new LibraryEntry { Id = id,
                                   Name = string.IsNullOrEmpty(displayName) ? Path.GetFileName(full) : displayName,
                                   Path = full,
                                   Version = _launcherVersion,
                                   Sha256 = op.Sha256,
                                   BuildHash = _launcherBuildHash,
                                   Size = copyBytes.Length,
                                   CreatedUtc = DateTime.UtcNow,
                                   LastLaunchUtc = DateTime.MinValue,
                                   LaunchCount = 0,
                                   Favorite = false,
                                   Origin = "created" };

            _library.Update(list =>
                            {
                                LibraryEntry old = Library.FindByPath(list, full);
                                if (old != null)
                                {
                                    entry.Favorite = old.Favorite;
                                    entry.LaunchCount = old.LaunchCount;
                                    list.Remove(old);
                                }
                                list.Add(entry);
                            });

            Log.Info("Создана копия: " + full + " (id=" + id + ")");
            return CopyServiceResult.Success(entry, "Копия создана.");
        }

        public BulkUpdateResult UpdateOutdatedCopies()
        {
            var result = new BulkUpdateResult();
            foreach (LibraryEntry entry in _library.Load())
            {
                try
                {
                    EntryView view = Evaluate(entry, null);
                    if (view.Status != EntryStatus.Outdated || ProcessTools.FindRunning(entry.Path).Count > 0)
                    {
                        result.Skipped++;
                        continue;
                    }
                    CopyServiceResult update = UpdateCopy(entry);
                    if (update.Ok)
                        result.Updated++;
                    else if (update.OpStatus == FileOpStatus.Busy)
                        result.Skipped++;
                    else
                        result.Errors.Add(entry.Name + ": " + update.Message);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(entry.Name + ": " + ex.Message);
                }
            }
            return result;
        }

        public CopyServiceResult UpdateCopy(LibraryEntry entry)
        {
            if (entry == null)
                return CopyServiceResult.Fail("Запись не выбрана.", FileOpStatus.Failed);
            string full = PathResolver.Normalize(entry.Path);
            if (PathResolver.SamePath(full, _launcherExePath))
                return CopyServiceResult.Fail("Нельзя обновлять сам исполняемый файл лаунчера как копию.",
                                              FileOpStatus.Failed);

            try
            {
                EnsureLauncherLoaded();
            }
            catch (Exception ex)
            {
                Log.Error("Чтение лаунчера при обновлении", ex);
                return CopyServiceResult.Fail("Ошибка чтения образа лаунчера: " + ex.Message, FileOpStatus.Failed);
            }

            string bakPath = FileOps.GetBackupPath(full);
            if (File.Exists(bakPath))
            {
                if (!FileOps.RecoverIfInterrupted(full, entry.Sha256, _launcherBuildHash) && File.Exists(bakPath))
                {
                    return CopyServiceResult.Fail(
                        "Обнаружена незавершённая резервная копия (.qlbak), а целевой файл отличается от записи в реестре:\n" +
                            full +
                            "\n\nОба файла сохранены. Сначала выполните явное восстановление из резервной копии.",
                        FileOpStatus.Modified);
                }
            }
            FileInspection fi = CopyIdentity.Inspect(full, _launcherBuildHash);
            if (fi.Kind == FileKind.Missing)
                return CopyServiceResult.Fail("Файл не найден на диске:\n" + full, FileOpStatus.NotFound);
            if (fi.Kind == FileKind.Unreadable)
                return CopyServiceResult.Fail("Не удалось прочитать файл:\n" + full + "\n" + fi.Error,
                                              FileOpStatus.AccessDenied);
            if (!string.Equals(fi.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return CopyServiceResult.Fail(
                    "Файл на диске не совпадает с записью в реестре (был изменён или подменён):\n" + full +
                        "\nОбновление отменено в целях безопасности.",
                    FileOpStatus.Modified);
            if (fi.Kind == FileKind.LegacyLikely)
                return CopyServiceResult.Fail(
                    "Файл определён только по косвенным признакам (LegacyLikely), его хеш не входит в список подтверждённых сборок:\n" +
                        full + "\nПерезапись неподтверждённого файла запрещена.",
                    FileOpStatus.Modified);
            if (!fi.IsVerifiedOwnedCopy)
                return CopyServiceResult.Fail("Файл не является подтверждённой копией QuestLauncher:\n" + full,
                                              FileOpStatus.Modified);

            string copyId = (entry.Id != null && entry.Id.Length == 32) ? entry.Id : CopyIdentity.NewCopyId();

            copyId = NormalizeHexId(copyId);
            byte[] newBytes = CopyIdentity.BuildCopyBytes(_launcherBytes, copyId);

            FileOpResult op = FileOps.ReplaceOwned(full, entry.Sha256, newBytes, _launcherBuildHash, true);
            if (!op.Ok)
            {
                Log.Warn("Обновление копии не удалось (" + op.Status + "): " + op.Message);
                return CopyServiceResult.Fail(op.Message, op.Status);
            }

            LibraryEntry updated = null;
            _library.Update(list =>
                            {
                                LibraryEntry target = Library.FindByPath(list, full);
                                if (target == null)
                                {
                                    target = entry.Clone();
                                    list.Add(target);
                                }
                                target.Id = copyId;
                                target.Version = _launcherVersion;
                                target.Sha256 = op.Sha256;
                                target.BuildHash = _launcherBuildHash;
                                target.Size = newBytes.Length;
                                updated = target.Clone();
                            });

            FileOps.CommitBackup(full);

            Log.Info("Обновлена копия: " + full + " -> v" + _launcherVersion);
            return CopyServiceResult.Success(updated, "Копия обновлена до v" + _launcherVersion + ".");
        }

        public CopyServiceResult RestoreFromBackupConfirmed(LibraryEntry entry, bool archiveCurrentTarget = true)
        {
            if (entry == null)
                return CopyServiceResult.Fail("Запись не выбрана.", FileOpStatus.Failed);
            string full = PathResolver.Normalize(entry.Path);
            if (PathResolver.SamePath(full, _launcherExePath))
                return CopyServiceResult.Fail("Нельзя перезаписывать сам исполняемый файл лаунчера.",
                                              FileOpStatus.Failed);

            string curBuild = TryGetBuildHash();
            FileOpResult res = FileOps.RestoreFromBackupConfirmed(full, entry.Sha256, curBuild, archiveCurrentTarget);
            if (!res.Ok)
            {
                Log.Warn("Восстановление из .qlbak не удалось (" + res.Status + "): " + res.Message);
                return CopyServiceResult.Fail(res.Message, res.Status);
            }

            return CopyServiceResult.Success(entry, "Копия восстановлена из проверенной резервной копии (.qlbak).");
        }

        private static string NormalizeHexId(string id)
        {
            if (id != null && id.Length == 32)
            {
                string lower = id.ToLowerInvariant();
                bool ok = true;
                for (int i = 0; i < 32; i++)
                {
                    char c = lower[i];
                    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return lower;
            }
            return CopyIdentity.NewCopyId();
        }

        public CopyServiceResult RecreateMissing(LibraryEntry entry)
        {
            if (entry == null)
                return CopyServiceResult.Fail("Запись не выбрана.", FileOpStatus.Failed);
            string full = PathResolver.Normalize(entry.Path);
            if (File.Exists(full) || Directory.Exists(full))
                return CopyServiceResult.Fail("Файл уже существует на диске.", FileOpStatus.Exists);

            EnsureLauncherLoaded();
            string copyId = NormalizeHexId(entry.Id);
            byte[] newBytes = CopyIdentity.BuildCopyBytes(_launcherBytes, copyId);
            FileOpResult op = FileOps.CreateNew(full, newBytes);
            if (!op.Ok)
                return CopyServiceResult.Fail(op.Message, op.Status);

            LibraryEntry updated = null;
            _library.Update(list =>
                            {
                                LibraryEntry target = Library.FindByPath(list, full);
                                if (target == null)
                                {
                                    target = entry.Clone();
                                    list.Add(target);
                                }
                                target.Id = copyId;
                                target.Version = _launcherVersion;
                                target.Sha256 = op.Sha256;
                                target.BuildHash = _launcherBuildHash;
                                target.Size = newBytes.Length;
                                updated = target.Clone();
                            });
            Log.Info("Воссоздана копия: " + full);
            return CopyServiceResult.Success(updated, "Копия воссоздана.");
        }

        public CopyServiceResult DeleteCopy(LibraryEntry entry, bool deleteFileFromDisk)
        {
            if (entry == null)
                return CopyServiceResult.Fail("Запись не выбрана.", FileOpStatus.Failed);
            string full = PathResolver.Normalize(entry.Path);
            if (PathResolver.SamePath(full, _launcherExePath))
                return CopyServiceResult.Fail("Нельзя удалить сам исполняемый файл лаунчера.", FileOpStatus.Failed);

            if (deleteFileFromDisk && File.Exists(full))
            {
                string curBuild = TryGetBuildHash();
                FileInspection fi = CopyIdentity.Inspect(full, curBuild);
                if (!fi.IsVerifiedOwnedCopy)
                    return CopyServiceResult.Fail(
                        "Файл не является подтверждённой собственной копией QuestLauncher (" +
                            CopyIdentity.Describe(fi.Kind) + "):\n" + full +
                            "\nУдаление файла с диска заблокировано. Вы можете убрать только запись из списка.",
                        FileOpStatus.Modified);

                FileOpResult op = FileOps.DeleteOwned(full, entry.Sha256, curBuild);
                if (!op.Ok)
                {
                    Log.Warn("Удаление копии не удалось (" + op.Status + "): " + op.Message);
                    return CopyServiceResult.Fail(op.Message, op.Status);
                }
            }

            _library.Update(list =>
                            {
                                LibraryEntry target = Library.FindByPath(list, full);
                                if (target != null)
                                    list.Remove(target);
                            });

            Log.Info((deleteFileFromDisk ? "Удалён файл и запись: " : "Удалена запись из реестра: ") + full);
            return CopyServiceResult.Success(entry, deleteFileFromDisk ? "Файл и запись удалены."
                                                                       : "Запись убрана из библиотеки.");
        }

        public void RecordLaunch(string path)
        {
            string full = PathResolver.Normalize(path);
            _library.Update(list =>
                            {
                                LibraryEntry target = Library.FindByPath(list, full);
                                if (target != null)
                                {
                                    target.LastLaunchUtc = DateTime.UtcNow;
                                    target.LaunchCount++;
                                }
                            });
        }

        public void ToggleFavorite(string path)
        {
            string full = PathResolver.Normalize(path);
            _library.Update(list =>
                            {
                                LibraryEntry target = Library.FindByPath(list, full);
                                if (target != null)
                                    target.Favorite = !target.Favorite;
                            });
        }

        public List<ImportCandidate> ScanForCopies(string rootFolder, int maxDepth,
                                                   HashSet<string> existingPathsNormalized, CancellationToken ct,
                                                   Action<string> onProgress)
        {
            var found = new List<ImportCandidate>();
            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder))
                return found;
            string rootNorm = PathResolver.Normalize(rootFolder);
            ScanDirRecursive(rootNorm, 0, maxDepth, existingPathsNormalized, found, ct, onProgress);
            return found;
        }

        private void ScanDirRecursive(string dir, int depth, int maxDepth, HashSet<string> existing,
                                      List<ImportCandidate> found, CancellationToken ct, Action<string> onProgress)
        {
            if (ct.IsCancellationRequested || depth > maxDepth)
                return;
            if (onProgress != null)
                onProgress(dir);

            string[] files = null;
            try
            {
                files = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            if (files != null)
            {
                foreach (string f in files)
                {
                    if (ct.IsCancellationRequested)
                        return;
                    string norm = PathResolver.Normalize(f);
                    if (PathResolver.SamePath(norm, _launcherExePath))
                        continue;
                    if (existing != null && existing.Contains(norm))
                        continue;

                    string curBuild = TryGetBuildHash();
                    FileInspection fi = CopyIdentity.Inspect(norm, curBuild);

                    if (fi.Kind == FileKind.LauncherBuild)
                        continue;
                    if (fi.Kind == FileKind.LegacyKnown)
                    {
                        string fileName = Path.GetFileName(norm);
                        if (string.Equals(fileName, "QuestLauncher.exe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, "Launcher.exe", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (fi.Kind == FileKind.OwnCopy || fi.Kind == FileKind.LegacyKnown ||
                        fi.Kind == FileKind.LegacyLikely)
                    {
                        found.Add(new ImportCandidate {
                            Path = norm, Name = Path.GetFileName(norm), Kind = fi.Kind, Version = fi.Version,
                            Sha256 = fi.Sha256, BuildHash = fi.BuildHash, CopyId = fi.CopyId, Size = fi.Size,
                            LastWriteUtc = fi.LastWriteUtc,

                            Selected = (fi.Kind == FileKind.OwnCopy || fi.Kind == FileKind.LegacyKnown)
                        });
                    }
                }
            }

            if (depth >= maxDepth)
                return;

            string[] subDirs = null;
            try
            {
                subDirs = Directory.GetDirectories(dir);
            }
            catch
            {
            }
            if (subDirs == null)
                return;

            foreach (string sub in subDirs)
            {
                if (ct.IsCancellationRequested)
                    return;
                string name = Path.GetFileName(sub);
                if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                    continue;
                try
                {
                    var di = new DirectoryInfo(sub);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if ((di.Attributes & FileAttributes.System) != 0)
                        continue;
                }
                catch
                {
                    continue;
                }
                ScanDirRecursive(sub, depth + 1, maxDepth, existing, found, ct, onProgress);
            }
        }

        public int ImportSelected(IEnumerable<ImportCandidate> candidates)
        {
            int added = 0;
            string curBuild = TryGetBuildHash();
            _library.Update(
                list =>
                {
                    foreach (ImportCandidate c in candidates)
                    {
                        if (c == null || !c.Selected)
                            continue;
                        if (Library.FindByPath(list, c.Path) != null)
                            continue;

                        FileInspection fi = CopyIdentity.Inspect(c.Path, curBuild);
                        if (!string.Equals(fi.Sha256, c.Sha256, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (fi.Kind != FileKind.OwnCopy && fi.Kind != FileKind.LegacyKnown &&
                            fi.Kind != FileKind.LegacyLikely)
                            continue;

                        list.Add(new LibraryEntry {
                            Id = !string.IsNullOrEmpty(c.CopyId) ? NormalizeHexId(c.CopyId) : CopyIdentity.NewCopyId(),
                            Name = c.Name, Path = PathResolver.Normalize(c.Path), Version = c.Version ?? "старая",
                            Sha256 = c.Sha256, BuildHash = c.BuildHash ?? string.Empty, Size = c.Size,
                            CreatedUtc = c.LastWriteUtc == DateTime.MinValue ? DateTime.UtcNow : c.LastWriteUtc,
                            LastLaunchUtc = DateTime.MinValue, LaunchCount = 0, Favorite = false,
                            Origin = c.Kind == FileKind.OwnCopy ? "imported" : "imported-legacy"
                        });
                        added++;
                    }
                });
            if (added > 0)
                Log.Info("Импортировано записей: " + added);
            return added;
        }
    }
}
