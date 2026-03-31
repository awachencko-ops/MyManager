using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void ShowStatusFilterDropDown()
        {
            EnsureStatusFilterDropDown();
            RefreshStatusFilterChecklist();

            if (_statusFilterDropDown == null)
                return;

            _statusFilterDropDown.Show(cbStatus, new Point(0, cbStatus.Height));
        }

        private void EnsureStatusFilterDropDown()
        {
            if (_statusFilterDropDown != null && _statusFilterCheckedList != null)
                return;

            _statusFilterCheckedList = new CheckedListBox
            {
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = cbStatus.Font,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(47, 53, 72),
                Width = Math.Max(cbStatus.Width + 140, 280),
                Height = 240
            };
            _statusFilterCheckedList.ItemCheck += StatusFilterCheckedList_ItemCheck;

            var host = new ToolStripControlHost(_statusFilterCheckedList)
            {
                AutoSize = false,
                Size = _statusFilterCheckedList.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _statusFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _statusFilterDropDown.Closing += StatusFilterDropDown_Closing;
            _statusFilterDropDown.Items.Add(host);
        }

        private void StatusFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
                return;

            var comboRect = cbStatus.RectangleToScreen(cbStatus.ClientRectangle);
            if (comboRect.Contains(Cursor.Position))
                _suppressNextStatusFilterLabelClick = true;
        }

        private void RefreshStatusFilterChecklist()
        {
            if (_statusFilterCheckedList == null)
                return;

            var countsByFilterStatus = GetCountsByFilterStatus();

            _isUpdatingStatusFilterList = true;
            _statusFilterCheckedList.BeginUpdate();
            _statusFilterCheckedList.Items.Clear();

            foreach (var statusName in FilterStatuses)
            {
                countsByFilterStatus.TryGetValue(statusName, out var count);
                var item = new StatusFilterOption(statusName, count);
                _statusFilterCheckedList.Items.Add(item, _selectedFilterStatuses.Contains(statusName));
            }

            _statusFilterCheckedList.EndUpdate();
            _isUpdatingStatusFilterList = false;
        }

        private void StatusFilterCheckedList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingStatusFilterList)
                return;

            BeginInvoke(new Action(() =>
            {
                UpdateSelectedStatusesFromChecklist();
                ApplyStatusFilterToGrid();
                UpdateStatusFilterCaption();
                RefreshQueuePresentation();
            }));
        }

        private void UpdateSelectedStatusesFromChecklist()
        {
            if (_statusFilterCheckedList == null)
                return;

            _selectedFilterStatuses.Clear();
            foreach (var item in _statusFilterCheckedList.CheckedItems)
            {
                if (item is StatusFilterOption statusItem)
                    _selectedFilterStatuses.Add(statusItem.StatusName);
            }
        }

        private void UpdateStatusFilterCaption()
        {
            AdjustFilterLabelWidths();
        }

        private void UpdateOrderNoSearchCaption()
        {
            AdjustFilterLabelWidths();
        }

        private void UpdateUserFilterCaption()
        {
            if (_userFilterLabel == null)
                return;

            if (string.Equals(_userFilterLabel.Text, UserFilterLabelText, StringComparison.Ordinal))
                return;

            _userFilterLabel.Text = UserFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateCreatedDateFilterCaption()
        {
            if (_createdFilterLabel == null)
                return;

            if (string.Equals(_createdFilterLabel.Text, CreatedDateFilterLabelText, StringComparison.Ordinal))
                return;

            _createdFilterLabel.Text = CreatedDateFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateReceivedDateFilterCaption()
        {
            if (_receivedFilterLabel == null)
                return;

            if (string.Equals(_receivedFilterLabel.Text, ReceivedDateFilterLabelText, StringComparison.Ordinal))
                return;

            _receivedFilterLabel.Text = ReceivedDateFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void AdjustFilterLabelWidths()
        {
            SetFilterLabelWidth(cbStatus, StatusFilterLabelText, 200);
            SetFilterLabelWidth(cbOrderNo, OrderNoSearchLabelText, 180);
            SetFilterLabelWidth(_userFilterLabel, UserFilterLabelText, 150);
            SetFilterLabelWidth(_createdFilterLabel, CreatedDateFilterLabelText, 190);
            SetFilterLabelWidth(_receivedFilterLabel, ReceivedDateFilterLabelText, 190);
        }

        private static void SetFilterLabelWidth(Control? control, string text, int minWidth)
        {
            if (control == null)
                return;

            var measuredWidth = TextRenderer.MeasureText(
                text,
                control.Font,
                new Size(int.MaxValue, Math.Max(control.Height, 1)),
                TextFormatFlags.NoPadding).Width;

            control.Width = Math.Max(minWidth, measuredWidth + 16);
        }

        private void ApplyStatusFilterToGrid()
        {
            var hasSelectedStatuses = _selectedFilterStatuses.Count > 0;
            var hasSelectedUsers = _selectedFilterUsers.Count > 0;
            var hasOrderNoFilter = !string.IsNullOrWhiteSpace(_orderNumberFilterText);
            var hasCreatedDateFilter = _createdDateFilterKind != CreatedDateFilterKind.None;
            var hasReceivedDateFilter = _receivedDateFilterKind != CreatedDateFilterKind.None;
            var selectedQueueStatus = GetSelectedQueueStatusName();
            var queueFilterActive = !string.IsNullOrWhiteSpace(selectedQueueStatus)
                && !string.Equals(selectedQueueStatus, QueueStatusNames.AllJobs, StringComparison.Ordinal);
            var requiresNormalizedStatus = queueFilterActive || hasSelectedStatuses;
            var orderVisibilityByInternalId = new Dictionary<string, bool>(StringComparer.Ordinal);
            var ordersByInternalId = OrderGridLogic.BuildOrderIndex(_orderHistory);
            var visibleOrdersCount = 0;

            dgvJobs.SuspendLayout();
            try
            {
                foreach (DataGridViewRow row in dgvJobs.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    var rowTag = row.Tag?.ToString();
                    if (IsItemTag(rowTag))
                        continue;

                    string? normalizedStatus = null;
                    if (requiresNormalizedStatus)
                    {
                        var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                        normalizedStatus = NormalizeStatus(statusValue);
                    }

                    var queueMatches = !queueFilterActive || MatchesQueueStatus(selectedQueueStatus, normalizedStatus);
                    var statusMatches = !hasSelectedStatuses || (normalizedStatus != null && _selectedFilterStatuses.Contains(normalizedStatus));
                    var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                    var userMatches = true;
                    if (hasSelectedUsers)
                    {
                        var order = OrderGridLogic.FindOrderByInternalId(ordersByInternalId, orderInternalId);
                        var orderUserName = NormalizeOrderUserName(order?.UserName);
                        userMatches = _selectedFilterUsers.Contains(orderUserName);
                    }

                    var orderNoMatches = true;
                    if (hasOrderNoFilter)
                    {
                        var orderNoValue = row.Cells[colOrderNumber.Index].Value?.ToString();
                        orderNoMatches = !string.IsNullOrWhiteSpace(orderNoValue)
                            && orderNoValue.IndexOf(_orderNumberFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    var createdDateMatches = true;
                    if (hasCreatedDateFilter)
                    {
                        var createdDateValue = row.Cells[colCreated.Index].Value?.ToString();
                        createdDateMatches = MatchesCreatedDateFilter(createdDateValue);
                    }

                    var receivedDateMatches = true;
                    if (hasReceivedDateFilter)
                    {
                        var receivedDateValue = row.Cells[colReceived.Index].Value?.ToString();
                        receivedDateMatches = MatchesReceivedDateFilter(receivedDateValue);
                    }

                    var shouldShow = queueMatches && statusMatches && userMatches && orderNoMatches && createdDateMatches && receivedDateMatches;

                    if (!string.IsNullOrWhiteSpace(orderInternalId))
                        orderVisibilityByInternalId[orderInternalId] = shouldShow;

                    if (shouldShow)
                        visibleOrdersCount++;

                    try
                    {
                        if (row.Visible != shouldShow)
                            row.Visible = shouldShow;
                    }
                    catch (InvalidOperationException)
                    {
                        // Если строка управляется внешним DataSource, пропускаем скрытие без падения формы.
                    }
                }

                foreach (DataGridViewRow row in dgvJobs.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    var rowTag = row.Tag?.ToString();
                    if (!IsItemTag(rowTag))
                        continue;

                    var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                    var shouldShow = !string.IsNullOrWhiteSpace(orderInternalId) &&
                                     orderVisibilityByInternalId.TryGetValue(orderInternalId, out var visible) &&
                                     visible;

                    try
                    {
                        if (row.Visible != shouldShow)
                            row.Visible = shouldShow;
                    }
                    catch (InvalidOperationException)
                    {
                        // Если строка управляется внешним DataSource, пропускаем скрытие без падения формы.
                    }
                }
            }
            finally
            {
                dgvJobs.ResumeLayout(performLayout: false);
            }

            _visibleOrdersCountCache = visibleOrdersCount;
            _visibleOrdersCountCacheValid = true;
        }

        private void FillQueueCombo(string? preferredStatus)
        {
            var targetStatus = string.IsNullOrWhiteSpace(preferredStatus)
                ? QueueStatuses[0]
                : preferredStatus;

            var previousSync = _isSyncingQueueSelection;
            _isSyncingQueueSelection = true;

            cbQueue.BeginUpdate();
            cbQueue.Items.Clear();
            foreach (var statusName in QueueStatuses)
            {
                cbQueue.Items.Add(new QueueStatusItem(statusName, statusName));
            }
            cbQueue.EndUpdate();

            var targetItem = FindQueueItem(targetStatus);
            if (targetItem != null)
                cbQueue.SelectedItem = targetItem;
            else if (cbQueue.Items.Count > 0)
                cbQueue.SelectedIndex = 0;

            _isSyncingQueueSelection = previousSync;
        }

        private QueueStatusItem? FindQueueItem(string statusName)
        {
            foreach (var item in cbQueue.Items)
            {
                if (item is QueueStatusItem queueItem &&
                    string.Equals(queueItem.StatusName, statusName, StringComparison.Ordinal))
                    return queueItem;
            }

            return null;
        }

        private string? GetSelectedQueueStatusName()
        {
            if (cbQueue.SelectedItem is QueueStatusItem selectedItem)
                return selectedItem.StatusName;

            return null;
        }

        private static string FormatQueueLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var culture = CultureInfo.CurrentCulture;
            var normalized = value.Trim().ToLower(culture);
            if (normalized.Length == 1)
                return normalized.ToUpper(culture);

            return char.ToUpper(normalized[0], culture) + normalized[1..];
        }

        private string GetQueueStatusCountText(string statusName)
        {
            return $"({GetQueueStatusCount(statusName)})";
        }

        private int GetQueueStatusCount(string queueStatusName)
        {
            EnsureQueueStatusCountsCache();

            if (string.Equals(queueStatusName, QueueStatusNames.AllJobs, StringComparison.Ordinal))
                return GetOrdersTotalCount();

            if (!QueueStatusMappings.TryGetValue(queueStatusName, out var mappedStatuses)
                || _queueStatusCountsCache == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var status in mappedStatuses)
            {
                if (_queueStatusCountsCache.TryGetValue(status, out var count))
                    total += count;
            }

            return total;
        }

        private int GetOrdersTotalCount()
        {
            EnsureQueueStatusCountsCache();
            return _queueTotalOrdersCountCache;
        }

        private bool MatchesCreatedDateFilter(string? rawCreatedDate)
        {
            if (_createdDateFilterKind == CreatedDateFilterKind.None)
                return true;

            if (!TryParseCreatedDate(rawCreatedDate, out var createdDate))
                return false;

            var date = createdDate.Date;
            return _createdDateFilterKind switch
            {
                CreatedDateFilterKind.Today => date == DateTime.Today,
                CreatedDateFilterKind.Single => _createdDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => date <= _createdDateSingleValue.Date,
                    CreatedDateSingleMode.After => date >= _createdDateSingleValue.Date,
                    _ => date == _createdDateSingleValue.Date
                },
                CreatedDateFilterKind.Range => date >= _createdDateRangeFrom.Date && date <= _createdDateRangeTo.Date,
                _ => true
            };
        }

        private bool MatchesReceivedDateFilter(string? rawReceivedDate)
        {
            if (_receivedDateFilterKind == CreatedDateFilterKind.None)
                return true;

            if (!TryParseCreatedDate(rawReceivedDate, out var receivedDate))
                return false;

            var date = receivedDate.Date;
            return _receivedDateFilterKind switch
            {
                CreatedDateFilterKind.Today => date == DateTime.Today,
                CreatedDateFilterKind.Single => _receivedDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => date <= _receivedDateSingleValue.Date,
                    CreatedDateSingleMode.After => date >= _receivedDateSingleValue.Date,
                    _ => date == _receivedDateSingleValue.Date
                },
                CreatedDateFilterKind.Range => date >= _receivedDateRangeFrom.Date && date <= _receivedDateRangeTo.Date,
                _ => true
            };
        }

        private static bool TryParseCreatedDate(string? rawCreatedDate, out DateTime parsedDate)
        {
            parsedDate = default;
            if (string.IsNullOrWhiteSpace(rawCreatedDate))
                return false;

            var value = rawCreatedDate.Trim();
            var formats = new[]
            {
                "dd.MM.yyyy",
                "d.M.yyyy",
                "dd.MM.yy",
                "d.M.yy",
                "yyyy-MM-dd",
                "yyyy/MM/dd",
                "dd/MM/yyyy",
                "d/M/yyyy",
                "MM/dd/yyyy",
                "M/d/yyyy"
            };

            return DateTime.TryParseExact(value, formats, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate) ||
                   DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate) ||
                   DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate);
        }

        private Dictionary<string, int> GetCountsByFilterUsers()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordersByInternalId = OrderGridLogic.BuildOrderIndex(_orderHistory);
            foreach (var userName in _filterUsers)
                counts[userName] = 0;

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                var order = OrderGridLogic.FindOrderByInternalId(ordersByInternalId, orderInternalId);
                var orderUserName = NormalizeOrderUserName(order?.UserName);
                if (!counts.ContainsKey(orderUserName))
                    counts[orderUserName] = 0;

                counts[orderUserName]++;
            }

            return counts;
        }

        private Dictionary<string, int> GetCountsByFilterStatus()
        {
            EnsureQueueStatusCountsCache();
            if (_queueStatusCountsCache != null)
                return new Dictionary<string, int>(_queueStatusCountsCache, StringComparer.Ordinal);

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var status in FilterStatuses)
                counts[status] = 0;

            return counts;
        }

        private void InvalidateQueueStatusCountsCache()
        {
            _queueStatusCountsCacheValid = false;
        }

        private void InvalidateVisibleOrdersCountCache()
        {
            _visibleOrdersCountCacheValid = false;
        }

        private void EnsureQueueStatusCountsCache()
        {
            if (_queueStatusCountsCacheValid && _queueStatusCountsCache != null)
                return;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var status in FilterStatuses)
                counts[status] = 0;

            var total = 0;
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                total++;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                if (normalizedStatus != null && counts.TryGetValue(normalizedStatus, out var count))
                    counts[normalizedStatus] = count + 1;
            }

            _queueStatusCountsCache = counts;
            _queueTotalOrdersCountCache = total;
            _queueStatusCountsCacheValid = true;
        }

        private static string? NormalizeStatus(string? rawStatus)
        {
            return WorkflowStatusNames.Normalize(rawStatus);
        }

        private static bool MatchesQueueStatus(string? queueStatusName, string? normalizedWorkflowStatus)
        {
            if (string.IsNullOrWhiteSpace(queueStatusName)
                || string.Equals(queueStatusName, QueueStatusNames.AllJobs, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(normalizedWorkflowStatus))
                return false;

            if (!QueueStatusMappings.TryGetValue(queueStatusName, out var mappedStatuses))
                return false;

            foreach (var mappedStatus in mappedStatuses)
            {
                if (string.Equals(mappedStatus, normalizedWorkflowStatus, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}

