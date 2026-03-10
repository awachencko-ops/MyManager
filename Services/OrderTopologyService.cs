using System;
using System.Collections.Generic;
using System.IO;

namespace MyManager
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
    /// order = группа файлов, где single-флоу является частным случаем (один item).
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

                changed |= SetMarker(order, OrderFileTopologyMarker.SingleFile);
            }
            else if (order.Items.Count > 1)
            {
                changed |= SetMarker(order, OrderFileTopologyMarker.MultiFile);
                if (hasOrderLevelPaths)
                    issues.Add("MultiFile-заказ содержит пути на уровне order; источником истины считаются item-пути.");
            }
            else
            {
                // Пустой заказ трактуем как single-контур до появления item.
                changed |= SetMarker(order, OrderFileTopologyMarker.SingleFile);
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

        public static bool IsMultiFileOrder(OrderData? order)
        {
            return GetEffectiveItemCount(order) > 1;
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

            return changed;
        }

        private static OrderFileItem CreateSyntheticSingleItem(OrderData order)
        {
            return new OrderFileItem
            {
                ClientFileLabel = BuildDefaultItemLabel(order, null),
                SourcePath = order.SourcePath ?? string.Empty,
                PreparedPath = order.PreparedPath ?? string.Empty,
                PrintPath = order.PrintPath ?? string.Empty,
                PitStopAction = string.IsNullOrWhiteSpace(order.PitStopAction) ? "-" : order.PitStopAction,
                ImposingAction = string.IsNullOrWhiteSpace(order.ImposingAction) ? "-" : order.ImposingAction,
                FileStatus = string.IsNullOrWhiteSpace(order.Status) ? "Ожидание" : order.Status,
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
                1 => order.SourcePath ?? string.Empty,
                2 => order.PreparedPath ?? string.Empty,
                3 => order.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static void SetOrderStagePath(OrderData order, int stage, string path)
        {
            if (stage == 1)
                order.SourcePath = path;
            else if (stage == 2)
                order.PreparedPath = path;
            else if (stage == 3)
                order.PrintPath = path;
        }

        private static string GetItemStagePath(OrderFileItem item, int stage)
        {
            return stage switch
            {
                1 => item.SourcePath ?? string.Empty,
                2 => item.PreparedPath ?? string.Empty,
                3 => item.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static void SetItemStagePath(OrderFileItem item, int stage, string path)
        {
            if (stage == 1)
                item.SourcePath = path;
            else if (stage == 2)
                item.PreparedPath = path;
            else if (stage == 3)
                item.PrintPath = path;
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
