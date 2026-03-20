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
        private void LblFStatus_Click(object? sender, EventArgs e)
        {
            if (_suppressNextStatusFilterLabelClick)
            {
                _suppressNextStatusFilterLabelClick = false;
                return;
            }

            if (_statusFilterDropDown?.Visible == true)
            {
                _statusFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowStatusFilterDropDown();
        }

        private void LblFOrderNo_Click(object? sender, EventArgs e)
        {
            if (_suppressNextOrderNoLabelClick)
            {
                _suppressNextOrderNoLabelClick = false;
                return;
            }

            if (_orderNoFilterDropDown?.Visible == true)
            {
                _orderNoFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowOrderNoFilterDropDown();
        }

        private void LblFUser_Click(object? sender, EventArgs e)
        {
            if (_suppressNextUserFilterLabelClick)
            {
                _suppressNextUserFilterLabelClick = false;
                return;
            }

            if (_userFilterDropDown?.Visible == true)
            {
                _userFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowUserFilterDropDown();
        }

        private void LblFCreated_Click(object? sender, EventArgs e)
        {
            if (_suppressNextCreatedFilterLabelClick)
            {
                _suppressNextCreatedFilterLabelClick = false;
                return;
            }

            if (_createdFilterDropDown?.Visible == true)
            {
                _createdFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowCreatedDateFilterDropDown();
        }

        private void LblFReceived_Click(object? sender, EventArgs e)
        {
            if (_suppressNextReceivedFilterLabelClick)
            {
                _suppressNextReceivedFilterLabelClick = false;
                return;
            }

            if (_receivedFilterDropDown?.Visible == true)
            {
                _receivedFilterDropDown.Close(ToolStripDropDownCloseReason.AppClicked);
                return;
            }

            ShowReceivedDateFilterDropDown();
        }

        private void ShowOrderNoFilterDropDown()
        {
            EnsureOrderNoFilterDropDown();
            SyncOrderNoFilterPopupState();

            if (_orderNoFilterDropDown == null)
                return;

            _orderNoFilterDropDown.Show(picFOrderNoGlyph, new Point(0, picFOrderNoGlyph.Height));
            _orderNoFilterTextBox?.Focus();
            _orderNoFilterTextBox?.SelectAll();
        }

        private void EnsureOrderNoFilterDropDown()
        {
            if (_orderNoFilterDropDown != null &&
                _orderNoFilterTextBox != null)
                return;

            var popupWidth = Math.Max(lblFOrderNo.Width + 100, 280);
            var popupHeight = 96;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _orderNoFilterTextBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Location = new Point(16, 16),
                Size = new Size(popupWidth - 32, 24),
                Font = lblFOrderNo.Font
            };
            _orderNoFilterTextBox.TextChanged += (_, _) => UpdateOrderNoFilterActionButtonsState();
            _orderNoFilterTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (!HasOrderNoFilterInputText())
                        return;

                    ApplyOrderNoFilterFromPopup(clearFilter: false);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _orderNoFilterDropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
                }
            };

            var underline = new Panel
            {
                BackColor = Color.FromArgb(33, 127, 203),
                Location = new Point(16, 42),
                Size = new Size(popupWidth - 32, 2)
            };

            _orderNoFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, 56),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _orderNoFilterClearButton.FlatAppearance.BorderSize = 0;
            _orderNoFilterClearButton.Click += (_, _) => ApplyOrderNoFilterFromPopup(clearFilter: true);

            _orderNoFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(126, 56),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _orderNoFilterApplyButton.FlatAppearance.BorderSize = 0;
            _orderNoFilterApplyButton.Click += (_, _) => ApplyOrderNoFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_orderNoFilterTextBox);
            panel.Controls.Add(underline);
            panel.Controls.Add(_orderNoFilterClearButton);
            panel.Controls.Add(_orderNoFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _orderNoFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _orderNoFilterDropDown.Closing += OrderNoFilterDropDown_Closing;
            _orderNoFilterDropDown.Items.Add(host);
            UpdateOrderNoFilterActionButtonsState();
        }

        private void SyncOrderNoFilterPopupState()
        {
            if (_orderNoFilterTextBox == null)
                return;

            _orderNoFilterTextBox.Text = _orderNumberFilterText;
            UpdateOrderNoFilterActionButtonsState();
        }

        private bool HasOrderNoFilterInputText()
        {
            return !string.IsNullOrWhiteSpace(_orderNoFilterTextBox?.Text);
        }

        private void UpdateOrderNoFilterActionButtonsState()
        {
            if (_orderNoFilterClearButton == null || _orderNoFilterApplyButton == null)
                return;

            var hasInput = HasOrderNoFilterInputText();
            _orderNoFilterClearButton.Enabled = hasInput;
            _orderNoFilterApplyButton.Enabled = hasInput;
            _orderNoFilterClearButton.ForeColor = hasInput
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _orderNoFilterApplyButton.BackColor = hasInput
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _orderNoFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyOrderNoFilterFromPopup(bool clearFilter)
        {
            if (_orderNoFilterTextBox == null)
                return;

            if (!clearFilter && !HasOrderNoFilterInputText())
                return;

            var nextText = clearFilter ? string.Empty : (_orderNoFilterTextBox.Text ?? string.Empty).Trim();
            var changed = !string.Equals(_orderNumberFilterText, nextText, StringComparison.Ordinal);

            _orderNumberFilterText = nextText;

            if (changed)
                HandleOrdersGridChanged();

            _orderNoFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void OrderNoFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
                return;

            var labelRect = lblFOrderNo.RectangleToScreen(lblFOrderNo.ClientRectangle);
            var glyphRect = picFOrderNoGlyph.RectangleToScreen(picFOrderNoGlyph.ClientRectangle);
            if (labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position))
                _suppressNextOrderNoLabelClick = true;
        }

        private void ShowUserFilterDropDown()
        {
            EnsureUserFilterDropDown();
            RefreshUserFilterChecklist();

            if (_userFilterDropDown == null || _userFilterGlyph == null)
                return;

            _userFilterDropDown.Show(_userFilterGlyph, new Point(0, _userFilterGlyph.Height));
        }

        private void EnsureUserFilterDropDown()
        {
            if (_userFilterDropDown != null &&
                _userFilterCheckedList != null &&
                _userFilterClearButton != null &&
                _userFilterApplyButton != null)
                return;

            var labelWidth = _userFilterLabel?.Width ?? 150;
            var popupWidth = Math.Max(labelWidth + 110, 280);
            var listHeight = Math.Max(_filterUsers.Count * 33, 96);
            var buttonTop = 16 + listHeight + 10;
            var popupHeight = buttonTop + 32 + 10;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _userFilterCheckedList = new CheckedListBox
            {
                CheckOnClick = true,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = _userFilterLabel?.Font ?? Font,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(47, 53, 72),
                Location = new Point(16, 16),
                Size = new Size(popupWidth - 32, listHeight)
            };
            _userFilterCheckedList.ItemCheck += UserFilterCheckedList_ItemCheck;

            _userFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, buttonTop),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _userFilterClearButton.FlatAppearance.BorderSize = 0;
            _userFilterClearButton.Click += (_, _) => ApplyUserFilterFromPopup(clearFilter: true);

            _userFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(126, buttonTop),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _userFilterApplyButton.FlatAppearance.BorderSize = 0;
            _userFilterApplyButton.Click += (_, _) => ApplyUserFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_userFilterCheckedList);
            panel.Controls.Add(_userFilterClearButton);
            panel.Controls.Add(_userFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _userFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _userFilterDropDown.Closing += UserFilterDropDown_Closing;
            _userFilterDropDown.Items.Add(host);
            UpdateUserFilterActionButtonsState();
        }

        private void UserFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
                return;

            if (_userFilterLabel == null || _userFilterGlyph == null)
                return;

            var labelRect = _userFilterLabel.RectangleToScreen(_userFilterLabel.ClientRectangle);
            var glyphRect = _userFilterGlyph.RectangleToScreen(_userFilterGlyph.ClientRectangle);
            if (labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position))
                _suppressNextUserFilterLabelClick = true;
        }

        private void RefreshUserFilterChecklist()
        {
            if (_userFilterCheckedList == null)
                return;

            var countsByFilterUser = GetCountsByFilterUsers();

            _isUpdatingUserFilterList = true;
            _userFilterCheckedList.BeginUpdate();
            _userFilterCheckedList.Items.Clear();

            foreach (var userName in _filterUsers)
            {
                countsByFilterUser.TryGetValue(userName, out var count);
                var item = new UserFilterOption(userName, count);
                _userFilterCheckedList.Items.Add(item, _selectedFilterUsers.Contains(userName));
            }

            _userFilterCheckedList.EndUpdate();
            _isUpdatingUserFilterList = false;
            UpdateUserFilterActionButtonsState();
        }

        private void UserFilterCheckedList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingUserFilterList)
                return;

            BeginInvoke(new Action(UpdateUserFilterActionButtonsState));
        }

        private void UpdateUserFilterActionButtonsState()
        {
            if (_userFilterClearButton == null || _userFilterApplyButton == null || _userFilterCheckedList == null)
                return;

            var hasSelection = _userFilterCheckedList.CheckedItems.Count > 0;
            _userFilterClearButton.Enabled = hasSelection;
            _userFilterApplyButton.Enabled = hasSelection;
            _userFilterClearButton.ForeColor = hasSelection
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _userFilterApplyButton.BackColor = hasSelection
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _userFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyUserFilterFromPopup(bool clearFilter)
        {
            if (_userFilterCheckedList == null)
                return;

            var nextSelectedUsers = new HashSet<string>(StringComparer.Ordinal);
            if (!clearFilter)
            {
                foreach (var item in _userFilterCheckedList.CheckedItems)
                {
                    if (item is UserFilterOption userItem)
                        nextSelectedUsers.Add(userItem.UserName);
                }

                if (nextSelectedUsers.Count == 0)
                    return;
            }

            var changed = !nextSelectedUsers.SetEquals(_selectedFilterUsers);
            _selectedFilterUsers.Clear();
            foreach (var userName in nextSelectedUsers)
                _selectedFilterUsers.Add(userName);

            if (changed)
                HandleOrdersGridChanged();

            _userFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

    }
}

