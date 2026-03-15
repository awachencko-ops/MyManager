using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                            "Файл найден в архиве",
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

            var fileNames = GetOrderArchiveFileNames(order);
            if (fileNames.Count == 0)
                return false;

            RefreshArchiveIndexIfNeeded();
            foreach (var fileName in fileNames)
            {
                if (_archivedFileNames.Contains(fileName))
                    return true;
            }

            return false;
        }

        private bool TryResolveArchivedPrintPath(OrderData order, out string archivedPrintPath)
        {
            archivedPrintPath = string.Empty;
            if (order == null || string.IsNullOrWhiteSpace(_grandpaFolder))
                return false;

            var fileNames = GetOrderArchiveFileNames(order);
            if (fileNames.Count == 0)
                return false;

            RefreshArchiveIndexIfNeeded();
            foreach (var fileName in fileNames)
            {
                if (!_archivedFilePathsByName.TryGetValue(fileName, out var candidatePath))
                    continue;

                if (!HasExistingFile(candidatePath))
                    continue;

                archivedPrintPath = candidatePath;
                return true;
            }

            return false;
        }

        private static HashSet<string> GetOrderArchiveFileNames(OrderData order)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddFileName(names, order.PrintPath);

            if (order.Items == null || order.Items.Count == 0)
                return names;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                AddFileName(names, item.PrintPath);
            }

            return names;
        }

        private static void AddFileName(HashSet<string> names, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fileName = Path.GetFileName(path.Trim());
            if (!string.IsNullOrWhiteSpace(fileName))
                names.Add(fileName);
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
            _archivedFileNames.Clear();
            _archivedFilePathsByName.Clear();

            var archiveFolderPath = ResolveArchiveDoneFolderPath();
            if (string.IsNullOrWhiteSpace(archiveFolderPath) || !Directory.Exists(archiveFolderPath))
                return;

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(archiveFolderPath, "*", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        _archivedFileNames.Add(fileName);
                        if (!_archivedFilePathsByName.ContainsKey(fileName))
                            _archivedFilePathsByName[fileName] = filePath;
                    }
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
    }
}
