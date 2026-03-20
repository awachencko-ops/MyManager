using System;
using System.IO;

namespace Replica;

public enum RenamePathBuildStatus
{
    Success = 0,
    SourceMissing = 1,
    EmptyInput = 2,
    Unchanged = 3,
    InvalidDirectory = 4,
    TargetExists = 5
}

public sealed record RenamePathBuildResult(RenamePathBuildStatus Status, string RenamedPath)
{
    public bool IsSuccess => Status == RenamePathBuildStatus.Success;
}

public sealed record OrderItemFileRemoveOutcome(
    FileSyncStatusUpdate StatusUpdate,
    bool ItemRemovedFromOrder,
    OrderItemTopologyMutationResult TopologyMutation,
    bool CanRestoreItemSelection);

public sealed class OrderFileRenameRemoveCommandService
{
    private readonly OrderFilePathMutationService _filePathMutationService;
    private readonly OrderItemMutationService _itemMutationService;

    public OrderFileRenameRemoveCommandService(
        OrderFilePathMutationService? filePathMutationService = null,
        OrderItemMutationService? itemMutationService = null)
    {
        _filePathMutationService = filePathMutationService ?? new OrderFilePathMutationService();
        _itemMutationService = itemMutationService ?? new OrderItemMutationService();
    }

    public FileSyncStatusUpdate ApplyOrderFileRemoved(OrderData order, int stage)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        return _filePathMutationService.ApplyOrderFilePath(order, stage, string.Empty);
    }

    public FileSyncStatusUpdate ApplyOrderFileRenamed(OrderData order, int stage, string renamedPath)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        return _filePathMutationService.ApplyOrderFilePath(order, stage, renamedPath);
    }

    public FileSyncStatusUpdate ApplyItemFileRenamed(OrderData order, OrderFileItem item, int stage, string renamedPath)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        return _filePathMutationService.ApplyItemFilePath(order, item, stage, renamedPath);
    }

    public FileSyncStatusUpdate ApplyPrintTileFileRenamed(OrderData order, string oldPath, string renamedPath)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        var updatedOrderPath = false;
        var updatedItemPath = false;
        if (PathsEqual(order.PrintPath, oldPath))
        {
            order.PrintPath = renamedPath;
            updatedOrderPath = true;
        }

        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                if (item == null || !PathsEqual(item.PrintPath, oldPath))
                    continue;

                item.PrintPath = renamedPath;
                updatedItemPath = true;
            }
        }

        if (!updatedOrderPath && !updatedItemPath)
        {
            order.PrintPath = renamedPath;
            updatedOrderPath = true;
        }

        if (updatedItemPath)
            return _filePathMutationService.CalculateOrderStatusFromItems(order);

        return new FileSyncStatusUpdate(
            OrderFilePathMutationService.ResolveWorkflowStatus(order.SourcePath, order.PreparedPath, order.PrintPath),
            "print-tile-rename");
    }

    public OrderItemFileRemoveOutcome ApplyItemFileRemoved(
        OrderData order,
        OrderFileItem item,
        int stage,
        bool wasMultiOrderBeforeMutation)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        var statusUpdate = _filePathMutationService.ApplyItemFilePath(order, item, stage, string.Empty);
        var itemRemovedFromOrder = _itemMutationService.RemoveItemIfEmpty(order, item);
        var topologyMutation = _itemMutationService.ApplyTopologyAfterItemMutation(order, wasMultiOrderBeforeMutation);
        var canRestoreItemSelection = !itemRemovedFromOrder && _itemMutationService.ContainsOrderItem(order, item.ItemId);

        return new OrderItemFileRemoveOutcome(
            statusUpdate,
            itemRemovedFromOrder,
            topologyMutation,
            canRestoreItemSelection);
    }

    public RenamePathBuildResult TryBuildRenamedPath(string currentPath, string? requestedName)
    {
        if (!HasExistingFile(currentPath))
            return new RenamePathBuildResult(RenamePathBuildStatus.SourceMissing, string.Empty);

        if (string.IsNullOrWhiteSpace(requestedName))
            return new RenamePathBuildResult(RenamePathBuildStatus.EmptyInput, string.Empty);

        var oldName = Path.GetFileNameWithoutExtension(currentPath);
        var extension = Path.GetExtension(currentPath);
        var nextName = requestedName.Trim();
        if (string.Equals(nextName, oldName, StringComparison.Ordinal))
            return new RenamePathBuildResult(RenamePathBuildStatus.Unchanged, string.Empty);

        foreach (var invalid in Path.GetInvalidFileNameChars())
            nextName = nextName.Replace(invalid, '_');

        var directory = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(directory))
            return new RenamePathBuildResult(RenamePathBuildStatus.InvalidDirectory, string.Empty);

        var targetPath = Path.Combine(directory, nextName + extension);
        if (PathsEqual(currentPath, targetPath))
            return new RenamePathBuildResult(RenamePathBuildStatus.Unchanged, string.Empty);

        if (File.Exists(targetPath))
            return new RenamePathBuildResult(RenamePathBuildStatus.TargetExists, string.Empty);

        return new RenamePathBuildResult(RenamePathBuildStatus.Success, targetPath);
    }

    private static bool HasExistingFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private static bool PathsEqual(string? leftPath, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
            return false;

        var left = NormalizePath(leftPath);
        var right = NormalizePath(rightPath);
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }
}
