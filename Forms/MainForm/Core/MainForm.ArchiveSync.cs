using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

                    var archived = TryResolveArchivedPrintPath(order, out var archivedPrintPath, out var matchedLength, out var matchedBy, out var archiveMissReason);

                    if (string.Equals(NormalizeStatus(order.Status), WorkflowStatusNames.Error, StringComparison.Ordinal))
                        continue;

                    if (archived)
                    {
                        changed |= SetOrderStatus(
                            order,
                            WorkflowStatusNames.Archived,
                            "archive-sync",
                            $"Файл найден в папке Готово: {archivedPrintPath} (совпало {matchedBy}; размер: {matchedLength} байт)",
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

            return TryResolveArchivedPrintPath(order, out archivedPrintPath, out _, out _, out _);
        }

        private bool TryResolveArchivedPrintPath(OrderData order, out string archivedPrintPath, out long matchedLength, out string matchedBy, out string missReason)
        {
            archivedPrintPath = string.Empty;
            matchedLength = 0;
            matchedBy = string.Empty;
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

                var hasExpectedHash = TryGetExpectedFileHash(candidate, out var expectedHash);
                if (hasExpectedHash
                    && _archivedFilePathsByHash.TryGetValue(expectedHash, out var hashPaths))
                {
                    foreach (var candidatePath in hashPaths)
                    {
                        if (!HasExistingFile(candidatePath))
                            continue;

                        if (!TryGetFileLength(candidatePath, out var candidateLength))
                            continue;

                        archivedPrintPath = candidatePath;
                        matchedLength = candidateLength;
                        matchedBy = "hash";
                        return true;
                    }
                }

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

                    if (hasExpectedHash && !ArchiveFileMatchesHash(candidatePath, expectedHash))
                        continue;

                    archivedPrintPath = candidatePath;
                    matchedLength = currentLength;
                    matchedBy = hasExpectedHash ? "имя, размер и hash" : "имя и размер";
                    return true;
                }

                var foundSizes = candidatePaths
                    .Where(HasExistingFile)
                    .Select(path => TryGetFileLength(path, out var len) ? len.ToString() : "?")
                    .Distinct()
                    .ToList();
                missReason = hasExpectedHash
                    ? foundSizes.Count == 0
                        ? $"в папке Готово есть имя {fileName}, но файлы недоступны"
                        : _archiveHashIndexBuildInProgress
                            ? $"в папке Готово есть имя {fileName}, но hash пока не совпал (ожидалось {expectedHash}; индекс hash ещё строится)"
                            : $"в папке Готово есть имя {fileName}, но hash не совпал (ожидалось {expectedHash})"
                    : foundSizes.Count == 0
                        ? $"в папке Готово есть имя {fileName}, но файлы недоступны"
                        : $"в папке Готово есть имя {fileName}, но размер не совпал (ожидалось {currentLength} байт, найдено: {string.Join(", ", foundSizes)})";
            }

            if (string.IsNullOrWhiteSpace(missReason))
                missReason = "совпадение по hash и имени/размеру не найдено";

            return false;
        }

        private static string DescribeFallbackReason(OrderData order, string fallbackStatus)
        {
            if (order.Items == null || order.Items.Count == 0)
            {
                return DescribePathState(order.SourcePath, order.PreparedPath, order.PrintPath);
            }

            var items = order.Items.Where(x => x != null).ToList();
            if (items.Count == 0)
                return "в заказе нет item-ов";

            var total = items.Count;
            var done = items.Count(x => HasExistingFile(x.PrintPath));
            var active = items.Count(x => HasExistingFile(x.SourcePath) || HasExistingFile(x.PreparedPath) || HasExistingFile(x.PrintPath));

            if (fallbackStatus == WorkflowStatusNames.Completed)
                return $"все item-печати на месте ({done}/{total})";

            var itemDetails = items
                .Select((item, index) => DescribeItemPathState(item, index + 1))
                .ToList();

            return active > 0
                ? $"есть активные файлы ({active}/{total}); {string.Join("; ", itemDetails)}"
                : $"все item-stage файлы отсутствуют; {string.Join("; ", itemDetails)}";
        }

        private static string DescribePathState(string? sourcePath, string? preparedPath, string? printPath)
        {
            var present = new List<string>();
            var missing = new List<string>();

            AddPathState(sourcePath, "исходник", present, missing);
            AddPathState(preparedPath, "подготовка", present, missing);
            AddPathState(printPath, "печать", present, missing);

            if (missing.Count == 0)
                return $"все пути заполнены ({string.Join(", ", present)})";

            if (present.Count == 0)
                return $"пустые пути: {string.Join(", ", missing)}";

            return $"есть пути: {string.Join(", ", present)}; пустые пути: {string.Join(", ", missing)}";
        }

        private static string DescribeItemPathState(OrderFileItem item, int index)
        {
            var label = string.IsNullOrWhiteSpace(item.ClientFileLabel)
                ? !string.IsNullOrWhiteSpace(item.ItemId)
                    ? item.ItemId!
                    : $"item-{index}"
                : item.ClientFileLabel;

            return $"{label}: {DescribePathState(item.SourcePath, item.PreparedPath, item.PrintPath)}";
        }

        private static void AddPathState(string? path, string label, List<string> present, List<string> missing)
        {
            if (HasExistingFile(path))
                present.Add(label);
            else
                missing.Add(label);
        }

        private static List<(string Path, long? ExpectedLength, string Hash)> GetOrderArchiveCandidates(OrderData order)
        {
            var candidates = new List<(string Path, long? ExpectedLength, string Hash)>();
            AddCandidate(candidates, order.PrintPath, order.PrintFileSizeBytes, order.PrintFileHash);

            if (order.Items == null || order.Items.Count == 0)
                return candidates;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                AddCandidate(candidates, item.PrintPath, item.PrintFileSizeBytes, item.PrintFileHash);
            }

            return candidates;
        }

        private static void AddCandidate(List<(string Path, long? ExpectedLength, string Hash)> candidates, string? path, long? expectedLength, string? hash)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            candidates.Add((path.Trim(), expectedLength, hash ?? string.Empty));
        }

        private static bool TryGetExpectedFileLength((string Path, long? ExpectedLength, string Hash) candidate, out long length)
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

        private static bool TryGetExpectedFileHash((string Path, long? ExpectedLength, string Hash) candidate, out string hash)
        {
            hash = candidate.Hash ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(hash))
                return true;

            if (!HasExistingFile(candidate.Path))
                return false;

            return FileHashService.TryComputeSha256(candidate.Path, out hash, out _);
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
            if (!force && _archiveHashIndexBuildInProgress)
                return;

            _archiveIndexLoadedAt = DateTime.UtcNow;
            _archivedFileNames.Clear();
            _archivedFilePathsByName.Clear();
            _archivedFilePathsByHash.Clear();
            _archiveHashIndexBuildInProgress = false;

            var archiveFolderPath = ResolveArchiveDoneFolderPath();
            if (string.IsNullOrWhiteSpace(archiveFolderPath) || !Directory.Exists(archiveFolderPath))
                return;

            var archivePdfPaths = new List<string>();
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(archiveFolderPath, "*.pdf", SearchOption.AllDirectories))
                {
                    archivePdfPaths.Add(filePath);
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

                StartArchiveHashIndexBuild(archivePdfPaths);
            }
            catch
            {
                // Ошибки архива не должны блокировать UI и смену статусов.
                _archiveHashIndexBuildInProgress = false;
            }
        }

        private static void AddArchiveIndex(Dictionary<string, List<string>> index, string key, string filePath)
        {
            if (!index.TryGetValue(key, out var filePaths))
            {
                filePaths = new List<string>();
                index[key] = filePaths;
            }

            filePaths.Add(filePath);
        }

        private void StartArchiveHashIndexBuild(List<string> archivePdfPaths)
        {
            _archiveHashIndexBuildVersion++;
            var buildVersion = _archiveHashIndexBuildVersion;
            _archiveHashIndexBuildInProgress = archivePdfPaths.Count > 0;

            if (archivePdfPaths.Count == 0)
                return;

            _ = Task.Run(() =>
            {
                var hashIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var filePath in archivePdfPaths)
                {
                    if (IsDisposed || buildVersion != _archiveHashIndexBuildVersion)
                        return;

                    if (FileHashService.TryComputeSha256(filePath, out var hash, out _) && !string.IsNullOrWhiteSpace(hash))
                        AddArchiveIndex(hashIndex, hash, filePath);
                }

                if (IsDisposed)
                    return;

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed || buildVersion != _archiveHashIndexBuildVersion)
                            return;

                        _archivedFilePathsByHash.Clear();
                        foreach (var entry in hashIndex)
                            _archivedFilePathsByHash[entry.Key] = entry.Value;

                        _archiveHashIndexBuildInProgress = false;
                        RefreshArchivedStatuses(forceArchiveIndexRefresh: false, rebuildGridIfChanged: true);
                    }));
                }
                catch
                {
                    // Форма может быть уже закрыта — безопасно игнорируем.
                }
            });
        }

        private static bool ArchiveFileMatchesHash(string archivePath, string expectedHash)
        {
            return FileHashService.TryComputeSha256(archivePath, out var actualHash, out _)
                && string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
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
