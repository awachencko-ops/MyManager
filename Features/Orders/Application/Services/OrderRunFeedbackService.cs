using System;
using System.Collections.Generic;
using System.Linq;

namespace Replica;

public enum OrderRunFeedbackSeverity
{
    Information = 0,
    Warning = 1
}

public sealed class OrderRunFeedbackDialog
{
    public OrderRunFeedbackDialog(string caption, string message, OrderRunFeedbackSeverity severity)
    {
        Caption = string.IsNullOrWhiteSpace(caption) ? "Запуск" : caption.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        Severity = severity;
    }

    public string Caption { get; }
    public string Message { get; }
    public OrderRunFeedbackSeverity Severity { get; }
}

public sealed class OrderRunFeedbackLogEntry
{
    public OrderRunFeedbackLogEntry(string message, bool isWarning)
    {
        Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        IsWarning = isWarning;
    }

    public string Message { get; }
    public bool IsWarning { get; }
}

public sealed class OrderRunStartUiFeedback
{
    private OrderRunStartUiFeedback(
        bool shouldAbort,
        string bottomStatus,
        OrderRunFeedbackDialog? dialog,
        IReadOnlyList<OrderRunFeedbackLogEntry> logs)
    {
        ShouldAbort = shouldAbort;
        BottomStatus = bottomStatus ?? string.Empty;
        Dialog = dialog;
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public bool ShouldAbort { get; }
    public string BottomStatus { get; }
    public OrderRunFeedbackDialog? Dialog { get; }
    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }

    public static OrderRunStartUiFeedback Continue()
        => new(
            shouldAbort: false,
            bottomStatus: string.Empty,
            dialog: null,
            logs: Array.Empty<OrderRunFeedbackLogEntry>());

    public static OrderRunStartUiFeedback Abort(
        string bottomStatus,
        OrderRunFeedbackDialog? dialog,
        IReadOnlyList<OrderRunFeedbackLogEntry>? logs = null)
        => new(
            shouldAbort: true,
            bottomStatus: bottomStatus ?? string.Empty,
            dialog: dialog,
            logs: logs ?? Array.Empty<OrderRunFeedbackLogEntry>());
}

public sealed class OrderRunStopUiFeedback
{
    public OrderRunStopUiFeedback(
        string bottomStatus,
        bool shouldUpdateActionButtons,
        OrderRunFeedbackDialog? dialog,
        IReadOnlyList<OrderRunFeedbackLogEntry>? logs = null)
    {
        BottomStatus = bottomStatus ?? string.Empty;
        ShouldUpdateActionButtons = shouldUpdateActionButtons;
        Dialog = dialog;
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public string BottomStatus { get; }
    public bool ShouldUpdateActionButtons { get; }
    public OrderRunFeedbackDialog? Dialog { get; }
    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }
}

public sealed class OrderRunStartProgressUiFeedback
{
    public OrderRunStartProgressUiFeedback(string bottomStatus, OrderRunFeedbackDialog? dialog)
    {
        BottomStatus = bottomStatus ?? string.Empty;
        Dialog = dialog;
    }

    public string BottomStatus { get; }
    public OrderRunFeedbackDialog? Dialog { get; }
}

public sealed class OrderRunCompletionUiFeedback
{
    public OrderRunCompletionUiFeedback(string bottomStatus, OrderRunFeedbackDialog? dialog)
    {
        BottomStatus = bottomStatus ?? string.Empty;
        Dialog = dialog;
    }

    public string BottomStatus { get; }
    public OrderRunFeedbackDialog? Dialog { get; }
}

public sealed class OrderRunLifecycleUiFeedback
{
    public OrderRunLifecycleUiFeedback(IReadOnlyList<OrderRunFeedbackLogEntry>? logs = null)
    {
        Logs = logs ?? Array.Empty<OrderRunFeedbackLogEntry>();
    }

    public IReadOnlyList<OrderRunFeedbackLogEntry> Logs { get; }
}

