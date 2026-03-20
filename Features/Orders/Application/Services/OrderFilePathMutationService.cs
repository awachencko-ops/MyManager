using System;
using System.IO;
using System.Linq;

namespace Replica;

public sealed record FileSyncStatusUpdate(string Status, string Reason);

public sealed class OrderFilePathMutationService
{
    private readonly Func<DateTime> _nowProvider;

    public OrderFilePathMutationService(Func<DateTime>? nowProvider = null)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public FileSyncStatusUpdate ApplyOrderFilePath(OrderData order, int stage, string path)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        if (stage == OrderStages.Source)
        {
            order.SourcePath = path;
            order.SourceFileSizeBytes = TryGetFileLength(path, out var sourceSize) ? sourceSize : null;
            order.SourceFileHash = TryGetFileHash(path);
        }
        else if (stage == OrderStages.Prepared)
        {
            order.PreparedPath = path;
            order.PreparedFileSizeBytes = TryGetFileLength(path, out var preparedSize) ? preparedSize : null;
            order.PreparedFileHash = TryGetFileHash(path);
        }
        else if (stage == OrderStages.Print)
        {
            order.PrintPath = path;
            order.PrintFileSizeBytes = TryGetFileLength(path, out var printSize) ? printSize : null;
            order.PrintFileHash = TryGetFileHash(path);
        }

        var status = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);
        if (order.Items != null && order.Items.Count == 1)
        {
            var singleItem = order.Items[0];
            SetItemStagePath(singleItem, stage, path);
            if (stage == OrderStages.Source)
            {
                singleItem.SourceFileSizeBytes = order.SourceFileSizeBytes;
                singleItem.SourceFileHash = order.SourceFileHash;
            }
            else if (stage == OrderStages.Prepared)
            {
                singleItem.PreparedFileSizeBytes = order.PreparedFileSizeBytes;
                singleItem.PreparedFileHash = order.PreparedFileHash;
            }
            else if (stage == OrderStages.Print)
            {
                singleItem.PrintFileSizeBytes = order.PrintFileSizeBytes;
                singleItem.PrintFileHash = order.PrintFileHash;
            }

            singleItem.FileStatus = status;
            singleItem.UpdatedAt = _nowProvider();
        }

        return new FileSyncStatusUpdate(status, DescribeStageReason(stage));
    }

    public FileSyncStatusUpdate ApplyItemFilePath(OrderData order, OrderFileItem item, int stage, string path)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        SetItemStagePath(item, stage, path);
        if (stage == OrderStages.Source)
        {
            item.SourceFileSizeBytes = TryGetFileLength(path, out var sourceSize) ? sourceSize : null;
            item.SourceFileHash = TryGetFileHash(path);
        }
        else if (stage == OrderStages.Prepared)
        {
            item.PreparedFileSizeBytes = TryGetFileLength(path, out var preparedSize) ? preparedSize : null;
            item.PreparedFileHash = TryGetFileHash(path);
        }
        else if (stage == OrderStages.Print)
        {
            item.PrintFileSizeBytes = TryGetFileLength(path, out var printSize) ? printSize : null;
            item.PrintFileHash = TryGetFileHash(path);
        }

        item.FileStatus = ResolveWorkflowStatus(item.SourcePath, item.PreparedPath, item.PrintPath);
        item.UpdatedAt = _nowProvider();

        if (order.Items != null
            && order.Items.Count == 1
            && string.Equals(order.Items[0].ItemId, item.ItemId, StringComparison.Ordinal))
        {
            if (stage == OrderStages.Source)
            {
                order.SourcePath = item.SourcePath;
                order.SourceFileSizeBytes = item.SourceFileSizeBytes;
                order.SourceFileHash = item.SourceFileHash;
            }
            else if (stage == OrderStages.Prepared)
            {
                order.PreparedPath = item.PreparedPath;
                order.PreparedFileSizeBytes = item.PreparedFileSizeBytes;
                order.PreparedFileHash = item.PreparedFileHash;
            }
            else if (stage == OrderStages.Print)
            {
                order.PrintPath = item.PrintPath;
                order.PrintFileSizeBytes = item.PrintFileSizeBytes;
                order.PrintFileHash = item.PrintFileHash;
            }

            return new FileSyncStatusUpdate(item.FileStatus, $"item: {DescribeStageReason(stage)}");
        }

        return CalculateOrderStatusFromItems(order);
    }

    public FileSyncStatusUpdate CalculateOrderStatusFromItems(OrderData order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        if (order.Items == null || order.Items.Count == 0)
        {
            var status = ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath);
            return new FileSyncStatusUpdate(status, "no-items");
        }

        var items = order.Items.Where(x => x != null).ToList();
        if (items.Count == 0)
            return new FileSyncStatusUpdate(WorkflowStatusNames.Waiting, "empty-items");

        var total = items.Count;
        var done = items.Count(x => HasExistingFile(x.PrintPath));
        var active = items.Count(x => HasExistingFile(x.SourcePath)
                                      || HasExistingFile(x.PreparedPath)
                                      || HasExistingFile(x.PrintPath));

        var statusValue = done == total
            ? WorkflowStatusNames.Completed
            : active > 0
                ? WorkflowStatusNames.Processing
                : WorkflowStatusNames.Waiting;

        return new FileSyncStatusUpdate(statusValue, "aggregate");
    }

    public static string ResolveWorkflowStatus(string? sourcePath, string? preparedPath, string? printPath)
    {
        if (HasExistingFile(printPath))
            return WorkflowStatusNames.Completed;

        if (HasExistingFile(preparedPath) || HasExistingFile(sourcePath))
            return WorkflowStatusNames.Processing;

        return WorkflowStatusNames.Waiting;
    }

    public static string DescribeStageReason(int stage)
    {
        return stage switch
        {
            OrderStages.Source => "Найден исходный файл",
            OrderStages.Prepared => "Найден файл подготовки",
            OrderStages.Print => "Найден печатный файл",
            _ => $"stage-{stage}"
        };
    }

    private static bool TryGetFileLength(string? path, out long sizeBytes)
    {
        sizeBytes = 0;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            if (!File.Exists(path))
                return false;

            sizeBytes = new FileInfo(path).Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string TryGetFileHash(string? path)
    {
        return File.Exists(path) && FileHashService.TryComputeSha256(path, out var hash, out _)
            ? hash
            : string.Empty;
    }

    private static void SetItemStagePath(OrderFileItem item, int stage, string path)
    {
        if (stage == OrderStages.Source)
            item.SourcePath = path;
        else if (stage == OrderStages.Prepared)
            item.PreparedPath = path;
        else if (stage == OrderStages.Print)
            item.PrintPath = path;
    }

    private static bool HasExistingFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }
}
