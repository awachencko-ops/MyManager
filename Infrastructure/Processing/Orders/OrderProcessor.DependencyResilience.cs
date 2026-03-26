using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Replica
{
    public partial class OrderProcessor
    {
        private static readonly TimeSpan DependencyReadinessProbeTimeout = TimeSpan.FromSeconds(10);

        private void EnsureWorkflowDependenciesReady(OrderData order, HashSet<string>? selectedSet, string tempRoot)
        {
            EnsureStorageDependencyReady(tempRoot);

            var pitActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var imposingActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectRequiredDependencyActions(order, selectedSet, pitActions, imposingActions);

            foreach (var actionName in pitActions)
            {
                var pitCfg = ConfigService.GetPitStopConfigByName(actionName);
                if (pitCfg == null)
                    continue;

                EnsureDependencyDirectoryReady(DependencyPitStop, "readiness-pitstop-in", pitCfg.InputFolder);
                EnsureDependencyDirectoryReady(DependencyPitStop, "readiness-pitstop-success", pitCfg.ProcessedSuccess);
                EnsureDependencyDirectoryReady(DependencyPitStop, "readiness-pitstop-error", pitCfg.ProcessedError);
            }

            foreach (var actionName in imposingActions)
            {
                var impCfg = ConfigService.GetImposingConfigByName(actionName);
                if (impCfg == null)
                    continue;

                EnsureDependencyDirectoryReady(DependencyImposing, "readiness-imposing-in", impCfg.In);
                EnsureDependencyDirectoryReady(DependencyImposing, "readiness-imposing-out", impCfg.Out);
            }
        }

        private void EnsureStorageDependencyReady(string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(_rootPath))
                throw new InvalidOperationException("Не задан корневой путь хранилища заказов.");

            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "readiness-storage-root-create",
                path: _rootPath,
                action: () => Directory.CreateDirectory(_rootPath));
            EnsureDependencyDirectoryReady(DependencyStorage, "readiness-storage-root-access", _rootPath);

            if (string.IsNullOrWhiteSpace(tempRoot))
                throw new InvalidOperationException("Не задан временный путь для обработки заказов.");

            ExecuteWithDependencyRetry(
                dependencyName: DependencyStorage,
                operation: "readiness-temp-root-create",
                path: tempRoot,
                action: () => Directory.CreateDirectory(tempRoot));
            EnsureTempFolders(tempRoot);
        }

        private static void CollectRequiredDependencyActions(
            OrderData order,
            HashSet<string>? selectedSet,
            HashSet<string> pitActions,
            HashSet<string> imposingActions)
        {
            if (OrderTopologyService.IsMultiOrder(order))
            {
                var runItems = order.Items.Where(x => x != null);
                if (selectedSet != null && selectedSet.Count > 0)
                    runItems = runItems.Where(x => selectedSet.Contains(x.ItemId));

                foreach (var item in runItems)
                {
                    var pitAction = string.IsNullOrWhiteSpace(item.PitStopAction) || item.PitStopAction == "-"
                        ? order.PitStopAction
                        : item.PitStopAction;
                    var impAction = string.IsNullOrWhiteSpace(item.ImposingAction) || item.ImposingAction == "-"
                        ? order.ImposingAction
                        : item.ImposingAction;

                    AddConfiguredAction(pitActions, pitAction);
                    AddConfiguredAction(imposingActions, impAction);
                }

                return;
            }

            AddConfiguredAction(pitActions, order.PitStopAction);
            AddConfiguredAction(imposingActions, order.ImposingAction);
        }

        private static void AddConfiguredAction(HashSet<string> target, string? actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName) || actionName == "-")
                return;

            target.Add(actionName);
        }

        private void EnsureDependencyDirectoryReady(string dependencyName, string operation, string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new InvalidOperationException($"Dependency '{dependencyName}' path is empty for operation '{operation}'.");

            ExecuteWithDependencyRetry(
                dependencyName: dependencyName,
                operation: operation,
                path: directoryPath,
                action: () =>
                {
                    ExecuteDependencyReadinessProbe(
                        dependencyName,
                        operation,
                        directoryPath,
                        () =>
                        {
                            if (!Directory.Exists(directoryPath))
                            {
                                throw new DirectoryNotFoundException(
                                    $"Dependency '{dependencyName}' directory not found: {directoryPath}");
                            }

                            using var enumerator = Directory.EnumerateFileSystemEntries(directoryPath).GetEnumerator();
                            _ = enumerator.MoveNext();
                        });
                });
        }

        private static void ExecuteDependencyReadinessProbe(
            string dependencyName,
            string operation,
            string directoryPath,
            Action probe)
        {
            if (probe == null)
                throw new ArgumentNullException(nameof(probe));

            Exception? capturedException = null;
            using var completed = new ManualResetEventSlim(false);
            var probeThread = new Thread(() =>
            {
                try
                {
                    probe();
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
                finally
                {
                    completed.Set();
                }
            })
            {
                IsBackground = true,
                Name = $"Replica-{dependencyName}-probe"
            };

            probeThread.Start();
            if (!completed.Wait(DependencyReadinessProbeTimeout))
            {
                throw new IOException(
                    $"Dependency '{dependencyName}' readiness probe timed out for operation '{operation}' at path '{directoryPath}' after {(int)DependencyReadinessProbeTimeout.TotalSeconds}s.");
            }

            if (capturedException != null)
                ExceptionDispatchInfo.Capture(capturedException).Throw();
        }

        private bool IsFileReady(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"FILE-READY | unexpected-error | path={path} | {ex.Message}");
                return false;
            }
        }

        private void EnsureDependencyAvailable(string dependencyName, string operation, string path)
        {
            var breaker = GetDependencyCircuitBreaker(dependencyName);
            var nowUtc = DateTime.UtcNow;
            if (breaker.TryAllow(nowUtc, out var retryAfter))
                return;

            PublishDependencyHealth(
                dependencyName,
                breaker.GetLevel(nowUtc),
                $"circuit-open | retry-after={(int)Math.Ceiling(retryAfter.TotalSeconds)}s | op={operation}");
            throw new IOException($"Dependency '{dependencyName}' is temporarily unavailable for operation '{operation}'.");
        }

        private void MarkDependencySuccess(string dependencyName)
        {
            var nowUtc = DateTime.UtcNow;
            var breaker = GetDependencyCircuitBreaker(dependencyName);
            breaker.RecordSuccess();
            PublishDependencyHealth(dependencyName, breaker.GetLevel(nowUtc), "ok");
        }

        private void MarkDependencyFailure(string dependencyName, string operation, string path, Exception ex)
        {
            var nowUtc = DateTime.UtcNow;
            var breaker = GetDependencyCircuitBreaker(dependencyName);
            breaker.RecordFailure(nowUtc);
            PublishDependencyHealth(
                dependencyName,
                breaker.GetLevel(nowUtc),
                $"failure | op={operation} | path={path} | {ex.Message}");
        }

        private IDisposable EnterDependencyBulkhead(string dependencyName, string operation, string path)
        {
            if (_dependencyBulkhead.TryEnter(dependencyName, out var lease, out var inFlightAfterEnter, out var limit)
                && lease != null)
            {
                return lease;
            }

            PublishDependencyHealth(
                dependencyName,
                DependencyHealthLevel.Degraded,
                $"bulkhead-reject | op={operation} | path={path} | in-flight={inFlightAfterEnter}/{limit}");
            throw new IOException($"Dependency '{dependencyName}' is overloaded for operation '{operation}' (in-flight {inFlightAfterEnter}/{limit}).");
        }

        private void ExecuteWithDependencyRetry(string dependencyName, string operation, string path, Action action)
        {
            using var lease = EnterDependencyBulkhead(dependencyName, operation, path);
            EnsureDependencyAvailable(dependencyName, operation, path);
            try
            {
                _fileRetryPolicy.Execute(operation, path, action);
                MarkDependencySuccess(dependencyName);
            }
            catch (Exception ex) when (FileOperationRetryPolicy.IsTransient(ex))
            {
                MarkDependencyFailure(dependencyName, operation, path, ex);
                throw;
            }
        }

        private T ExecuteWithDependencyRetry<T>(string dependencyName, string operation, string path, Func<T> action)
        {
            using var lease = EnterDependencyBulkhead(dependencyName, operation, path);
            EnsureDependencyAvailable(dependencyName, operation, path);
            try
            {
                var result = _fileRetryPolicy.Execute(operation, path, action);
                MarkDependencySuccess(dependencyName);
                return result;
            }
            catch (Exception ex) when (FileOperationRetryPolicy.IsTransient(ex))
            {
                MarkDependencyFailure(dependencyName, operation, path, ex);
                throw;
            }
        }

        private DependencyCircuitBreaker GetDependencyCircuitBreaker(string dependencyName)
        {
            if (_dependencyCircuitBreakers.TryGetValue(dependencyName, out var existing))
                return existing;

            var created = new DependencyCircuitBreaker(DependencyFailureThreshold, DependencyOpenDuration);
            _dependencyCircuitBreakers[dependencyName] = created;
            return created;
        }

        private void PublishDependencyHealth(string dependencyName, DependencyHealthLevel level, string reason)
        {
            if (_dependencyHealthLevels.TryGetValue(dependencyName, out var knownLevel)
                && knownLevel == level)
            {
                return;
            }

            _dependencyHealthLevels[dependencyName] = level;
            Logger.Info($"DEPENDENCY | name={dependencyName} | level={level} | {reason}");
            OnDependencyHealthChanged?.Invoke(new DependencyHealthSignal(dependencyName, level, reason, DateTime.UtcNow));
        }

        private void CaptureQuiteImposingLog(OrderData order, ImposingConfig cfg, string fileName)
        {
            foreach (var logPath in GetQuiteImposingLogPaths(cfg, fileName))
            {
                try
                {
                    var lines = ExecuteWithDependencyRetry(
                        dependencyName: DependencyImposing,
                        operation: "read-qhi-log",
                        path: logPath,
                        action: () => File.ReadAllLines(logPath));
                    if (lines.Length == 0)
                        continue;

                    var header = $"source=quite-imposing | Quite Hot Imposing log: {Path.GetFileName(logPath)}";
                    Logger.Info($"QHI-LOG | order={order.Id} | {header}");
                    OnCapturedOrderLog?.Invoke(order.Id, header);

                    foreach (var line in lines)
                    {
                        var normalizedLine = line?.TrimEnd();
                        if (string.IsNullOrWhiteSpace(normalizedLine))
                            continue;

                        var taggedLine = $"source=quite-imposing | {normalizedLine}";
                        Logger.Info($"QHI-LOG | order={order.Id} | {taggedLine}");
                        OnCapturedOrderLog?.Invoke(order.Id, taggedLine);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Не удалось прочитать log Quite Imposing ({Path.GetFileName(logPath)}): {ex.Message}");
                }
            }
        }

        private async Task CapturePitStopReportAsync(OrderData order, ActionConfig pitCfg, string fileName, TimeSpan reportTimeout, CancellationToken ct)
        {
            var effectiveReportTimeout = reportTimeout <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(20)
                : reportTimeout;

            var reportFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_log.pdf";
            var places = new (string folder, string label)[]
            {
                (pitCfg.ReportSuccess, "PitStop Report Success"),
                (pitCfg.ReportError, "PitStop Report Error")
            };

            var (reportPath, where) = await WaitForFileInAnyAsync(places, reportFileName, effectiveReportTimeout, ct);
            if (reportPath == null)
            {
                Logger.Warn($"Не найден отчёт PitStop: order={order.Id} | file={reportFileName}");
                return;
            }

            await CapturePdfFirstPageLogAsync(order, reportPath, "pitstop-report", where);
        }

        private async Task CapturePdfFirstPageLogAsync(OrderData order, string pdfPath, string sourceTag, string? displayName = null)
        {
            try
            {
                var extraction = await PythonPdfTextExtractor.TryExtractFirstPageTextAsync(pdfPath);
                if (!extraction.success)
                {
                    Logger.Warn($"Не удалось извлечь текст PDF ({Path.GetFileName(pdfPath)}): {extraction.error}");
                    return;
                }

                var header = $"source={sourceTag} | {(string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(pdfPath) : displayName)}";
                Logger.Info($"PDF-LOG | order={order.Id} | {header}");
                OnCapturedOrderLog?.Invoke(order.Id, header);

                foreach (var line in extraction.text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    var taggedLine = $"source={sourceTag} | {trimmed}";
                    Logger.Info($"PDF-LOG | order={order.Id} | {taggedLine}");
                    OnCapturedOrderLog?.Invoke(order.Id, taggedLine);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Не удалось прочитать PDF-лог ({Path.GetFileName(pdfPath)}): {ex.Message}");
            }
        }
    }
}