public sealed class OrderRunStatusUiMutation
{
    public OrderRunStatusUiMutation(
        string status,
        string source,
        string reason,
        bool persistHistory,
        bool rebuildGrid)
    {
        Status = string.IsNullOrWhiteSpace(status) ? WorkflowStatusNames.Error : status.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? OrderStatusSourceNames.Ui : source.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? "неизвестная ошибка" : reason.Trim();
        PersistHistory = persistHistory;
        RebuildGrid = rebuildGrid;
    }

    public string Status { get; }
    public string Source { get; }
    public string Reason { get; }
    public bool PersistHistory { get; }
    public bool RebuildGrid { get; }
}

public sealed class OrderRunStartUiMutation
{
    public OrderRunStartUiMutation(string operationLogMessage, OrderRunStatusUiMutation statusMutation)
    {
        OperationLogMessage = string.IsNullOrWhiteSpace(operationLogMessage) ? "Запуск заказа" : operationLogMessage.Trim();
        StatusMutation = statusMutation ?? throw new ArgumentNullException(nameof(statusMutation));
    }

    public string OperationLogMessage { get; }
    public OrderRunStatusUiMutation StatusMutation { get; }
}

public sealed class OrderRunStopLocalUiMutation
{
    public OrderRunStopLocalUiMutation(string operationLogMessage, OrderRunStatusUiMutation statusMutation)
    {
        OperationLogMessage = string.IsNullOrWhiteSpace(operationLogMessage) ? "Остановлено пользователем" : operationLogMessage.Trim();
        StatusMutation = statusMutation ?? throw new ArgumentNullException(nameof(statusMutation));
    }

    public string OperationLogMessage { get; }
    public OrderRunStatusUiMutation StatusMutation { get; }
}

public sealed class OrderRunUiEffectsPlan
{
    public OrderRunUiEffectsPlan(
        bool shouldUpdateTrayProgress = false,
        bool shouldSaveHistory = false,
        bool shouldRefreshGrid = false,
        bool shouldUpdateActionButtons = false)
    {
        ShouldUpdateTrayProgress = shouldUpdateTrayProgress;
        ShouldSaveHistory = shouldSaveHistory;
        ShouldRefreshGrid = shouldRefreshGrid;
        ShouldUpdateActionButtons = shouldUpdateActionButtons;
    }

    public bool ShouldUpdateTrayProgress { get; }
    public bool ShouldSaveHistory { get; }
    public bool ShouldRefreshGrid { get; }
    public bool ShouldUpdateActionButtons { get; }
}

public sealed class OrderRunFeedbackService
{
    private const int DefaultPreviewLimit = 5;

