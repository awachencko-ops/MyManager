using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrderProcessor
    {
        private static bool ShouldStoreInOrderFolder(OrderData order)
        {
            return !string.IsNullOrWhiteSpace(order.FolderName);
        }

        private string EnsureUniquePath(string fullPath)
        {
            if (!File.Exists(fullPath))
                return fullPath;

            string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string ext = Path.GetExtension(fullPath);
            string name = Path.GetFileNameWithoutExtension(fullPath);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                i++;
            }
            while (File.Exists(candidate));

            return candidate;
        }

        private string CopyIntoStage(OrderData o, int stage, string src, string name, string rootPath)
        {
            string sub = stage switch
            {
                OrderStages.Source => TempInFolder,
                OrderStages.Prepared => TempPrepressFolder,
                OrderStages.Print => TempPrintFolder,
                _ => ""
            };
            string path = Path.Combine(rootPath, sub);
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "ensure-stage-folder",
                path: path,
                action: () => Directory.CreateDirectory(path));
            string dest = Path.Combine(path, name);
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "copy-into-stage",
                path: dest,
                action: () => File.Copy(src, dest, true));
            return dest;
        }

        private static long? TryGetFileLength(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;

                return new FileInfo(path).Length;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetFileHash(string path)
        {
            return FileHashService.TryComputeSha256(path, out var hash, out _) ? hash : string.Empty;
        }

        private void MoveTempToOrderFolder(OrderData order, string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(order.FolderName)) return;

            string sourcePath = MoveFileIfExists(order.SourcePath, GetOrderStagePath(order, OrderStages.Source));
            string preparedPath = MoveFileIfExists(order.PreparedPath, GetOrderStagePath(order, OrderStages.Prepared));
            string printPath = MoveFileIfExists(order.PrintPath, GetOrderStagePath(order, OrderStages.Print));

            if (!string.IsNullOrEmpty(sourcePath)) order.SourcePath = sourcePath;
            if (!string.IsNullOrEmpty(preparedPath)) order.PreparedPath = preparedPath;
            if (!string.IsNullOrEmpty(printPath)) order.PrintPath = printPath;

            TryDeleteEmptyFolders(tempRoot);
        }

        private void MovePrintToGrandpa(OrderData order, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(grandpaPath)) return;
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "ensure-grandpa-folder",
                path: grandpaPath,
                action: () => Directory.CreateDirectory(grandpaPath));

            string target = Path.Combine(grandpaPath, Path.GetFileName(order.PrintPath));
            MoveFileWithOverwrite(order.PrintPath, target);
            order.PrintPath = target;
            TryCopyPathToClipboard(target);

            if (!string.IsNullOrEmpty(order.SourcePath) && File.Exists(order.SourcePath))
            {
                TryDeleteFileQuietly(order.SourcePath, $"cleanup-source-after-grandpa:{order.Id}", DependencyStorage);
            }
        }

        private string CopyToGrandpa(string sourcePath, string fileName, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(grandpaPath)) return sourcePath;
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "ensure-grandpa-folder",
                path: grandpaPath,
                action: () => Directory.CreateDirectory(grandpaPath));
            string target = Path.Combine(grandpaPath, fileName);
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "copy-to-grandpa",
                path: target,
                action: () => File.Copy(sourcePath, target, true));
            TryCopyPathToClipboard(target);
            return target;
        }

        private void TryCopyPathToClipboard(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                    Clipboard.SetText(path);
            }
            catch (ExternalException ex)
            {
                Logger.Warn($"CLIPBOARD | set-text-failed | path={path} | {ex.Message}");
            }
            catch
            {
                Logger.Warn($"CLIPBOARD | set-text-failed | path={path} | unknown error");
            }
        }

        private bool IsInGrandpa(string path, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(grandpaPath)) return false;
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string fullGrandpa = Path.GetFullPath(grandpaPath).TrimEnd(Path.DirectorySeparatorChar);
            return fullPath.StartsWith(fullGrandpa + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, fullGrandpa, StringComparison.OrdinalIgnoreCase);
        }

        private string GetOrderStagePath(OrderData order, int stage)
        {
            string sub = stage switch
            {
                OrderStages.Source => "1. исходные",
                OrderStages.Prepared => "2. подготовка",
                OrderStages.Print => "3. печать",
                _ => ""
            };
            string path = Path.Combine(_rootPath, order.FolderName, sub);
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "ensure-order-stage-folder",
                path: path,
                action: () => Directory.CreateDirectory(path));
            return path;
        }

        private string MoveFileIfExists(string sourcePath, string targetFolder)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return "";
            string targetPath = Path.Combine(targetFolder, Path.GetFileName(sourcePath));
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                return sourcePath;
            MoveFileWithOverwrite(sourcePath, targetPath);
            return targetPath;
        }

        private void MoveFileWithOverwrite(string sourcePath, string targetPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;
            if (File.Exists(targetPath))
            {
                TryDeleteFileQuietly(targetPath, $"move-overwrite-target:{Path.GetFileName(targetPath)}", DependencyStorage);
            }
            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "move-file-with-overwrite",
                path: targetPath,
                action: () => File.Move(sourcePath, targetPath));
        }

        private void EnsureTempFolders(string tempRoot)
        {
            foreach (var folder in new[] { TempInFolder, TempPrepressFolder, TempPrintFolder })
            {
                var path = Path.Combine(tempRoot, folder);
                ExecuteWithDependencyRetry(
                    dependencyName: DependencyStorage,
                    operation: "ensure-temp-folder",
                    path: path,
                    action: () => Directory.CreateDirectory(path));
            }
        }

        private void TryDeleteEmptyFolders(string tempRoot)
        {
            try
            {
                foreach (var folder in new[] { TempInFolder, TempPrepressFolder, TempPrintFolder })
                {
                    string path = Path.Combine(tempRoot, folder);
                    if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
                    {
                        ExecuteWithDependencyRetry(
                            dependencyName: DependencyStorage,
                            operation: "delete-empty-temp-folder",
                            path: path,
                            action: () => Directory.Delete(path, true));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"TEMP-CLEANUP | delete-empty-folders-failed | root={tempRoot} | {ex.Message}");
            }
        }

        private async Task<string?> WaitForFileAsync(
            string folder,
            string fileName,
            TimeSpan timeout,
            CancellationToken ct,
            Action<double>? progressReporter = null)
        {
            string full = Path.Combine(folder, fileName);
            var startUtc = DateTime.UtcNow;
            long lastSize = -1;
            while (DateTime.UtcNow - startUtc < timeout)
            {
                ct.ThrowIfCancellationRequested();
                progressReporter?.Invoke(CalculateElapsedRatio(startUtc, timeout));
                if (File.Exists(full))
                {
                    FileInfo fi = new FileInfo(full);
                    if (fi.Length > 0 && fi.Length == lastSize && IsFileReady(full))
                    {
                        progressReporter?.Invoke(1d);
                        return full;
                    }

                    lastSize = fi.Length;
                }
                await Task.Delay(1000, ct);
            }

            progressReporter?.Invoke(1d);
            return null;
        }

        private async Task<(string? path, string? where)> WaitForFileInAnyAsync(
            (string folder, string label)[] places,
            string fileName,
            TimeSpan timeout,
            CancellationToken ct,
            Action<double>? progressReporter = null)
        {
            var startUtc = DateTime.UtcNow;
            while (DateTime.UtcNow - startUtc < timeout)
            {
                ct.ThrowIfCancellationRequested();
                progressReporter?.Invoke(CalculateElapsedRatio(startUtc, timeout));
                foreach (var p in places)
                {
                    if (string.IsNullOrEmpty(p.folder)) continue;
                    string full = Path.Combine(p.folder, fileName);
                    if (File.Exists(full) && IsFileReady(full))
                    {
                        progressReporter?.Invoke(1d);
                        return (full, p.label);
                    }
                }
                await Task.Delay(1000, ct);
            }

            progressReporter?.Invoke(1d);
            return (null, null);
        }

        private static double CalculateElapsedRatio(DateTime startUtc, TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
                return 1d;

            var elapsed = DateTime.UtcNow - startUtc;
            var ratio = elapsed.TotalMilliseconds / timeout.TotalMilliseconds;
            return Math.Clamp(ratio, 0d, 1d);
        }

        private void TryDeleteFileQuietly(string? path, string context, string dependencyName)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                ExecuteWithDependencyRetry(
                    dependencyName: dependencyName,
                    operation: $"delete-file:{context}",
                    path: path,
                    action: () => File.Delete(path));
            }
            catch (Exception ex)
            {
                Logger.Warn($"FILE | delete-failed | context={context} | path={path} | {ex.Message}");
            }
        }


        private void CleanupPitStopArtifacts(ActionConfig cfg, string fileName, params string?[] extraPaths)
        {
            var reportFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_log.pdf";
            var paths = new List<string?>(extraPaths);
            AddIfNotEmpty(paths, cfg.InputFolder, fileName);
            AddIfNotEmpty(paths, cfg.ReportSuccess, reportFileName);
            AddIfNotEmpty(paths, cfg.ReportError, reportFileName);
            AddIfNotEmpty(paths, cfg.OriginalSuccess, fileName);
            AddIfNotEmpty(paths, cfg.OriginalError, fileName);
            AddIfNotEmpty(paths, cfg.ProcessedSuccess, fileName);
            AddIfNotEmpty(paths, cfg.ProcessedError, fileName);

            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                TryDeleteFileQuietly(path, $"pitstop-cleanup:{fileName}", DependencyPitStop);
            }
        }

        private static void AddIfNotEmpty(List<string?> paths, string? folder, string fileName)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return;

            paths.Add(Path.Combine(folder, fileName));
        }

        private static IEnumerable<string> GetQuiteImposingLogPaths(ImposingConfig cfg, string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(cfg.In, fileName + ".log"),
                Path.Combine(cfg.Out, fileName + ".log"),
                Path.Combine(cfg.Done, fileName + ".log"),
                Path.Combine(cfg.Error, fileName + ".log")
            };

            return candidates.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private void CleanupQuiteImposingArtifacts(ImposingConfig cfg, string fileName, params string?[] extraPaths)
        {
            var paths = new List<string?>(extraPaths)
            {
                Path.Combine(cfg.In, fileName),
                Path.Combine(cfg.Out, fileName),
                Path.Combine(cfg.Done, fileName),
                Path.Combine(cfg.Error, fileName),
                Path.Combine(cfg.In, fileName + ".log"),
                Path.Combine(cfg.Out, fileName + ".log"),
                Path.Combine(cfg.Done, fileName + ".log"),
                Path.Combine(cfg.Error, fileName + ".log")
            };

            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                TryDeleteFileQuietly(path, $"imposing-cleanup:{fileName}", DependencyImposing);
            }
        }
    }
}
