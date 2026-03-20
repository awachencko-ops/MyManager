using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Replica
{
    public partial class OrderProcessor
    {
        public event Action<string, string, string>? OnStatusChanged;
        public event Action<string>? OnLog;
        public event Action<string, int, string>? OnProgressChanged;
        public event Action<string, string>? OnCapturedOrderLog;
        public event Action<DependencyHealthSignal>? OnDependencyHealthChanged;

        private readonly string _rootPath;
        private readonly ISettingsProvider _settingsProvider;
        private readonly FileOperationRetryPolicy _fileRetryPolicy;
        private readonly WorkflowTimeoutBudgetPolicy _timeoutBudgetPolicy;
        private readonly DependencyBulkheadPolicy _dependencyBulkhead;
        private readonly Dictionary<string, DependencyCircuitBreaker> _dependencyCircuitBreakers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DependencyHealthLevel> _dependencyHealthLevels = new(StringComparer.OrdinalIgnoreCase);
        private const string TempInFolder = "in";
        private const string TempPrepressFolder = "prepress";
        private const string TempPrintFolder = "print";
        private const string DependencyPitStop = "pitstop";
        private const string DependencyImposing = "imposing";
        private const string DependencyStorage = "storage";
        private const int DependencyFailureThreshold = 3;
        private const int DependencyBulkheadDefaultLimit = 4;
        private static readonly IReadOnlyDictionary<string, int> DependencyBulkheadLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [DependencyPitStop] = 3,
            [DependencyImposing] = 3,
            [DependencyStorage] = 6
        };
        private static readonly TimeSpan DependencyOpenDuration = TimeSpan.FromSeconds(30);

        public OrderProcessor(
            string rootPath,
            ISettingsProvider? settingsProvider = null,
            FileOperationRetryPolicy? fileRetryPolicy = null,
            WorkflowTimeoutBudgetPolicy? timeoutBudgetPolicy = null)
        {
            _rootPath = rootPath;
            _settingsProvider = settingsProvider ?? new FileSettingsProvider();
            _fileRetryPolicy = fileRetryPolicy ?? new FileOperationRetryPolicy(
                warnLogger: Logger.Warn,
                errorLogger: Logger.Error);
            _timeoutBudgetPolicy = timeoutBudgetPolicy ?? new WorkflowTimeoutBudgetPolicy();
            _dependencyBulkhead = new DependencyBulkheadPolicy(DependencyBulkheadDefaultLimit, DependencyBulkheadLimits);
        }

        public async Task RunAsync(OrderData order, CancellationToken ct, IEnumerable<string>? selectedItemIds = null)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            var isMultiOrder = OrderTopologyService.IsMultiOrder(order);
            using var correlationScope = Logger.BeginCorrelationScope();
            using var logScope = Logger.BeginScope(
                ("component", "order_processor"),
                ("workflow_mode", isMultiOrder ? "multi" : "single"),
                ("order_id", order.Id),
                ("order_internal_id", order.InternalId));

            var topologyResult = OrderTopologyService.Normalize(order);
            if (topologyResult.Issues.Count > 0)
            {
                foreach (var issue in topologyResult.Issues)
                    Logger.Warn($"TOPOLOGY | order={order.Id} | {issue}");
            }

            var settings = _settingsProvider.Load();
            var timeout = TimeSpan.FromMinutes(settings.RunTimeoutMinutes);
            var timeoutBudget = _timeoutBudgetPolicy.Calculate(timeout);
            string tempRoot = string.IsNullOrWhiteSpace(settings.TempFolderPath)
                ? Path.Combine(_rootPath, settings.TempFolderName)
                : settings.TempFolderPath;

            var selectedSet = selectedItemIds == null
                ? null
                : new HashSet<string>(selectedItemIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);

            if (isMultiOrder)
            {
                ReportProgress(order, 0, "Запуск мульти-заказа");
                await RunMultiOrderAsync(order, settings, timeoutBudget, tempRoot, selectedSet, ct);
                return;
            }

            ReportProgress(order, 0, "Запуск");
            try
            {
                Logger.Info($">>> СТАРТ: Заказ {order.Id}");
                Notify(order, "🟡 Запуск…", "Поиск конфигов...");
                ReportProgress(order, 5, "Поиск конфигов");
                EnsureWorkflowDependenciesReady(order, selectedSet, tempRoot);
                Logger.Info(
                    $"TIMEOUT-BUDGET | order={order.Id} | pitstop_s={(int)timeoutBudget.PitStop.TotalSeconds} | imposing_s={(int)timeoutBudget.Imposing.TotalSeconds} | pitstop_report_s={(int)timeoutBudget.PitStopReport.TotalSeconds}");

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

                    try
                    {
                        Notify(order, "🟡 PitStop: копирование", "Копирую в Hotfolder PitStop...");
                        Logger.Info($"Копирование: {order.PreparedPath} -> {targetIn}");
                        ExecuteWithDependencyRetry(
                            dependencyName: DependencyPitStop,
                            operation: "copy-to-pitstop",
                            path: targetIn,
                            action: () => File.Copy(order.PreparedPath, targetIn, true));

                        if (impCfg != null)
                        {
                            var places = new (string folder, string label)[] {
                                (pitCfg.ProcessedSuccess, "PitStop Success"),
                                (pitCfg.ProcessedError,   "PitStop Error"),
                                (impCfg.In,               "Imposing In"),
                                (impCfg.Out,              "Imposing Out")
                            };
                            var pitWaitReporter = CreateRangedProgressReporter(order, 25, 54, "PitStop: ожидание");
                            var (foundPath, where) = await WaitForFileInAnyAsync(places, fileName, timeoutBudget.PitStop, ct, pitWaitReporter);
                            if (foundPath == null) throw new Exception("Таймаут PitStop.");
                            await CapturePitStopReportAsync(order, pitCfg, fileName, timeoutBudget.PitStopReport, ct);
                            if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                            Notify(order, "🟡 PitStop OK", $"PitStop завершен ({where})");
                            ReportProgress(order, 55, "PitStop завершен");
                        }
                        else
                        {
                            var pitWaitReporter = CreateRangedProgressReporter(order, 25, 58, "PitStop: ожидание");
                            var (okFile, where) = await WaitForFileInAnyAsync(new (string folder, string label)[]
                            {
                                (pitCfg.ProcessedSuccess, "PitStop Success"),
                                (pitCfg.ProcessedError,   "PitStop Error")
                            }, fileName, timeoutBudget.PitStop, ct, pitWaitReporter);
                            if (okFile == null) throw new Exception("Таймаут PitStop.");
                            await CapturePitStopReportAsync(order, pitCfg, fileName, timeoutBudget.PitStopReport, ct);
                            if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                            string newName = $"{Path.GetFileNameWithoutExtension(fileName)}_pitstop{Path.GetExtension(fileName)}";
                            order.PreparedPath = CopyIntoStage(order, 2, okFile, newName, tempRoot);
                            order.PreparedFileSizeBytes = TryGetFileLength(order.PreparedPath);
                            order.PreparedFileHash = TryGetFileHash(order.PreparedPath);
                            Notify(order, "🟡 PitStop готово", "Версия сохранена.");
                            ReportProgress(order, 60, "PitStop завершен");
                        }
                    }
                    finally
                    {
                        CleanupPitStopArtifacts(pitCfg, fileName, targetIn);
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
                    string? outFile = null;

                    if (!File.Exists(targetIn))
                    {
                        Notify(order, "🟡 Imposing: старт", "Копирую в Hotfolder Imposing...");
                        ExecuteWithDependencyRetry(
                            dependencyName: DependencyImposing,
                            operation: "copy-to-imposing",
                            path: targetIn,
                            action: () => File.Copy(order.PreparedPath, targetIn, true));
                    }

                    try
                    {
                        var imposingWaitReporter = CreateRangedProgressReporter(order, 72, 89, "Imposing: ожидание");
                        outFile = await WaitForFileAsync(impCfg.Out, fileName, timeoutBudget.Imposing, ct, imposingWaitReporter);
                        if (outFile == null) throw new Exception("Таймаут Imposing.");

                        CaptureQuiteImposingLog(order, impCfg, fileName);

                        string printName = $"{order.Id}.pdf";
                        if (ShouldStoreInOrderFolder(order))
                        {
                            order.PrintPath = CopyIntoStage(order, 3, outFile, printName, tempRoot);
                            order.PrintFileSizeBytes = TryGetFileLength(order.PrintPath);
                            order.PrintFileHash = TryGetFileHash(order.PrintPath);
                            TryDeleteFileQuietly(outFile, $"imposing-single-order:{order.Id}", DependencyImposing);
                        }
                        else
                        {
                            order.PrintPath = CopyToGrandpa(outFile, printName, settings.GrandpaPath);
                            order.PrintFileSizeBytes = TryGetFileLength(order.PrintPath);
                            order.PrintFileHash = TryGetFileHash(order.PrintPath);
                            TryDeleteFileQuietly(outFile, $"imposing-single-grandpa:{order.Id}", DependencyImposing);
                        }

                        ReportProgress(order, 90, "Imposing завершен");
                    }
                    finally
                    {
                        CleanupQuiteImposingArtifacts(impCfg, fileName, targetIn, outFile);
                    }
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

        private async Task RunMultiOrderAsync(OrderData order, AppSettings settings, WorkflowTimeoutBudget timeoutBudget, string tempRoot, HashSet<string>? selectedSet, CancellationToken ct)
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
                        timeoutBudget,
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
            WorkflowTimeoutBudget timeoutBudget,
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

                try
                {
                    targetIn = EnsureUniquePath(targetIn);
                    ExecuteWithDependencyRetry(
                        dependencyName: DependencyPitStop,
                        operation: "copy-item-to-pitstop",
                        path: targetIn,
                        action: () => File.Copy(item.PreparedPath, targetIn, true));

                    if (impCfg != null)
                    {
                        var places = new (string folder, string label)[] {
                            (pitCfg.ProcessedSuccess, "PitStop Success"),
                            (pitCfg.ProcessedError,   "PitStop Error"),
                            (impCfg.In,               "Imposing In"),
                            (impCfg.Out,              "Imposing Out")
                        };
                        var pitWaitReporter = CreateItemProgressRangeReporter(20, 55);
                        var (foundPath, where) = await WaitForFileInAnyAsync(places, fileName, timeoutBudget.PitStop, ct, pitWaitReporter);
                        if (foundPath == null) throw new Exception("Таймаут PitStop.");
                        await CapturePitStopReportAsync(order, pitCfg, fileName, timeoutBudget.PitStopReport, ct);
                        if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                    }
                    else
                    {
                        var pitWaitReporter = CreateItemProgressRangeReporter(20, 58);
                        var (okFile, where) = await WaitForFileInAnyAsync(new (string folder, string label)[]
                        {
                            (pitCfg.ProcessedSuccess, "PitStop Success"),
                            (pitCfg.ProcessedError,   "PitStop Error")
                        }, fileName, timeoutBudget.PitStop, ct, pitWaitReporter);
                        if (okFile == null) throw new Exception("Таймаут PitStop.");
                        await CapturePitStopReportAsync(order, pitCfg, fileName, timeoutBudget.PitStopReport, ct);
                        if (where == "PitStop Error") throw new Exception("Ошибка PitStop (см. отчет).");
                        string newName = $"{Path.GetFileNameWithoutExtension(fileName)}_pitstop{Path.GetExtension(fileName)}";
                        item.PreparedPath = CopyIntoStage(order, 2, okFile, newName, tempRoot);
                        item.PreparedFileSizeBytes = TryGetFileLength(item.PreparedPath);
                        item.PreparedFileHash = TryGetFileHash(item.PreparedPath);
                    }
                }
                finally
                {
                    CleanupPitStopArtifacts(pitCfg, fileName, targetIn);
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
                  string? outFile = null;
                  if (!File.Exists(targetIn))
                  {
                      targetIn = EnsureUniquePath(targetIn);
                      ExecuteWithDependencyRetry(
                          dependencyName: DependencyImposing,
                          operation: "copy-item-to-imposing",
                          path: targetIn,
                          action: () => File.Copy(item.PreparedPath, targetIn, true));
                  }

                  try
                  {
                      var imposingWaitReporter = CreateItemProgressRangeReporter(72, 95);
                      outFile = await WaitForFileAsync(impCfg.Out, fileName, timeoutBudget.Imposing, ct, imposingWaitReporter);
                      if (outFile == null) throw new Exception("Таймаут Imposing.");

                      CaptureQuiteImposingLog(order, impCfg, fileName);

                      string printNameBase = string.IsNullOrWhiteSpace(order.Id) ? "order" : order.Id;
                      string printName = $"{printNameBase}_{itemIndex}.pdf";
                      if (ShouldStoreInOrderFolder(order))
                      {
                          item.PrintPath = CopyIntoStage(order, 3, outFile, printName, tempRoot);
                          item.PrintFileSizeBytes = TryGetFileLength(item.PrintPath);
                          item.PrintFileHash = TryGetFileHash(item.PrintPath);
                          TryDeleteFileQuietly(outFile, $"imposing-multi-item:{order.Id}:{item.ItemId}", DependencyImposing);
                      }
                      else
                      {
                          item.PrintPath = CopyToGrandpa(outFile, printName, settings.GrandpaPath);
                          item.PrintFileSizeBytes = TryGetFileLength(item.PrintPath);
                          item.PrintFileHash = TryGetFileHash(item.PrintPath);
                          TryDeleteFileQuietly(outFile, $"imposing-multi-grandpa:{order.Id}:{item.ItemId}", DependencyImposing);
                      }
                  }
                  finally
                  {
                      CleanupQuiteImposingArtifacts(impCfg, fileName, targetIn, outFile);
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
    }
}
