using System;
using System.Collections.Generic;
using System.IO;

namespace Replica
{
    public sealed class OrderTopologyNormalizationResult
    {
        public OrderTopologyNormalizationResult(bool changed, IReadOnlyList<string> issues)
        {
            Changed = changed;
            Issues = issues ?? Array.Empty<string>();
        }

        public bool Changed { get; }
        public IReadOnlyList<string> Issues { get; }
    }

    /// <summary>
    /// Единый нормализатор модели заказа:
    /// order = группа файлов, где single-order является частным случаем (один item).
    /// </summary>
    public static class OrderTopologyService
    {
        public static OrderTopologyNormalizationResult Normalize(OrderData? order)
        {
            if (order == null)
                return new OrderTopologyNormalizationResult(false, Array.Empty<string>());

            var issues = new List<string>();
            var changed = false;
            var markerBefore = order.FileTopologyMarker;

            if (order.Items == null)
            {
                order.Items = new List<OrderFileItem>();
                changed = true;
            }

            for (var i = order.Items.Count - 1; i >= 0; i--)
            {
                if (order.Items[i] != null)
                    continue;

                order.Items.RemoveAt(i);
                changed = true;
                issues.Add("Удален null item из заказа.");
            }

            changed |= EnsureItemMetadata(order);

            var hasOrderLevelPaths = HasAnyPath(order.SourcePath, order.PreparedPath, order.PrintPath);

            if (order.Items.Count == 0 && hasOrderLevelPaths)
            {
                order.Items.Add(CreateSyntheticSingleItem(order));
                changed = true;
                issues.Add("Legacy-пути заказа перенесены в единый item для single-сценария.");
            }

            if (order.Items.Count == 1)
            {
                var item = order.Items[0];
                if (item != null)
                {
                    changed |= SyncSingleItemPath(order, item, 1, issues);
                    changed |= SyncSingleItemPath(order, item, 2, issues);
                    changed |= SyncSingleItemPath(order, item, 3, issues);

                    if (string.IsNullOrWhiteSpace(item.ClientFileLabel))
                    {
                        item.ClientFileLabel = BuildDefaultItemLabel(order, item);
                        changed = true;
                    }
                }

                changed |= SetMarker(order, OrderFileTopologyMarker.SingleOrder);
            }
            else if (order.Items.Count > 1)
            {
                changed |= SetMarker(order, OrderFileTopologyMarker.MultiOrder);
                if (hasOrderLevelPaths)
                    issues.Add("MultiOrder-заказ содержит пути на уровне order; источником истины считаются item-пути.");
            }
            else
            {
                // Пустой заказ трактуем как single-контур до появления item.
                changed |= SetMarker(order, OrderFileTopologyMarker.SingleOrder);
            }

            if (order.FileTopologyMarker != markerBefore)
                issues.Add($"Маркер топологии синхронизирован: {markerBefore} -> {order.FileTopologyMarker}.");

            return new OrderTopologyNormalizationResult(changed, issues);
        }

        public static int GetEffectiveItemCount(OrderData? order)
        {
            if (order == null)
                return 0;

            if (order.Items != null && order.Items.Count > 0)
                return order.Items.Count;

            return HasAnyPath(order.SourcePath, order.PreparedPath, order.PrintPath) ? 1 : 0;
        }

        public static bool IsMultiOrder(OrderData? order)
        {
            return GetEffectiveItemCount(order) > 1;
        }

        public static bool IsSingleOrder(OrderData? order)
        {
            return !IsMultiOrder(order);
        }

        private static bool SetMarker(OrderData order, OrderFileTopologyMarker nextMarker)
        {
            if (order.FileTopologyMarker == nextMarker)
                return false;

            order.FileTopologyMarker = nextMarker;
            return true;
        }

        private static bool EnsureItemMetadata(OrderData order)
        {
            var changed = false;
            var sequence = 0L;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                if (string.IsNullOrWhiteSpace(item.ItemId))
                {
                    item.ItemId = Guid.NewGuid().ToString("N");
                    changed = true;
                }

                if (item.UpdatedAt == default)
                {
                    item.UpdatedAt = DateTime.Now;
                    changed = true;
                }

                if (item.SequenceNo < sequence)
                {
                    item.SequenceNo = sequence;
                    changed = true;
                }

                sequence = item.SequenceNo + 1;
            }

            return changed;
        }

        private static bool SyncSingleItemPath(OrderData order, OrderFileItem item, int stage, List<string> issues)
        {
            var orderPath = GetOrderStagePath(order, stage);
            var itemPath = GetItemStagePath(item, stage);

            if (!string.IsNullOrWhiteSpace(orderPath) &&
                !string.IsNullOrWhiteSpace(itemPath) &&
                !PathsEqual(orderPath, itemPath))
            {
                issues.Add($"Конфликт stage={stage}: order-path и item-path различаются. Применен приоритет существующего файла.");
            }

            var resolvedPath = ResolvePreferredPath(orderPath, itemPath);
            var changed = false;

            if (!PathsEqual(orderPath, resolvedPath))
            {
                SetOrderStagePath(order, stage, resolvedPath);
                changed = true;
            }

            if (!PathsEqual(itemPath, resolvedPath))
            {
                SetItemStagePath(item, stage, resolvedPath);
                changed = true;
            }

            changed |= SyncSingleItemFileSize(order, item, stage, issues);

            return changed;
        }

