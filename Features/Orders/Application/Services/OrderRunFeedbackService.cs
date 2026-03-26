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
                var dialogMessage = string.IsNullOrWhiteSpace(skippedPreview)
                    ? "Сервер не подтвердил запуск выбранных заказов."
                    : $"Сервер не подтвердил запуск выбранных заказов:{Environment.NewLine}{skippedPreview}";
                return OrderRunStartUiFeedback.Abort(
                    bottomStatus: "Сервер не подтвердил запуск выбранных заказов",
                    dialog: new OrderRunFeedbackDialog(
                        "Запуск",
                        dialogMessage,
                        OrderRunFeedbackSeverity.Information),
                    logs: [new OrderRunFeedbackLogEntry("RUN | command-rejected-by-server", isWarning: true)]);
            }

            default:
                return OrderRunStartUiFeedback.Continue();
        }
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
            var message = string.IsNullOrWhiteSpace(skippedPreview)
                ? "Часть заказов не запущена сервером."
                : $"Часть заказов не запущена сервером:{Environment.NewLine}{skippedPreview}";
            dialog = new OrderRunFeedbackDialog("Запуск", message, OrderRunFeedbackSeverity.Information);
        }

        return new OrderRunStartProgressUiFeedback(bottomStatus, dialog);
    }

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
}
