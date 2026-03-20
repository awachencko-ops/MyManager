using System;
using System.Collections.Generic;
using System.Linq;

namespace Replica;

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
}