        private static OrderFileItem CreateSyntheticSingleItem(OrderData order)
        {
            return new OrderFileItem
            {
                ClientFileLabel = BuildDefaultItemLabel(order, null),
                SourcePath = order.SourcePath ?? string.Empty,
                SourceFileSizeBytes = order.SourceFileSizeBytes,
                PreparedPath = order.PreparedPath ?? string.Empty,
                PreparedFileSizeBytes = order.PreparedFileSizeBytes,
                PrintPath = order.PrintPath ?? string.Empty,
                PrintFileSizeBytes = order.PrintFileSizeBytes,
                PitStopAction = string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction,
                ImposingAction = string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction,
                FileStatus = string.IsNullOrWhiteSpace(order.Status) ? WorkflowStatusNames.Waiting : order.Status,
                UpdatedAt = DateTime.Now,
                SequenceNo = 0
            };
        }

        private static string BuildDefaultItemLabel(OrderData order, OrderFileItem? item)
        {
            var candidate = item == null
                ? order.SourcePath
                : FirstNotEmpty(item.SourcePath, item.PreparedPath, item.PrintPath);

            var fileName = Path.GetFileNameWithoutExtension(candidate ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;

            if (!string.IsNullOrWhiteSpace(order.Id))
                return order.Id;

            return "item";
        }

        private static string ResolvePreferredPath(string? orderPath, string? itemPath)
        {
            var normalizedOrderPath = NormalizePath(orderPath);
            var normalizedItemPath = NormalizePath(itemPath);

            if (!string.IsNullOrWhiteSpace(normalizedItemPath) && File.Exists(normalizedItemPath))
                return normalizedItemPath;

            if (!string.IsNullOrWhiteSpace(normalizedOrderPath) && File.Exists(normalizedOrderPath))
                return normalizedOrderPath;

            if (!string.IsNullOrWhiteSpace(normalizedItemPath))
                return normalizedItemPath;

            return normalizedOrderPath;
        }

        private static string GetOrderStagePath(OrderData order, int stage)
        {
            return stage switch
            {
                OrderStages.Source => order.SourcePath ?? string.Empty,
                OrderStages.Prepared => order.PreparedPath ?? string.Empty,
                OrderStages.Print => order.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static void SetOrderStagePath(OrderData order, int stage, string path)
        {
            if (stage == OrderStages.Source)
                order.SourcePath = path;
            else if (stage == OrderStages.Prepared)
                order.PreparedPath = path;
            else if (stage == OrderStages.Print)
                order.PrintPath = path;
        }

        private static long? GetOrderStageSize(OrderData order, int stage)
        {
            return stage switch
            {
                OrderStages.Source => order.SourceFileSizeBytes,
                OrderStages.Prepared => order.PreparedFileSizeBytes,
                OrderStages.Print => order.PrintFileSizeBytes,
                _ => null
            };
        }

        private static void SetOrderStageSize(OrderData order, int stage, long? size)
        {
            if (stage == OrderStages.Source)
                order.SourceFileSizeBytes = size;
            else if (stage == OrderStages.Prepared)
                order.PreparedFileSizeBytes = size;
            else if (stage == OrderStages.Print)
                order.PrintFileSizeBytes = size;
        }

        private static string GetItemStagePath(OrderFileItem item, int stage)
        {
            return stage switch
            {
                OrderStages.Source => item.SourcePath ?? string.Empty,
                OrderStages.Prepared => item.PreparedPath ?? string.Empty,
                OrderStages.Print => item.PrintPath ?? string.Empty,
                _ => string.Empty
            };
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

        private static long? GetItemStageSize(OrderFileItem item, int stage)
        {
            return stage switch
            {
                OrderStages.Source => item.SourceFileSizeBytes,
                OrderStages.Prepared => item.PreparedFileSizeBytes,
                OrderStages.Print => item.PrintFileSizeBytes,
                _ => null
            };
        }

        private static void SetItemStageSize(OrderFileItem item, int stage, long? size)
        {
            if (stage == OrderStages.Source)
                item.SourceFileSizeBytes = size;
            else if (stage == OrderStages.Prepared)
                item.PreparedFileSizeBytes = size;
            else if (stage == OrderStages.Print)
                item.PrintFileSizeBytes = size;
        }

        private static bool SyncSingleItemFileSize(OrderData order, OrderFileItem item, int stage, List<string> issues)
        {
            var orderSize = GetOrderStageSize(order, stage);
            var itemSize = GetItemStageSize(item, stage);

            if (orderSize.HasValue && itemSize.HasValue && orderSize.Value != itemSize.Value)
            {
                issues.Add($"Конфликт stage={stage}: order-size и item-size различаются. Применен приоритет существующего размера.");
            }

            var resolvedSize = orderSize ?? itemSize;
            if (!resolvedSize.HasValue)
                return false;

            var changed = false;
            if (GetOrderStageSize(order, stage) != resolvedSize)
            {
                SetOrderStageSize(order, stage, resolvedSize);
                changed = true;
            }

            if (GetItemStageSize(item, stage) != resolvedSize)
            {
                SetItemStageSize(item, stage, resolvedSize);
                changed = true;
            }

            return changed;
        }

        private static bool PathsEqual(string? left, string? right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string? path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        }

        private static bool HasAnyPath(params string?[] paths)
        {
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    return true;
            }

            return false;
        }

        private static string? FirstNotEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }
    }
}
