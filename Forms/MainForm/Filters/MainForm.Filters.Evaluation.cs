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

namespace MyManager
{
    public partial class MainForm
    {
        private void ShowStatusFilterDropDown()
        {
            EnsureStatusFilterDropDown();
            RefreshStatusFilterChecklist();

            if (_statusFilterDropDown == null)
                return;

            _statusFilterDropDown.Show(picFStatusGlyph, new Point(0, picFStatusGlyph.Height));
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
                Font = lblFStatus.Font,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(47, 53, 72),
                Width = Math.Max(lblFStatus.Width + 140, 280),
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

            var labelRect = lblFStatus.RectangleToScreen(lblFStatus.ClientRectangle);
            var glyphRect = picFStatusGlyph.RectangleToScreen(picFStatusGlyph.ClientRectangle);
            if (labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position))
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
            lblFStatus.Text = StatusFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateOrderNoSearchCaption()
        {
            lblFOrderNo.Text = OrderNoSearchLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateUserFilterCaption()
        {
            if (_userFilterLabel == null)
                return;

            _userFilterLabel.Text = UserFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateCreatedDateFilterCaption()
        {
            if (_createdFilterLabel == null)
                return;

            _createdFilterLabel.Text = CreatedDateFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void UpdateReceivedDateFilterCaption()
        {
            if (_receivedFilterLabel == null)
                return;

            _receivedFilterLabel.Text = ReceivedDateFilterLabelText;
            AdjustFilterLabelWidths();
        }

        private void AdjustFilterLabelWidths()
        {
            SetFilterLabelWidth(lblFStatus, StatusFilterLabelText, 200);
            SetFilterLabelWidth(lblFOrderNo, OrderNoSearchLabelText, 180);
            SetFilterLabelWidth(_userFilterLabel, UserFilterLabelText, 150);
            SetFilterLabelWidth(_createdFilterLabel, CreatedDateFilterLabelText, 190);
            SetFilterLabelWidth(_receivedFilterLabel, ReceivedDateFilterLabelText, 190);
        }

        private static void SetFilterLabelWidth(Label? label, string text, int minWidth)
        {
            if (label == null)
                return;

            var measuredWidth = TextRenderer.MeasureText(
                text,
                label.Font,
                new Size(int.MaxValue, Math.Max(label.Height, 1)),
                TextFormatFlags.NoPadding).Width;

            label.Width = Math.Max(minWidth, measuredWidth + 16);
        }

        private void ApplyStatusFilterToGrid()
        {
            var hasSelectedStatuses = _selectedFilterStatuses.Count > 0;
            var hasOrderNoFilter = !string.IsNullOrWhiteSpace(_orderNumberFilterText);
            var selectedQueueStatus = GetSelectedQueueStatusName();
            var orderVisibilityByInternalId = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                var queueMatches = MatchesQueueStatus(selectedQueueStatus, normalizedStatus);
                var statusMatches = !hasSelectedStatuses || (normalizedStatus != null && _selectedFilterStatuses.Contains(normalizedStatus));
                var orderNoValue = row.Cells[colOrderNumber.Index].Value?.ToString();
                var orderNoMatches = !hasOrderNoFilter ||
                                     (!string.IsNullOrWhiteSpace(orderNoValue) &&
                                      orderNoValue.IndexOf(_orderNumberFilterText, StringComparison.OrdinalIgnoreCase) >= 0);
                var createdDateValue = row.Cells[colCreated.Index].Value?.ToString();
                var createdDateMatches = MatchesCreatedDateFilter(createdDateValue);
                var receivedDateValue = row.Cells[colReceived.Index].Value?.ToString();
                var receivedDateMatches = MatchesReceivedDateFilter(receivedDateValue);
                var shouldShow = queueMatches && statusMatches && orderNoMatches && createdDateMatches && receivedDateMatches;

                var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                if (!string.IsNullOrWhiteSpace(orderInternalId))
                    orderVisibilityByInternalId[orderInternalId] = shouldShow;

                try
                {
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
                    row.Visible = shouldShow;
                }
                catch (InvalidOperationException)
                {
                    // Если строка управляется внешним DataSource, пропускаем скрытие без падения формы.
                }
            }
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
            if (string.Equals(queueStatusName, "Все задания", StringComparison.Ordinal))
                return GetOrdersTotalCount();

            if (!QueueStatusMappings.TryGetValue(queueStatusName, out var mappedStatuses))
                return 0;

            var countsByFilterStatus = GetCountsByFilterStatus();
            var total = 0;
            foreach (var status in mappedStatuses)
            {
                if (countsByFilterStatus.TryGetValue(status, out var count))
                    total += count;
            }

            return total;
        }

        private int GetOrdersTotalCount()
        {
            var total = 0;
            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                total++;
            }

            return total;
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

        private static Dictionary<string, int> GetCountsByFilterUsers()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var userName in FilterUsers)
                counts[userName] = 0;

            return counts;
        }

        private Dictionary<string, int> GetCountsByFilterStatus()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var status in FilterStatuses)
                counts[status] = 0;

            foreach (DataGridViewRow row in dgvJobs.Rows)
            {
                if (row.IsNewRow)
                    continue;

                var rowTag = row.Tag?.ToString();
                if (IsItemTag(rowTag))
                    continue;

                var statusValue = row.Cells[colStatus.Index].Value?.ToString();
                var normalizedStatus = NormalizeStatus(statusValue);
                if (normalizedStatus == null)
                    continue;

                counts[normalizedStatus]++;
            }

            return counts;
        }

        private static string? NormalizeStatus(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
                return null;

            var value = rawStatus.Trim();
            foreach (var status in FilterStatuses)
            {
                if (string.Equals(value, status, StringComparison.OrdinalIgnoreCase))
                    return status;

                if (value.Contains(status, StringComparison.OrdinalIgnoreCase))
                    return status;
            }

            if (value.Contains("архив", StringComparison.OrdinalIgnoreCase))
                return "В архиве";

            if (value.Contains("отмен", StringComparison.OrdinalIgnoreCase))
                return "Отменено";

            if (value.Contains("ошиб", StringComparison.OrdinalIgnoreCase))
                return "Ошибка";

            if (value.Contains("сборк", StringComparison.OrdinalIgnoreCase)
                || value.Contains("imposing", StringComparison.OrdinalIgnoreCase)
                || value.Contains("pitstop", StringComparison.OrdinalIgnoreCase))
                return "Выполняется сборка";

            if (value.Contains("обрабатыва", StringComparison.OrdinalIgnoreCase)
                || value.Contains("в работе", StringComparison.OrdinalIgnoreCase)
                || value.Contains("запуск", StringComparison.OrdinalIgnoreCase))
                return "Обрабатывается";

            if (value.Contains("ожид", StringComparison.OrdinalIgnoreCase))
                return "Ожидание";

            if (value.Contains("обработано", StringComparison.OrdinalIgnoreCase))
                return "Обработано";

            if (value.Contains("Готово", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Заверш", StringComparison.OrdinalIgnoreCase))
                return "Завершено";

            return null;
        }

        private static bool MatchesQueueStatus(string? queueStatusName, string? normalizedWorkflowStatus)
        {
            if (string.IsNullOrWhiteSpace(queueStatusName)
                || string.Equals(queueStatusName, "Все задания", StringComparison.Ordinal))
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
