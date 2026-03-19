using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Replica
{
    public partial class MainForm
    {
        private void RefreshArchivedStatuses(bool forceArchiveIndexRefresh = false, bool rebuildGridIfChanged = true)
        {
            if (_archiveSyncInProgress || _orderHistory.Count == 0)
                return;

            _archiveSyncInProgress = true;
            try
            {
                RefreshArchiveIndexIfNeeded(forceArchiveIndexRefresh);

                var changed = false;
                foreach (var order in _orderHistory)
                {
                    if (order == null)
                        continue;

                    var archived = IsOrderInArchive(order);

                    if (string.Equals(NormalizeStatus(order.Status), WorkflowStatusNames.Error, StringComparison.Ordinal))
                        continue;

                    if (archived)
                    {
                        changed |= SetOrderStatus(
                            order,
                            WorkflowStatusNames.Archived,
                            "archive-sync",
                            "Файл найден в папке Готово (совпадение по содержимому)",
                            persistHistory: false,
                            rebuildGrid: false);
                        continue;
                    }

                    if (!string.Equals(NormalizeStatus(order.Status), WorkflowStatusNames.Archived, StringComparison.Ordinal))
                        continue;

                    var fallbackStatus = ResolveStatusWithoutArchive(order);
                    changed |= SetOrderStatus(
                        order,
                        fallbackStatus,
                        "archive-sync",
                        "Заказ больше не считается архивным",
                        persistHistory: false,
                        rebuildGrid: false);
                }

                if (!changed)
                    return;

                SaveHistory();
                if (rebuildGridIfChanged)
                    RebuildOrdersGrid();
            }
            finally
            {
                _archiveSyncInProgress = false;
            }
        }

        private bool IsOrderInArchive(OrderData order)
        {
            if (order == null || string.IsNullOrWhiteSpace(_grandpaFolder))
                return false;

            var fingerprints = GetOrderArchiveFingerprints(order);
            if (fingerprints.Count == 0)
                return false;

            RefreshArchiveIndexIfNeeded();
            foreach (var fingerprint in fingerprints)
            {
                if (!_archivedFilePathsByFingerprint.TryGetValue(fingerprint, out var candidatePath))
                    continue;

                return true;
            }

            return false;
        }

        private bool TryResolveArchivedPrintPath(OrderData order, out string archivedPrintPath)
        {
            archivedPrintPath = string.Empty;
            if (order == null || string.IsNullOrWhiteSpace(_grandpaFolder))
                return false;

            var fingerprints = GetOrderArchiveFingerprints(order);
            if (fingerprints.Count == 0)
                return false;

            RefreshArchiveIndexIfNeeded();
            foreach (var fingerprint in fingerprints)
            {
                if (!_archivedFilePathsByFingerprint.TryGetValue(fingerprint, out var candidatePath))
                    continue;

                if (!HasExistingFile(candidatePath))
                    continue;

                archivedPrintPath = candidatePath;
                return true;
            }

            return false;
        }

        private static HashSet<string> GetOrderArchiveFingerprints(OrderData order)
        {
            var fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddFileFingerprint(fingerprints, order.PrintPath);

            if (order.Items == null || order.Items.Count == 0)
                return fingerprints;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                AddFileFingerprint(fingerprints, item.PrintPath);
            }

            return fingerprints;
        }

        private static void AddFileFingerprint(HashSet<string> fingerprints, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !HasExistingFile(path))
                return;

            if (TryGetFileFingerprint(path, out var fingerprint))
                fingerprints.Add(fingerprint);
        }

        private string ResolveStatusWithoutArchive(OrderData order)
        {
            if (order.Items == null || order.Items.Count == 0)
                return ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);

            var items = order.Items.Where(x => x != null).ToList();
            if (items.Count == 0)
                return WorkflowStatusNames.Waiting;

            var total = items.Count;
            var done = items.Count(x => HasExistingFile(x.PrintPath));
            var active = items.Count(x => HasExistingFile(x.SourcePath) || HasExistingFile(x.PreparedPath) || HasExistingFile(x.PrintPath));

            if (done == total)
                return WorkflowStatusNames.Completed;

            return active > 0 ? WorkflowStatusNames.Processing : WorkflowStatusNames.Waiting;
        }

        private void RefreshArchiveIndexIfNeeded(bool force = false)
        {
            if (!force && DateTime.UtcNow - _archiveIndexLoadedAt < ArchiveIndexLifetime)
                return;

            _archiveIndexLoadedAt = DateTime.UtcNow;
            _archivedFilePathsByName.Clear();
            _archivedFilePathsByFingerprint.Clear();

            var archiveFolderPath = ResolveArchiveDoneFolderPath();
            if (string.IsNullOrWhiteSpace(archiveFolderPath) || !Directory.Exists(archiveFolderPath))
                return;

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(archiveFolderPath, "*", SearchOption.AllDirectories))
                {
                    if (!TryGetFileFingerprint(filePath, out var fingerprint))
                        continue;

                    if (!_archivedFilePathsByFingerprint.ContainsKey(fingerprint))
                        _archivedFilePathsByFingerprint[fingerprint] = filePath;
                }
            }
            catch
            {
                // Ошибки архива не должны блокировать UI и смену статусов.
            }
        }

        private string ResolveArchiveDoneFolderPath()
        {
            var grandpaPath = CleanPath(_grandpaFolder);
            if (string.IsNullOrWhiteSpace(grandpaPath))
                return string.Empty;

            var doneSubfolder = CleanPath(_archiveDoneSubfolder);
            if (string.IsNullOrWhiteSpace(doneSubfolder))
                return grandpaPath;

            if (Path.IsPathRooted(doneSubfolder))
                return doneSubfolder;

            if (EndsWithDirectoryName(grandpaPath, doneSubfolder))
                return grandpaPath;

            return Path.Combine(grandpaPath, doneSubfolder);
        }

        private static bool EndsWithDirectoryName(string folderPath, string directoryName)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(directoryName))
                return false;

            var normalizedPath = folderPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return false;

            var normalizedName = directoryName.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalizedName))
                return false;

            var lastSegment = Path.GetFileName(normalizedPath);
            return string.Equals(lastSegment, normalizedName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetFileFingerprint(string path, out string fingerprint)
        {
            fingerprint = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha256 = SHA256.Create();
                fingerprint = Convert.ToHexString(sha256.ComputeHash(stream));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
