using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Replica
{
    internal static class OrderGridLogic
    {
        private const string OrderTagPrefix = "order|";
        private const string ItemTagPrefix = "item|";

        public static string BuildOrderTag(string orderInternalId)
        {
            return $"{OrderTagPrefix}{orderInternalId}";
        }

        public static string BuildItemTag(string orderInternalId, string itemId)
        {
            return $"{ItemTagPrefix}{orderInternalId}|{itemId}";
        }

        public static bool IsOrderTag(string? tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.StartsWith(OrderTagPrefix, StringComparison.Ordinal);
        }

        public static bool IsItemTag(string? tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.StartsWith(ItemTagPrefix, StringComparison.Ordinal);
        }

        public static string? ExtractOrderInternalIdFromTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var parts = tag.Split('|');
            if (parts.Length < 2)
                return null;

            return parts[1];
        }

        public static string? ExtractItemIdFromTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            if (!IsItemTag(tag))
                return null;

            var parts = tag.Split('|');
            if (parts.Length < 3)
                return null;

            return parts[2];
        }

        public static OrderData? FindOrderByInternalId(IEnumerable<OrderData> orderHistory, string? internalId)
        {
            if (orderHistory == null || string.IsNullOrWhiteSpace(internalId))
                return null;

            return orderHistory.FirstOrDefault(x => string.Equals(x.InternalId, internalId, StringComparison.Ordinal));
        }

        public static void TryRestoreSelectedRowByTag(DataGridView grid, int focusColumnIndex, string selectedTag)
        {
            if (grid == null || string.IsNullOrWhiteSpace(selectedTag))
                return;

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!string.Equals(row.Tag?.ToString(), selectedTag, StringComparison.Ordinal))
                    continue;

                if (focusColumnIndex >= 0 && focusColumnIndex < row.Cells.Count)
                    grid.CurrentCell = row.Cells[focusColumnIndex];
                else if (row.Cells.Count > 0)
                    grid.CurrentCell = row.Cells[0];

                return;
            }
        }

        public static OrderData? GetSelectedOrder(DataGridView grid, IEnumerable<OrderData> orderHistory)
        {
            if (grid == null || orderHistory == null)
                return null;

            var selectedRow = grid.CurrentRow;
            if (selectedRow == null || selectedRow.IsNewRow)
            {
                selectedRow = grid.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Where(row => !row.IsNewRow)
                    .OrderBy(row => row.Index)
                    .FirstOrDefault();
            }

            if (selectedRow == null)
                return null;

            var rowTag = selectedRow.Tag?.ToString();
            var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return null;

            return FindOrderByInternalId(orderHistory, orderInternalId);
        }

        public static List<OrderData> GetSelectedOrders(DataGridView grid, IEnumerable<OrderData> orderHistory)
        {
            var selectedOrders = new List<OrderData>();
            var uniqueOrderIds = new HashSet<string>(StringComparer.Ordinal);

            if (grid == null || orderHistory == null)
                return selectedOrders;

            var selectedRows = grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .OrderBy(row => row.Index);

            foreach (var row in selectedRows)
            {
                var orderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                if (string.IsNullOrWhiteSpace(orderInternalId) || !uniqueOrderIds.Add(orderInternalId))
                    continue;

                var order = FindOrderByInternalId(orderHistory, orderInternalId);
                if (order != null)
                    selectedOrders.Add(order);
            }

            if (selectedOrders.Count > 0)
                return selectedOrders;

            var singleOrder = GetSelectedOrder(grid, orderHistory);
            if (singleOrder != null && uniqueOrderIds.Add(singleOrder.InternalId))
                selectedOrders.Add(singleOrder);

            return selectedOrders;
        }

        public static string GetOrderDisplayId(OrderData order)
        {
            return string.IsNullOrWhiteSpace(order.Id) ? "—" : order.Id.Trim();
        }

        public static string GetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "...";

            var normalizedPath = path.Trim();
            if (Directory.Exists(normalizedPath))
                return "...";

            var fileName = Path.GetFileName(normalizedPath);
            return string.IsNullOrWhiteSpace(fileName) ? "..." : fileName;
        }

        public static string FormatDate(DateTime value)
        {
            if (value == default)
                return string.Empty;

            return value.ToString("dd.MM.yyyy");
        }

        public static string NormalizeAction(string? action)
        {
            return string.IsNullOrWhiteSpace(action) ? "-" : action.Trim();
        }

        public static OrderFileItem? GetPrimaryItem(OrderData order)
        {
            if (order?.Items == null || order.Items.Count == 0)
                return null;

            return order.Items
                .Where(x => x != null)
                .OrderBy(x => x.SequenceNo)
                .FirstOrDefault();
        }

        public static string ResolveSingleOrderDisplayAction(OrderData order, Func<OrderFileItem, string> selector, string? orderAction)
        {
            var normalizedOrderAction = NormalizeAction(orderAction);
            if (!string.Equals(normalizedOrderAction, "-", StringComparison.Ordinal))
                return normalizedOrderAction;

            var primaryItem = GetPrimaryItem(order);
            if (primaryItem == null)
                return normalizedOrderAction;

            return NormalizeAction(selector(primaryItem));
        }

        public static bool OrderMatchesSearch(OrderData order, string searchText)
        {
            if (order == null)
                return false;

            var query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
                return true;

            static bool Contains(string source, string queryValue)
                => !string.IsNullOrWhiteSpace(source) &&
                   source.IndexOf(queryValue, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Contains(order.Id, query)
                || Contains(Path.GetFileName(order.SourcePath), query)
                || Contains(Path.GetFileName(order.PreparedPath), query)
                || Contains(Path.GetFileName(order.PrintPath), query))
            {
                return true;
            }

            if (order.Items == null || order.Items.Count == 0)
                return false;

            foreach (var item in order.Items)
            {
                if (item == null)
                    continue;

                if (Contains(item.ClientFileLabel, query)
                    || Contains(Path.GetFileName(item.SourcePath), query)
                    || Contains(Path.GetFileName(item.PreparedPath), query)
                    || Contains(Path.GetFileName(item.PrintPath), query))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
