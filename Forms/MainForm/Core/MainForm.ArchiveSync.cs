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

                    var archived = TryResolveArchivedPrintPath(order, out var archivedPrintPath, out var matchedLength);

                    if (string.Equals(NormalizeStatus(order.Status), WorkflowStatusNames.Error, StringComparison.Ordinal))
                        continue;

                    if (archived)
                    {
                        changed |= SetOrderStatus(
                            order,
                            WorkflowStatusNames.Archived,
                            "archive-sync",
                            $"Файл найден в папке Готово: {archivedPrintPath} (совпали имя и размер: {matchedLength} байт)",
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

        private bool TryResolveArchivedPrintPath(OrderData order, out string archivedPrintPath)
        {
            archivedPrintPath = string.Empty;
            if (order == null || string.IsNullOrWhiteSpace(_grandpaFolder))
                return false;

            return TryResolveArchivedPrintPath(order, out archivedPrintPath, out _);
        }

        private bool TryResolveArchivedPrintPath(OrderData order, out string archivedPrintPath, out long matchedLength)
        {
            archivedPrintPath = string.Empty;
            matchedLength = 0;
            if (order == null || string.IsNullOrWhiteSpace(_grandpaFolder))
                return false;

            RefreshArchiveIndexIfNeeded();
            foreach (var candidate in GetOrderArchiveCandidates(order))
            {
                var filePath = candidate.Path;
                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                if (!_archivedFilePathsByName.TryGetValue(fileName, out var candidatePaths))
                    continue;

                if (!TryGetExpectedFileLength(candidate, out var currentLength))
                    continue;

                foreach (var candidatePath in candidatePaths)
                {
                    if (!HasExistingFile(candidatePath))
                        continue;

                    if (!TryGetFileLength(candidatePath, out var candidateLength))
                        continue;

                    if (candidateLength != currentLength)
                        continue;

                    archivedPrintPath = candidatePath;
                    matchedLength = currentLength;
                    return true;
                }
            }

            return false;
        }

        private static List<(string Path, long? ExpectedLength)> GetOrderArchiveCandidates(OrderData order)
        {
            var candidates = new List<(string Path, long? ExpectedLength)>();
            AddCandidate(candidates, order.PrintPath, order.PrintFileSizeBytes);

            if (order.Items == null || order.Items.Count == 0)
                return candidates;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                AddCandidate(candidates, item.PrintPath, item.PrintFileSizeBytes);
            }

            return candidates;
        }

        private static void AddCandidate(List<(string Path, long? ExpectedLength)> candidates, string? path, long? expectedLength)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            candidates.Add((path.Trim(), expectedLength));
        }

        private static bool TryGetExpectedFileLength((string Path, long? ExpectedLength) candidate, out long length)
        {
            if (HasExistingFile(candidate.Path) && TryGetFileLength(candidate.Path, out length))
                return true;

            if (candidate.ExpectedLength.HasValue)
            {
                length = candidate.ExpectedLength.Value;
                return true;
            }

            length = 0;
            return false;
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
                        if (!_archivedFilePathsByName.TryGetValue(fileName, out var filePaths))
                        {
                            filePaths = new List<string>();
                            _archivedFilePathsByName[fileName] = filePaths;
                        }

                        filePaths.Add(filePath);
                    }
                }
            }
            catch
            {
                // Ошибки архива не должны блокировать UI и смену статусов.
            }
        }

        private static bool TryGetFileLength(string path, out long length)
        {
            length = 0;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                length = new FileInfo(path).Length;
                return true;
            }
            catch
            {
                return false;
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