    public string BuildServerSkippedPreview(IReadOnlyCollection<string>? serverSkipped, int previewLimit = DefaultPreviewLimit)
    {
        var safePreviewLimit = Math.Max(1, previewLimit);
        var skipped = (serverSkipped ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();
        if (skipped.Count == 0)
            return string.Empty;

        var preview = string.Join(Environment.NewLine, skipped.Take(safePreviewLimit));
        if (skipped.Count > safePreviewLimit)
            preview += $"{Environment.NewLine}... ещё: {skipped.Count - safePreviewLimit}";

        return preview;
    }

    private static string BuildAlreadyActiveServerSkippedPreview(IReadOnlyCollection<string>? serverSkipped, int previewLimit = DefaultPreviewLimit)
    {
        var safePreviewLimit = Math.Max(1, previewLimit);
        var skipped = (serverSkipped ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => NormalizeAlreadyActiveSkipEntry(item.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        if (skipped.Count == 0)
            return string.Empty;

        var preview = string.Join(Environment.NewLine, skipped.Take(safePreviewLimit));
        if (skipped.Count > safePreviewLimit)
            preview += $"{Environment.NewLine}... ещё: {skipped.Count - safePreviewLimit}";

        return preview;
    }

    public string BuildSkippedDetails(OrderRunStateService.RunPlan runPlan, IReadOnlyCollection<string>? serverSkipped)
    {
        if (runPlan == null)
            throw new ArgumentNullException(nameof(runPlan));

        var skippedParts = new List<string>();
        var localSkippedDetails = OrderRunStateService.BuildSkippedDetails(runPlan);
        if (!string.IsNullOrWhiteSpace(localSkippedDetails))
            skippedParts.Add(localSkippedDetails);

        var serverSkippedCount = serverSkipped?.Count ?? 0;
        if (serverSkippedCount > 0)
            skippedParts.Add($"сервер отклонил: {serverSkippedCount}");

        return string.Join(", ", skippedParts);
    }

    public string BuildExecutionErrorsPreview(
        IReadOnlyCollection<OrderRunExecutionError>? errors,
        Func<OrderData, string> orderDisplayIdResolver,
        int previewLimit = DefaultPreviewLimit)
    {
        if (orderDisplayIdResolver == null)
            throw new ArgumentNullException(nameof(orderDisplayIdResolver));

        var safePreviewLimit = Math.Max(1, previewLimit);
        var safeErrors = (errors ?? Array.Empty<OrderRunExecutionError>())
            .Where(error => error != null && error.Order != null)
            .ToList();
        if (safeErrors.Count == 0)
            return string.Empty;

        var lines = safeErrors
            .Select(error =>
            {
                var message = string.IsNullOrWhiteSpace(error.Message)
                    ? "неизвестная ошибка"
                    : error.Message.Trim();
                return $"{orderDisplayIdResolver(error.Order)}: {message}";
            })
            .Take(safePreviewLimit)
            .ToList();

        var preview = string.Join(Environment.NewLine, lines);
        if (safeErrors.Count > safePreviewLimit)
            preview += $"{Environment.NewLine}... ещё: {safeErrors.Count - safePreviewLimit}";

        return preview;
    }

    public OrderRunStartUiFeedback BuildStartUiFeedback(OrderRunStartPhaseResult startPhase)
    {
        if (startPhase == null)
            throw new ArgumentNullException(nameof(startPhase));

        var preparation = startPhase.Preparation ?? throw new ArgumentException("Missing run preparation.", nameof(startPhase));
        switch (startPhase.Status)
        {
            case OrderRunStartPhaseStatus.Fatal:
            {
                var fatalReason = string.IsNullOrWhiteSpace(preparation.FatalError)
                    ? "LAN API недоступен"
                    : preparation.FatalError.Trim();
                return OrderRunStartUiFeedback.Abort(
                    bottomStatus: $"Сервер недоступен: {fatalReason}",
                    dialog: new OrderRunFeedbackDialog(
                        "Запуск",
                        $"Не удалось запустить заказ через LAN API: {fatalReason}",
                        OrderRunFeedbackSeverity.Warning),
                    logs: [new OrderRunFeedbackLogEntry($"RUN | command-fatal | {fatalReason}", isWarning: true)]);
            }

            case OrderRunStartPhaseStatus.NoRunnable:
            {
                var details = string.IsNullOrWhiteSpace(startPhase.NoRunnableDetails)
                    ? OrderRunStateService.BuildNoRunnableDetails(preparation.RunPlan)
                    : startPhase.NoRunnableDetails.Trim();
                return OrderRunStartUiFeedback.Abort(
                    bottomStatus: $"Нет заказов для запуска ({details})",
                    dialog: new OrderRunFeedbackDialog(
                        "Запуск",
                        $"Нет заказов для запуска ({details}).",
                        OrderRunFeedbackSeverity.Information));
            }

            case OrderRunStartPhaseStatus.ServerRejected:
            {
                var skippedPreview = BuildServerSkippedPreview(preparation.SkippedByServer);
                var allAlreadyActive = IsAlreadyActiveServerSkip(preparation.SkippedByServer);
                if (allAlreadyActive)
                    skippedPreview = BuildAlreadyActiveServerSkippedPreview(preparation.SkippedByServer);
                var dialogMessage = string.IsNullOrWhiteSpace(skippedPreview)
                    ? (allAlreadyActive
                        ? "Выбранные заказы уже выполняются на сервере."
                        : "Сервер не подтвердил запуск выбранных заказов.")
                    : (allAlreadyActive
                        ? $"Выбранные заказы уже выполняются на сервере:{Environment.NewLine}{skippedPreview}"
                        : $"Сервер не подтвердил запуск выбранных заказов:{Environment.NewLine}{skippedPreview}");
                return OrderRunStartUiFeedback.Abort(
                    bottomStatus: allAlreadyActive
                        ? "Заказы уже выполняются на сервере"
                        : "Сервер не подтвердил запуск выбранных заказов",
                    dialog: new OrderRunFeedbackDialog(
                        "Запуск",
                        dialogMessage,
                        OrderRunFeedbackSeverity.Information),
                    logs: [new OrderRunFeedbackLogEntry(
                        allAlreadyActive ? "RUN | command-already-active" : "RUN | command-rejected-by-server",
                        isWarning: !allAlreadyActive)]);
            }

            default:
                return OrderRunStartUiFeedback.Continue();
        }
    }

    public OrderRunStartUiMutation BuildRunStartUiMutation(bool isBatchRun)
    {
        var operationMessage = isBatchRun
            ? "Пакетный запуск заказа из OrdersWorkspaceForm"
            : "Запуск заказа из OrdersWorkspaceForm";
        var statusReason = isBatchRun
            ? "Пакетный запуск из OrdersWorkspaceForm"
            : "Запуск из OrdersWorkspaceForm";

        return new OrderRunStartUiMutation(
            operationMessage,
            new OrderRunStatusUiMutation(
                status: WorkflowStatusNames.Processing,
                source: OrderStatusSourceNames.Ui,
                reason: statusReason,
                persistHistory: false,
                rebuildGrid: false));
    }

    public OrderRunStartUiFeedback BuildRunSelectionRequiredUiFeedback()
    {
        return OrderRunStartUiFeedback.Abort(
            bottomStatus: "Выберите строку заказа для запуска",
            dialog: new OrderRunFeedbackDialog(
                "Запуск",
                "Выберите строку заказа для запуска.",
                OrderRunFeedbackSeverity.Information));
    }

    public OrderRunStartProgressUiFeedback BuildStartProgressUiFeedback(
        int runnableOrdersCount,
        OrderRunStateService.RunPlan runPlan,
        IReadOnlyCollection<string>? serverSkipped)
    {
        if (runPlan == null)
            throw new ArgumentNullException(nameof(runPlan));

        var safeRunnableOrdersCount = Math.Max(0, runnableOrdersCount);
        var bottomStatus = safeRunnableOrdersCount == 1
            ? "Запущен заказ"
            : $"Запущено заказов: {safeRunnableOrdersCount}";

        OrderRunFeedbackDialog? dialog = null;
        var hasLocalSkips = runPlan.OrdersWithoutNumber.Count > 0 || runPlan.AlreadyRunningOrders.Count > 0;
        var serverSkippedCount = serverSkipped?.Count ?? 0;
        if (hasLocalSkips || serverSkippedCount > 0)
        {
            var skippedDetails = BuildSkippedDetails(runPlan, serverSkipped);
            bottomStatus = string.IsNullOrWhiteSpace(skippedDetails)
                ? "Часть заказов пропущена"
                : $"Часть заказов пропущена ({skippedDetails})";
        }

        if (serverSkippedCount > 0)
        {
            var skippedPreview = BuildServerSkippedPreview(serverSkipped);
            var allAlreadyActive = IsAlreadyActiveServerSkip(serverSkipped);
            if (allAlreadyActive)
                skippedPreview = BuildAlreadyActiveServerSkippedPreview(serverSkipped);
            var message = string.IsNullOrWhiteSpace(skippedPreview)
                ? (allAlreadyActive
                    ? "Часть заказов уже выполняется на сервере."
                    : "Часть заказов не запущена сервером.")
                : (allAlreadyActive
                    ? $"Часть заказов уже выполняется на сервере:{Environment.NewLine}{skippedPreview}"
                    : $"Часть заказов не запущена сервером:{Environment.NewLine}{skippedPreview}");
            dialog = new OrderRunFeedbackDialog("Запуск", message, OrderRunFeedbackSeverity.Information);
        }

        return new OrderRunStartProgressUiFeedback(bottomStatus, dialog);
    }

    public OrderRunUiEffectsPlan BuildRunPostStatusApplyUiEffectsPlan()
        => new(
            shouldUpdateTrayProgress: true,
            shouldSaveHistory: true,
            shouldRefreshGrid: true,
            shouldUpdateActionButtons: false);

    public OrderRunUiEffectsPlan BuildRunPerOrderCompletionUiEffectsPlan()
        => new(
            shouldUpdateTrayProgress: true,
            shouldSaveHistory: false,
            shouldRefreshGrid: false,
            shouldUpdateActionButtons: false);

    public OrderRunUiEffectsPlan BuildRunPostExecutionUiEffectsPlan()
        => new(
            shouldUpdateTrayProgress: false,
            shouldSaveHistory: true,
            shouldRefreshGrid: true,
            shouldUpdateActionButtons: true);

    public OrderRunCompletionUiFeedback BuildCompletionUiFeedback(
        IReadOnlyCollection<OrderRunExecutionError>? errors,
        int runnableOrdersCount,
        Func<OrderData, string> orderDisplayIdResolver)
    {
        if (orderDisplayIdResolver == null)
            throw new ArgumentNullException(nameof(orderDisplayIdResolver));

        var safeErrors = (errors ?? Array.Empty<OrderRunExecutionError>())
            .Where(error => error != null && error.Order != null)
            .ToList();
        if (safeErrors.Count > 0)
        {
            var errorsPreview = BuildExecutionErrorsPreview(safeErrors, orderDisplayIdResolver);
            var message = string.IsNullOrWhiteSpace(errorsPreview)
                ? "Некоторые заказы завершились с ошибкой."
                : $"Некоторые заказы завершились с ошибкой:{Environment.NewLine}{errorsPreview}";
            return new OrderRunCompletionUiFeedback(
                bottomStatus: $"Ошибок запуска: {safeErrors.Count}",
                dialog: new OrderRunFeedbackDialog("Запуск", message, OrderRunFeedbackSeverity.Warning));
        }

        var safeRunnableOrdersCount = Math.Max(0, runnableOrdersCount);
        if (safeRunnableOrdersCount > 1)
        {
            return new OrderRunCompletionUiFeedback(
                bottomStatus: $"Пакетная обработка завершена: {safeRunnableOrdersCount}",
                dialog: null);
        }

        return new OrderRunCompletionUiFeedback(bottomStatus: string.Empty, dialog: null);
    }

    public OrderRunLifecycleUiFeedback BuildRunCommandStartLifecycleUiFeedback()
    {
        return new OrderRunLifecycleUiFeedback(
            logs:
            [
                new OrderRunFeedbackLogEntry("RUN | command-start", isWarning: false)
            ]);
    }

    public OrderRunLifecycleUiFeedback BuildRunStopCommandStartLifecycleUiFeedback()
    {
        return new OrderRunLifecycleUiFeedback(
            logs:
            [
                new OrderRunFeedbackLogEntry("RUN | stop-command-start", isWarning: false)
            ]);
    }

    public OrderRunLifecycleUiFeedback BuildRunSnapshotRefreshWarningUiFeedback(string phase, string? orderDisplayId = null)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase) ? "run" : phase.Trim();
        var normalizedOrderDisplayId = string.IsNullOrWhiteSpace(orderDisplayId)
            ? string.Empty
            : orderDisplayId.Trim();
        var message = string.IsNullOrWhiteSpace(normalizedOrderDisplayId)
            ? $"RUN | snapshot-refresh-failed | reason={normalizedPhase} | save may conflict on next history write"
            : $"RUN | snapshot-refresh-failed | reason={normalizedPhase} | order={normalizedOrderDisplayId}";

        return new OrderRunLifecycleUiFeedback(
            logs:
            [
                new OrderRunFeedbackLogEntry(message, isWarning: true)
            ]);
    }

    public OrderRunLifecycleUiFeedback BuildRunCommandFinishLifecycleUiFeedback(int startedCount, int errorsCount)
    {
        var safeStartedCount = Math.Max(0, startedCount);
        var safeErrorsCount = Math.Max(0, errorsCount);
        return new OrderRunLifecycleUiFeedback(
            logs:
            [
                new OrderRunFeedbackLogEntry(
                    $"RUN | command-finish | started={safeStartedCount} | errors={safeErrorsCount}",
                    isWarning: false)
            ]);
    }

    public OrderRunStatusUiMutation BuildRunCancelledUiMutation()
    {
        return new OrderRunStatusUiMutation(
            status: WorkflowStatusNames.Cancelled,
            source: OrderStatusSourceNames.Ui,
            reason: "Остановлено пользователем",
            persistHistory: false,
            rebuildGrid: false);
    }

    public OrderRunStatusUiMutation BuildRunFailedUiMutation(string? errorMessage)
    {
        var safeReason = string.IsNullOrWhiteSpace(errorMessage) ? "неизвестная ошибка" : errorMessage.Trim();
        return new OrderRunStatusUiMutation(
            status: WorkflowStatusNames.Error,
            source: OrderStatusSourceNames.Ui,
            reason: safeReason,
            persistHistory: false,
            rebuildGrid: false);
    }

    public OrderRunStopUiFeedback BuildStopUiFeedback(OrderRunStopPhaseResult stopPhase, string orderDisplayId)
    {
        if (stopPhase == null)
            throw new ArgumentNullException(nameof(stopPhase));

        var safeOrderDisplayId = string.IsNullOrWhiteSpace(orderDisplayId) ? "—" : orderDisplayId.Trim();
        var logs = new List<OrderRunFeedbackLogEntry>();
        OrderRunFeedbackDialog? dialog = null;

        if (!stopPhase.Preparation.CanProceed)
        {
            logs.Add(new OrderRunFeedbackLogEntry("RUN | stop-command-skipped | reason=not-running", isWarning: true));
            return new OrderRunStopUiFeedback(
                bottomStatus: $"Заказ {safeOrderDisplayId} сейчас не выполняется",
                shouldUpdateActionButtons: false,
                dialog: new OrderRunFeedbackDialog(
                    "Остановка",
                    $"Заказ {safeOrderDisplayId} сейчас не выполняется.",
                    OrderRunFeedbackSeverity.Information),
                logs: logs);
        }

        if (stopPhase.ShouldWarnServerUnavailable)
        {
            logs.Add(new OrderRunFeedbackLogEntry(
                $"RUN | stop-server-unavailable | order={safeOrderDisplayId} | {stopPhase.ServerReason}",
                isWarning: true));

            var unavailableMessage = stopPhase.Status == OrderRunStopPhaseStatus.LocalStatusApplied
                ? $"Остановка на сервере не подтверждена ({stopPhase.ServerReason}).{Environment.NewLine}Локальная остановка будет выполнена, но серверный lock может остаться активным."
                : $"Остановка на сервере не подтверждена ({stopPhase.ServerReason}).";
            dialog = new OrderRunFeedbackDialog("Остановка", unavailableMessage, OrderRunFeedbackSeverity.Warning);
        }

        if (stopPhase.ShouldLogServerFailure)
        {
            logs.Add(new OrderRunFeedbackLogEntry(
                $"RUN | stop-server-failed | order={safeOrderDisplayId} | {stopPhase.ServerReason}",
                isWarning: true));
        }

        if (stopPhase.Status == OrderRunStopPhaseStatus.LocalStatusApplied)
        {
            logs.Add(new OrderRunFeedbackLogEntry("RUN | stop-command-finish | local-status-applied=1", isWarning: false));
            return new OrderRunStopUiFeedback(
                bottomStatus: $"Остановлен заказ {safeOrderDisplayId}",
                shouldUpdateActionButtons: true,
                dialog: dialog,
                logs: logs);
        }

        if (stopPhase.Status == OrderRunStopPhaseStatus.Conflict)
        {
            dialog = new OrderRunFeedbackDialog(
                "Остановка",
                "Сервер отклонил остановку из-за конфликта версии. Обновите заказ и повторите операцию.",
                OrderRunFeedbackSeverity.Information);
            return new OrderRunStopUiFeedback(
                bottomStatus: $"Остановка не подтверждена: конфликт версии ({safeOrderDisplayId})",
                shouldUpdateActionButtons: false,
                dialog: dialog,
                logs: logs);
        }

        logs.Add(new OrderRunFeedbackLogEntry("RUN | stop-command-finish | local-status-applied=0", isWarning: true));
        return new OrderRunStopUiFeedback(
            bottomStatus: $"Сервер не подтвердил остановку {safeOrderDisplayId}",
            shouldUpdateActionButtons: false,
            dialog: dialog,
            logs: logs);
    }

    public OrderRunUiEffectsPlan BuildStopPostPhaseUiEffectsPlan(
        OrderRunStopPhaseResult stopPhase,
        OrderRunStopUiFeedback stopUiFeedback)
    {
        if (stopPhase == null)
            throw new ArgumentNullException(nameof(stopPhase));
        if (stopUiFeedback == null)
            throw new ArgumentNullException(nameof(stopUiFeedback));

        return new OrderRunUiEffectsPlan(
            shouldUpdateTrayProgress: stopPhase.Preparation.LocalCancellationRequested,
            shouldSaveHistory: false,
            shouldRefreshGrid: false,
            shouldUpdateActionButtons: stopUiFeedback.ShouldUpdateActionButtons);
    }

    public OrderRunStopLocalUiMutation BuildStopLocalUiMutation()
    {
        return new OrderRunStopLocalUiMutation(
            operationLogMessage: "Остановлено пользователем",
            statusMutation: new OrderRunStatusUiMutation(
                status: WorkflowStatusNames.Cancelled,
                source: OrderStatusSourceNames.Ui,
                reason: "Остановлено пользователем",
                persistHistory: true,
                rebuildGrid: true));
    }

    private static bool IsAlreadyActiveServerSkip(IReadOnlyCollection<string>? serverSkipped)
    {
        if (serverSkipped == null || serverSkipped.Count == 0)
            return false;

        var hasAny = false;
        foreach (var entry in serverSkipped)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            hasAny = true;
            if (!IsAlreadyActiveReason(entry.Trim()))
                return false;
        }

        return hasAny;
    }

    private static string NormalizeAlreadyActiveSkipEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return string.Empty;

        var delimiterIndex = entry.IndexOf(':');
        if (delimiterIndex <= 0 || delimiterIndex >= entry.Length - 1)
            return entry.Trim();

        var orderDisplayId = entry[..delimiterIndex].Trim();
        var reason = entry[(delimiterIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(orderDisplayId))
            return entry.Trim();

        return IsAlreadyActiveReason(reason)
            ? orderDisplayId
            : entry.Trim();
    }

    private static bool IsAlreadyActiveReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        var normalized = reason.Trim();
        return normalized.IndexOf("run already active", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("уже запущен на сервере", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("уже выполняется", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public OrderRunStopUiFeedback BuildStopSelectionRequiredUiFeedback()
    {
        return new OrderRunStopUiFeedback(
            bottomStatus: "Выберите заказ для остановки",
            shouldUpdateActionButtons: false,
            dialog: new OrderRunFeedbackDialog(
                "Остановка",
                "Выберите заказ для остановки.",
                OrderRunFeedbackSeverity.Information));
    }
}
