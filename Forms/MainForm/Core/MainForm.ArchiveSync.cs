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

                    var archived = TryResolveArchivedPrintPath(order, out var archivedPrintPath, out var matchedLength, out var archiveMissReason);

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
                    var fallbackReason = DescribeFallbackReason(order, fallbackStatus);
                    changed |= SetOrderStatus(
                        order,
                        fallbackStatus,
                        "archive-sync",
                        $"Заказ больше не считается архивным: {archiveMissReason}; без архива -> {fallbackStatus} ({fallbackReason})",
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

            return TryResolveArchivedPrintPath(order, out archivedPrintPath, out _, out _);
        }

        private bool TryResolveArchivedPrintPath(OrderData order, out string archivedPrintPath, out long matchedLength, out string missReason)
        {
            archivedPrintPath = string.Empty;
            matchedLength = 0;
            missReason = string.Empty;
            if (order == null || string.IsNullOrWhiteSpace(_grandpaFolder))
            {
                missReason = "не задана папка архива";
                return false;
            }

            RefreshArchiveIndexIfNeeded();
            var candidates = GetOrderArchiveCandidates(order);
            if (candidates.Count == 0)
            {
                missReason = "в заказе нет печатного файла для проверки";
                return false;
            }

            foreach (var candidate in candidates)
            {
                var filePath = candidate.Path;
                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                if (!_archivedFilePathsByName.TryGetValue(fileName, out var candidatePaths))
                {
                    missReason = $"в папке Готово не найден файл с именем {fileName}";
                    continue;
                }

                if (!TryGetExpectedFileLength(candidate, out var currentLength))
                {
                    missReason = $"для файла {fileName} не известен ожидаемый размер";
                    continue;
                }

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

                var foundSizes = candidatePaths
                    .Where(HasExistingFile)
                    .Select(path => TryGetFileLength(path, out var len) ? len.ToString() : "?")
                    .Distinct()
                    .ToList();
                missReason = foundSizes.Count == 0
                    ? $"в папке Готово есть имя {fileName}, но файлы недоступны"
                    : $"в папке Готово есть имя {fileName}, но размер не совпал (ожидалось {currentLength} байт, найдено: {string.Join(", ", foundSizes)})";
            }

            if (string.IsNullOrWhiteSpace(missReason))
                missReason = "совпадение по имени и размеру не найдено";

            return false;
        }

        private static string DescribeFallbackReason(OrderData order, string fallbackStatus)
        {
            if (order.Items == null || order.Items.Count == 0)
            {
                var parts = new List<string>();
                if (HasExistingFile(order.SourcePath))
                    parts.Add("есть исходник");
                if (HasExistingFile(order.PreparedPath))
                    parts.Add("есть подготовка");
                if (HasExistingFile(order.PrintPath))
                    parts.Add("есть печать");

                return parts.Count == 0
                    ? "все stage-файлы отсутствуют"
                    : $"stage-файлы: {string.Join(", ", parts)}";
            }

            var items = order.Items.Where(x => x != null).ToList();
            if (items.Count == 0)
                return "в заказе нет item-ов";

            var total = items.Count;
            var done = items.Count(x => HasExistingFile(x.PrintPath));
            var active = items.Count(x => HasExistingFile(x.SourcePath) || HasExistingFile(x.PreparedPath) || HasExistingFile(x.PrintPath));

            if (fallbackStatus == WorkflowStatusNames.Completed)
                return $"все item-печати на месте ({done}/{total})";

            return active > 0
                ? $"есть активные файлы ({active}/{total})"
                : "все item-stage файлы отсутствуют";
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
