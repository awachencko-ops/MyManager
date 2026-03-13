using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyManager
{
    public class OrderProcessor
    {
        public event Action<string, string, string>? OnStatusChanged;
        public event Action<string>? OnLog;
        public event Action<string, int, string>? OnProgressChanged;

        private readonly string _rootPath;
        private const string TempInFolder = "in";
        private const string TempPrepressFolder = "prepress";
        private const string TempPrintFolder = "print";

        public OrderProcessor(string rootPath)
        {
            _rootPath = rootPath;
        }

        public async Task RunAsync(OrderData order, CancellationToken ct, IEnumerable<string>? selectedItemIds = null)
        {
            var topologyResult = OrderTopologyService.Normalize(order);
            if (topologyResult.Issues.Count > 0)
            {
                foreach (var issue in topologyResult.Issues)
                    Logger.Warn($"TOPOLOGY | order={order.Id} | {issue}");
            }

            var settings = AppSettings.Load();
            var timeout = TimeSpan.FromMinutes(settings.RunTimeoutMinutes);
            string tempRoot = string.IsNullOrWhiteSpace(settings.TempFolderPath)
                ? Path.Combine(_rootPath, settings.TempFolderName)
                : settings.TempFolderPath;
            EnsureTempFolders(tempRoot);

            var selectedSet = selectedItemIds == null
                ? null
                : new HashSet<string>(selectedItemIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);

            if (OrderTopologyService.IsMultiOrder(order))
            {
                ReportProgress(order, 0, "Запуск мульти-заказа");
                await RunMultiOrderAsync(order, settings, timeout, tempRoot, selectedSet, ct);
                return;
            }

            ReportProgress(order, 0, "Запуск");
            try
            {
                Logger.Info($">>> СТАРТ: Заказ {order.Id}");
                Notify(order, "🟡 Запуск…", "Поиск конфигов...");
                ReportProgress(order, 5, "Поиск конфигов");

                var pitCfg = ConfigService.GetPitStopConfigByName(order.PitStopAction);
                var impCfg = ConfigService.GetImposingConfigByName(order.ImposingAction);

                if (pitCfg == null && impCfg == null)
                {
                    Notify(order, WorkflowStatusNames.Waiting, "Сценарии не выбраны.");
                    ReportProgress(order, 100, "Сценарии не выбраны");
                    return;
                }

                // --- PITSTOP ---
                if (pitCfg != null)
                {
                    ReportProgress(order, 20, "PitStop");
                    if (!File.Exists(order.PreparedPath)) throw new Exception("Файл для PitStop не найден.");

                    string fileName = Path.GetFileName(order.PreparedPath);
                    string targetIn = Path.Combine(pitCfg.InputFolder, fileName);

                    Notify(order, "🟡 PitStop: копирование", "Копирую в Hotfolder PitStop...");
                    Logger.Info($"Копирование: {order.PreparedPath} -> {targetIn}");
                    File.Copy(order.PreparedPath, targetIn, true);

                    if (impCfg != null)
                    {
                        var places = new (string folder, string label)[] {
                            (pitCfg.ProcessedSuccess, "PitStop Success"),
                            (pitCfg.ProcessedError,   "PitStop Error"),
                            (impCfg.In,               "Imposing In"),
                            (impCfg.Out,              "Imposing Out")
                        };
                        var pitWaitReporter = CreateRangedProgressReporter(order, 25, 54, "PitStop: ожидание");
                        var (foundPath, where) = await WaitForFileInAnyAsync(places, fileName, timeout, ct, pitWaitReporter);
                        if (foundPath == null) throw new Exception("Таймаут PitStop.");
                        if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                        Notify(order, "🟡 PitStop OK", $"PitStop завершен ({where})");
                        ReportProgress(order, 55, "PitStop завершен");
                    }
                    else
                    {
                        var pitWaitReporter = CreateRangedProgressReporter(order, 25, 58, "PitStop: ожидание");
                        var okFile = await WaitForFileAsync(pitCfg.ProcessedSuccess, fileName, timeout, ct, pitWaitReporter);
                        if (okFile == null) throw new Exception("Таймаут PitStop.");
                        string newName = $"{Path.GetFileNameWithoutExtension(fileName)}_pitstop{Path.GetExtension(fileName)}";
                        order.PreparedPath = CopyIntoStage(order, 2, okFile, newName, tempRoot);
                        Notify(order, "🟡 PitStop готово", "Версия сохранена.");
                        ReportProgress(order, 60, "PitStop завершен");
                    }
                }
                else
                {
                    ReportProgress(order, 45, "PitStop пропущен");
                }

                // --- IMPOSING ---
                if (impCfg != null)
                {
                    ReportProgress(order, 70, "Imposing");
                    string fileName = Path.GetFileName(order.PreparedPath);
                    string targetIn = Path.Combine(impCfg.In, fileName);

                    if (!File.Exists(targetIn))
                    {
                        Notify(order, "🟡 Imposing: старт", "Копирую в Hotfolder Imposing...");
                        File.Copy(order.PreparedPath, targetIn, true);
                    }

                    var imposingWaitReporter = CreateRangedProgressReporter(order, 72, 89, "Imposing: ожидание");
                    var outFile = await WaitForFileAsync(impCfg.Out, fileName, timeout, ct, imposingWaitReporter);
                    if (outFile == null) throw new Exception("Таймаут Imposing.");

                    string printName = $"{order.Id}.pdf";
                    if (ShouldStoreInOrderFolder(order))
                    {
                        order.PrintPath = CopyIntoStage(order, 3, outFile, printName, tempRoot);
                        try { File.Delete(outFile); } catch { }
                    }
                    else
                    {
                        order.PrintPath = CopyToGrandpa(outFile, printName, settings.GrandpaPath);
                        try { File.Delete(outFile); } catch { }
                    }

                    ReportProgress(order, 90, "Imposing завершен");
                }
                else
                {
                    ReportProgress(order, 90, "Imposing пропущен");
                }

                if (!string.IsNullOrEmpty(order.PrintPath) && File.Exists(order.PrintPath))
                {
                    if (ShouldStoreInOrderFolder(order))
                        MoveTempToOrderFolder(order, tempRoot);
                    else if (!IsInGrandpa(order.PrintPath, settings.GrandpaPath))
                        MovePrintToGrandpa(order, settings.GrandpaPath);
                }

                Notify(order, WorkflowStatusNames.Completed, "Заказ успешно выполнен.");
                ReportProgress(order, 100, WorkflowStatusNames.Completed);
            }
            catch (OperationCanceledException)
            {
                Logger.Warn($"Остановлено пользователем: {order.Id}");
                Notify(order, WorkflowStatusNames.Cancelled, "Остановлено пользователем");
                ReportProgress(order, 100, "Остановлено");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка в {order.Id}: {ex.Message}");
                Notify(order, WorkflowStatusNames.Error, ex.Message);
                ReportProgress(order, 100, WorkflowStatusNames.Error);
            }
        }

        private async Task RunMultiOrderAsync(OrderData order, AppSettings settings, TimeSpan timeout, string tempRoot, HashSet<string>? selectedSet, CancellationToken ct)
        {
            var allItems = order.Items
                .Where(x => x != null)
                .OrderBy(x => x.SequenceNo)
                .ToList();
            var runItems = selectedSet == null || selectedSet.Count == 0
                ? allItems
                : allItems.Where(x => selectedSet.Contains(x.ItemId)).ToList();

            if (runItems.Count == 0)
            {
                Notify(order, WorkflowStatusNames.Waiting, "Нет выбранных файлов для обработки");
                ReportProgress(order, 100, "Нет выбранных файлов");
                return;
            }

            int maxParallel = settings.MaxParallelism <= 0 ? runItems.Count : Math.Min(settings.MaxParallelism, runItems.Count);
            using var semaphore = new SemaphoreSlim(Math.Max(1, maxParallel));
            int done = 0;
            var progressByItemKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var progressSync = new object();

            void SetItemProgress(string itemKey, int value)
            {
                lock (progressSync)
                {
                    if (!progressByItemKey.ContainsKey(itemKey))
                        progressByItemKey[itemKey] = 0;

                    progressByItemKey[itemKey] = Math.Clamp(value, 0, 100);
                    if (progressByItemKey.Count == 0)
                        return;

                    var average = (int)Math.Round(progressByItemKey.Values.Average());
                    ReportProgress(order, average, $"Прогресс мульти-заказа {average}%");
                }
            }

            var tasks = runItems.Select((item, index) => new { item, index }).Select(async payload =>
            {
                var item = payload.item;
                int itemIndex = payload.index + 1;
                var itemKey = string.IsNullOrWhiteSpace(item.ItemId) ? $"item-{itemIndex}" : item.ItemId;
                await semaphore.WaitAsync(ct);
                try
                {
                    SetItemProgress(itemKey, 5);
                    item.FileStatus = WorkflowStatusNames.Processing;
                    item.UpdatedAt = DateTime.Now;
                    order.RefreshAggregatedStatus();
                    Notify(order, order.Status, $"Обработка {item.ClientFileLabel}");
                    await RunSingleItemAsync(
                        order,
                        item,
                        itemIndex,
                        settings,
                        timeout,
                        tempRoot,
                        ct,
                        progressPercent => SetItemProgress(itemKey, progressPercent));
                }
                catch (OperationCanceledException)
                {
                    item.FileStatus = WorkflowStatusNames.Cancelled;
                    item.LastReason = "Остановлено пользователем";
                    item.UpdatedAt = DateTime.Now;
                }
                catch (Exception ex)
                {
                    item.FileStatus = WorkflowStatusNames.Error;
                    item.LastReason = ex.Message;
                    item.UpdatedAt = DateTime.Now;
                    Logger.Error($"Ошибка файла {item.ClientFileLabel}: {ex.Message}");
                }
                finally
                {
                    var doneCount = Interlocked.Increment(ref done);
                    SetItemProgress(itemKey, 100);
                    order.RefreshAggregatedStatus();
                    Notify(order, order.Status, $"Прогресс {doneCount}/{runItems.Count}");
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (ct.IsCancellationRequested)
            {
                Notify(order, WorkflowStatusNames.Cancelled, "Остановлено пользователем");
                ReportProgress(order, 100, "Остановлено");
                return;
            }

            order.RefreshAggregatedStatus();
            Notify(order, order.Status, "Обработка мульти-заказа завершена");
            ReportProgress(order, 100, "Мульти-заказ завершен");
        }

        private async Task RunSingleItemAsync(
            OrderData order,
            OrderFileItem item,
            int itemIndex,
            AppSettings settings,
            TimeSpan timeout,
            string tempRoot,
            CancellationToken ct,
            Action<int>? progressReporter = null)
        {
            var lastProgress = -1;
            void ReportItemProgress(int value)
            {
                var bounded = Math.Clamp(value, 0, 100);
                if (bounded <= lastProgress)
                    return;

                lastProgress = bounded;
                progressReporter?.Invoke(bounded);
            }

            Action<double> CreateItemProgressRangeReporter(int fromPercent, int toPercent)
            {
                var from = Math.Clamp(fromPercent, 0, 100);
                var to = Math.Clamp(toPercent, from, 100);
                return ratio =>
                {
                    var boundedRatio = Math.Clamp(ratio, 0d, 1d);
                    var value = from + (int)Math.Round((to - from) * boundedRatio);
                    ReportItemProgress(value);
                };
            }

            string pitAction = string.IsNullOrWhiteSpace(item.PitStopAction) || item.PitStopAction == "-"
                ? order.PitStopAction
                : item.PitStopAction;
            string impAction = string.IsNullOrWhiteSpace(item.ImposingAction) || item.ImposingAction == "-"
                ? order.ImposingAction
                : item.ImposingAction;

            var pitCfg = ConfigService.GetPitStopConfigByName(pitAction);
            var impCfg = ConfigService.GetImposingConfigByName(impAction);

            if (pitCfg == null && impCfg == null)
                throw new Exception("Сценарии не выбраны.");

            ReportItemProgress(5);
            if (pitCfg != null)
            {
                ReportItemProgress(12);
                if (!File.Exists(item.PreparedPath))
                    throw new Exception("Файл для PitStop не найден.");

                string fileName = Path.GetFileName(item.PreparedPath);
                string targetIn = Path.Combine(pitCfg.InputFolder, fileName);
                File.Copy(item.PreparedPath, EnsureUniquePath(targetIn), true);

                if (impCfg != null)
                {
                    var places = new (string folder, string label)[] {
                        (pitCfg.ProcessedSuccess, "PitStop Success"),
                        (pitCfg.ProcessedError,   "PitStop Error"),
                        (impCfg.In,               "Imposing In"),
                        (impCfg.Out,              "Imposing Out")
                    };
                    var pitWaitReporter = CreateItemProgressRangeReporter(20, 55);
                    var (foundPath, where) = await WaitForFileInAnyAsync(places, fileName, timeout, ct, pitWaitReporter);
                    if (foundPath == null) throw new Exception("Таймаут PitStop.");
                    if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                }
                else
                {
                    var pitWaitReporter = CreateItemProgressRangeReporter(20, 58);
                    var okFile = await WaitForFileAsync(pitCfg.ProcessedSuccess, fileName, timeout, ct, pitWaitReporter);
                    if (okFile == null) throw new Exception("Таймаут PitStop.");
                    string newName = $"{Path.GetFileNameWithoutExtension(fileName)}_pitstop{Path.GetExtension(fileName)}";
                    item.PreparedPath = CopyIntoStage(order, 2, okFile, newName, tempRoot);
                }

                ReportItemProgress(60);
            }
            else
            {
                ReportItemProgress(60);
            }

            if (impCfg != null)
            {
                ReportItemProgress(65);
                string fileName = Path.GetFileName(item.PreparedPath);
                string targetIn = Path.Combine(impCfg.In, fileName);
                if (!File.Exists(targetIn))
                    File.Copy(item.PreparedPath, EnsureUniquePath(targetIn), true);

                var imposingWaitReporter = CreateItemProgressRangeReporter(72, 95);
                var outFile = await WaitForFileAsync(impCfg.Out, fileName, timeout, ct, imposingWaitReporter);
                if (outFile == null) throw new Exception("Таймаут Imposing.");

                string printNameBase = string.IsNullOrWhiteSpace(order.Id) ? "order" : order.Id;
                string printName = $"{printNameBase}_{itemIndex}.pdf";
                if (ShouldStoreInOrderFolder(order))
                {
                    item.PrintPath = CopyIntoStage(order, 3, outFile, printName, tempRoot);
                    try { File.Delete(outFile); } catch { }
                }
                else
                {
                    item.PrintPath = CopyToGrandpa(outFile, printName, settings.GrandpaPath);
                    try { File.Delete(outFile); } catch { }
                }
            }
            else
            {
                ReportItemProgress(95);
            }

            item.FileStatus = !string.IsNullOrWhiteSpace(item.PrintPath) && File.Exists(item.PrintPath)
                ? WorkflowStatusNames.Completed
                : WorkflowStatusNames.Waiting;
            item.LastReason = string.Empty;
            item.UpdatedAt = DateTime.Now;
            ReportItemProgress(100);
        }

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

        private void Notify(OrderData o, string s, string l)
        {
            OnStatusChanged?.Invoke(o.Id, s, l);
            OnLog?.Invoke(l);
            Logger.Info($"STATUS | order={o.Id} | source=processor | status={s} | reason={l}");
        }

        private void ReportProgress(OrderData order, int value, string stage)
        {
            var boundedValue = Math.Clamp(value, 0, 100);
            OnProgressChanged?.Invoke(order.Id, boundedValue, stage ?? string.Empty);
        }

        private Action<double> CreateRangedProgressReporter(OrderData order, int fromPercent, int toPercent, string stage)
        {
            var from = Math.Clamp(fromPercent, 0, 100);
            var to = Math.Clamp(toPercent, from, 100);
            var lastProgress = -1;

            return ratio =>
            {
                var boundedRatio = Math.Clamp(ratio, 0d, 1d);
                var nextProgress = from + (int)Math.Round((to - from) * boundedRatio);
                if (nextProgress <= lastProgress)
                    return;

                lastProgress = nextProgress;
                ReportProgress(order, nextProgress, stage);
            };
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
            Directory.CreateDirectory(path);
            string dest = Path.Combine(path, name);
            File.Copy(src, dest, true);
            return dest;
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
            Directory.CreateDirectory(grandpaPath);

            string target = Path.Combine(grandpaPath, Path.GetFileName(order.PrintPath));
            MoveFileWithOverwrite(order.PrintPath, target);
            order.PrintPath = target;
            TryCopyPathToClipboard(target);

            if (!string.IsNullOrEmpty(order.SourcePath) && File.Exists(order.SourcePath))
            {
                try { File.Delete(order.SourcePath); } catch { }
            }
        }

        private string CopyToGrandpa(string sourcePath, string fileName, string grandpaPath)
        {
            if (string.IsNullOrWhiteSpace(grandpaPath)) return sourcePath;
            Directory.CreateDirectory(grandpaPath);
            string target = Path.Combine(grandpaPath, fileName);
            File.Copy(sourcePath, target, true);
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
            catch
            {
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
            Directory.CreateDirectory(path);
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
                try { File.Delete(targetPath); } catch { }
            }
            File.Move(sourcePath, targetPath);
        }

        private void EnsureTempFolders(string tempRoot)
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, TempInFolder));
            Directory.CreateDirectory(Path.Combine(tempRoot, TempPrepressFolder));
            Directory.CreateDirectory(Path.Combine(tempRoot, TempPrintFolder));
        }

        private void TryDeleteEmptyFolders(string tempRoot)
        {
            try
            {
                foreach (var folder in new[] { TempInFolder, TempPrepressFolder, TempPrintFolder })
                {
                    string path = Path.Combine(tempRoot, folder);
                    if (Directory.Exists(path) && Directory.GetFiles(path).Length == 0)
                        Directory.Delete(path, true);
                }
            }
            catch { }
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

        private bool IsFileReady(string path)
        {
            try { using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
            catch { return false; }
        }
    }
}
