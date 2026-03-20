using System;
using System.IO;

namespace Replica;

public sealed record OrderFileStageAddPlan(
    string CleanSourcePath,
    string TargetFileName,
    bool UsePrintCopy,
    bool EnsureSourceCopy);

public sealed class OrderFileStageCommandService
{
    public bool TryPrepareOrderAdd(
        OrderData order,
        string sourceFile,
        int stage,
        Func<int, string, string> ensureUniqueStageFileName,
        out OrderFileStageAddPlan plan)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (ensureUniqueStageFileName == null)
            throw new ArgumentNullException(nameof(ensureUniqueStageFileName));

        plan = null!;
        var cleanSource = CleanPath(sourceFile);
        if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
            return false;

        var targetFileName = stage == OrderStages.Print && !string.IsNullOrWhiteSpace(order.Id)
            ? $"{order.Id}{Path.GetExtension(cleanSource)}"
            : ensureUniqueStageFileName(stage, Path.GetFileName(cleanSource));

        plan = new OrderFileStageAddPlan(
            cleanSource,
            targetFileName,
            UsePrintCopy: stage == OrderStages.Print,
            EnsureSourceCopy: stage == OrderStages.Prepared);
        return true;
    }

    public bool TryPrepareItemAdd(
        OrderData order,
        OrderFileItem item,
        string sourceFile,
        int stage,
        Func<int, string, string> ensureUniqueStageFileName,
        Func<string, string> buildItemPrintFileName,
        out OrderFileStageAddPlan plan)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (item == null)
            throw new ArgumentNullException(nameof(item));
        if (ensureUniqueStageFileName == null)
            throw new ArgumentNullException(nameof(ensureUniqueStageFileName));
        if (buildItemPrintFileName == null)
            throw new ArgumentNullException(nameof(buildItemPrintFileName));

        plan = null!;
        var cleanSource = CleanPath(sourceFile);
        if (string.IsNullOrWhiteSpace(cleanSource) || !File.Exists(cleanSource))
            return false;

        if (string.IsNullOrWhiteSpace(item.ClientFileLabel))
            item.ClientFileLabel = Path.GetFileNameWithoutExtension(cleanSource);

        var targetFileName = stage == OrderStages.Print
            ? ensureUniqueStageFileName(OrderStages.Print, buildItemPrintFileName(cleanSource))
            : ensureUniqueStageFileName(stage, Path.GetFileName(cleanSource));

        plan = new OrderFileStageAddPlan(
            cleanSource,
            targetFileName,
            UsePrintCopy: stage == OrderStages.Print,
            EnsureSourceCopy: false);
        return true;
    }

    private static string CleanPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Trim('"');
    }
}
