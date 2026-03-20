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
        private void ShowReceivedDateFilterDropDown()
        {
            EnsureReceivedDateFilterDropDown();
            SyncReceivedDateFilterPopupState();

            if (_receivedFilterDropDown == null || _receivedFilterGlyph == null)
                return;

            _receivedFilterDropDown.Show(_receivedFilterGlyph, new Point(0, _receivedFilterGlyph.Height));
        }

        private void EnsureReceivedDateFilterDropDown()
        {
            if (_receivedFilterDropDown != null &&
                _receivedFilterTodayRadio != null &&
                _receivedFilterSingleRadio != null &&
                _receivedFilterSingleModeCombo != null &&
                _receivedFilterSingleDatePicker != null &&
                _receivedFilterRangeRadio != null &&
                _receivedFilterRangeFromDatePicker != null &&
                _receivedFilterRangeToDatePicker != null &&
                _receivedFilterClearButton != null &&
                _receivedFilterApplyButton != null)
                return;

            var popupWidth = Math.Max((_receivedFilterLabel?.Width ?? 170) + 260, 440);
            var popupHeight = 206;
            var font = _receivedFilterLabel?.Font ?? Font;
            var row1Y = 16;
            var row2Y = 60;
            var row3Y = 104;
            var bulletX = 16;
            var contentX = 48;
            var singleModeWidth = 172;
            var singleDateX = contentX + singleModeWidth + 10;
            var singleDateWidth = popupWidth - singleDateX - 16;
            var rangeFromX = 88;
            var rangeDateWidth = 146;
            var toLabelX = rangeFromX + rangeDateWidth + 10;
            var toLabelWidth = 38;
            var rangeToX = toLabelX + toLabelWidth;
            var rangeToWidth = popupWidth - rangeToX - 16;
            var buttonsY = 158;

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(popupWidth, popupHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _receivedFilterTodayRadio = new RadioButton
            {
                Location = new Point(bulletX, row1Y),
                Size = new Size(130, 30),
                Text = "Сегодня",
                Font = font,
                AutoCheck = true
            };
            _receivedFilterTodayRadio.CheckedChanged += ReceivedFilterModeControlChanged;

            _receivedFilterSingleRadio = new RadioButton
            {
                Location = new Point(bulletX, row2Y),
                Size = new Size(24, 30),
                Font = font,
                AutoCheck = true
            };
            _receivedFilterSingleRadio.CheckedChanged += ReceivedFilterModeControlChanged;

            _receivedFilterSingleModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                IntegralHeight = false,
                Location = new Point(contentX, row2Y - 1),
                Size = new Size(singleModeWidth, 32),
                Font = font
            };
            _receivedFilterSingleModeCombo.Items.AddRange(new object[] { "Точная дата", "До", "После" });
            _receivedFilterSingleModeCombo.SelectedIndexChanged += ReceivedFilterModeControlChanged;

            _receivedFilterSingleDatePicker = CreateCreatedFilterDatePicker(new Point(singleDateX, row2Y - 1), new Size(singleDateWidth, 32), font);
            _receivedFilterSingleDatePicker.ValueChanged += ReceivedFilterModeControlChanged;
            AttachReceivedDatePickerCalendarEvents(_receivedFilterSingleDatePicker);

            _receivedFilterRangeRadio = new RadioButton
            {
                Location = new Point(bulletX, row3Y),
                Size = new Size(58, 30),
                Text = "От",
                Font = font,
                AutoCheck = true
            };
            _receivedFilterRangeRadio.CheckedChanged += ReceivedFilterModeControlChanged;

            _receivedFilterRangeFromDatePicker = CreateCreatedFilterDatePicker(new Point(rangeFromX, row3Y - 1), new Size(rangeDateWidth, 32), font);
            _receivedFilterRangeFromDatePicker.ValueChanged += ReceivedFilterModeControlChanged;
            AttachReceivedDatePickerCalendarEvents(_receivedFilterRangeFromDatePicker);

            var toLabel = new Label
            {
                Location = new Point(toLabelX, row3Y),
                Size = new Size(toLabelWidth, 28),
                Text = "До",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font
            };

            _receivedFilterRangeToDatePicker = CreateCreatedFilterDatePicker(new Point(rangeToX, row3Y - 1), new Size(rangeToWidth, 32), font);
            _receivedFilterRangeToDatePicker.ValueChanged += ReceivedFilterModeControlChanged;
            AttachReceivedDatePickerCalendarEvents(_receivedFilterRangeToDatePicker);

            _receivedFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 232, buttonsY),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _receivedFilterClearButton.FlatAppearance.BorderSize = 0;
            _receivedFilterClearButton.Click += (_, _) => ApplyReceivedDateFilterFromPopup(clearFilter: true);

            _receivedFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 122, buttonsY),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _receivedFilterApplyButton.FlatAppearance.BorderSize = 0;
            _receivedFilterApplyButton.Click += (_, _) => ApplyReceivedDateFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_receivedFilterTodayRadio);
            panel.Controls.Add(_receivedFilterSingleRadio);
            panel.Controls.Add(_receivedFilterSingleModeCombo);
            panel.Controls.Add(_receivedFilterSingleDatePicker);
            panel.Controls.Add(_receivedFilterRangeRadio);
            panel.Controls.Add(_receivedFilterRangeFromDatePicker);
            panel.Controls.Add(toLabel);
            panel.Controls.Add(_receivedFilterRangeToDatePicker);
            panel.Controls.Add(_receivedFilterClearButton);
            panel.Controls.Add(_receivedFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _receivedFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _receivedFilterDropDown.Closing += ReceivedDateFilterDropDown_Closing;
            _receivedFilterDropDown.Items.Add(host);
            UpdateReceivedDateFilterControlsState();
        }

        private void AttachReceivedDatePickerCalendarEvents(DateTimePicker picker)
        {
            picker.DropDown += ReceivedDatePicker_DropDown;
        }

        private void ReceivedDatePicker_DropDown(object? sender, EventArgs e)
        {
            if (sender is not DateTimePicker picker)
                return;

            BeginInvoke(new Action(() =>
            {
                try
                {
                    SendKeys.Send("{ESC}");
                }
                catch
                {
                    // Игнорируем сбои нативного сворачивания, ниже откроем свой календарь.
                }

                ShowReceivedCalendarDropDown(picker);
            }));
        }

        private void EnsureReceivedCalendarDropDown()
        {
            if (_receivedCalendarDropDown != null && _receivedCalendar != null && _receivedCalendarOkButton != null)
                return;

            _receivedCalendar = new MonthCalendar
            {
                MaxSelectionCount = 1,
                ShowToday = false,
                ShowTodayCircle = false
            };

            _receivedCalendarOkButton = new Button
            {
                Text = "OK",
                FlatStyle = FlatStyle.Standard
            };
            _receivedCalendarOkButton.Click += ReceivedCalendarOkButton_Click;

            var calendarWidth = _receivedCalendar.Width;
            var buttonHeight = 32;
            var buttonTop = _receivedCalendar.Height + 6;
            _receivedCalendarOkButton.Location = new Point(0, buttonTop);
            _receivedCalendarOkButton.Size = new Size(calendarWidth, buttonHeight);

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(calendarWidth, buttonTop + buttonHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            panel.Controls.Add(_receivedCalendar);
            panel.Controls.Add(_receivedCalendarOkButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _receivedCalendarDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(2)
            };
            _receivedCalendarDropDown.Closed += ReceivedCalendarDropDown_Closed;
            _receivedCalendarDropDown.Items.Add(host);
        }

        private void ShowReceivedCalendarDropDown(DateTimePicker picker)
        {
            EnsureReceivedCalendarDropDown();
            if (_receivedCalendarDropDown == null || _receivedCalendar == null)
                return;

            _receivedCalendarTargetPicker = picker;
            var selectedDate = picker.Value.Date;
            _receivedCalendar.SetDate(selectedDate);
            _receivedCalendar.SelectionStart = selectedDate;
            _receivedCalendar.SelectionEnd = selectedDate;

            if (_receivedCalendarDropDown.Visible)
                _receivedCalendarDropDown.Close(ToolStripDropDownCloseReason.CloseCalled);

            _isReceivedDateCalendarOpen = true;
            _receivedCalendarDropDown.Show(picker, new Point(0, picker.Height));
        }

        private void ReceivedCalendarOkButton_Click(object? sender, EventArgs e)
        {
            if (_receivedCalendarTargetPicker != null && _receivedCalendar != null)
            {
                var selectedDate = _receivedCalendar.SelectionStart.Date;
                if (_receivedCalendarTargetPicker.Value.Date != selectedDate)
                    _receivedCalendarTargetPicker.Value = selectedDate;
            }

            CloseReceivedCalendarDropDown();
        }

        private void CloseReceivedCalendarDropDown()
        {
            if (_receivedCalendarDropDown?.Visible == true)
                _receivedCalendarDropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            else
            {
                _isReceivedDateCalendarOpen = false;
                _receivedCalendarTargetPicker = null;
            }
        }

        private void ReceivedCalendarDropDown_Closed(object? sender, ToolStripDropDownClosedEventArgs e)
        {
            _isReceivedDateCalendarOpen = false;
            _receivedCalendarTargetPicker = null;
        }

        private void SyncReceivedDateFilterPopupState()
        {
            if (_receivedFilterTodayRadio == null ||
                _receivedFilterSingleRadio == null ||
                _receivedFilterSingleModeCombo == null ||
                _receivedFilterSingleDatePicker == null ||
                _receivedFilterRangeRadio == null ||
                _receivedFilterRangeFromDatePicker == null ||
                _receivedFilterRangeToDatePicker == null)
                return;

            _isSyncingReceivedFilterControls = true;
            try
            {
                _receivedFilterSingleModeCombo.SelectedIndex = _receivedDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => 1,
                    CreatedDateSingleMode.After => 2,
                    _ => 0
                };
                _receivedFilterSingleDatePicker.Value = _receivedDateSingleValue.Date;
                _receivedFilterRangeFromDatePicker.Value = _receivedDateRangeFrom.Date;
                _receivedFilterRangeToDatePicker.Value = _receivedDateRangeTo.Date;

                _receivedFilterTodayRadio.Checked = _receivedDateFilterKind == CreatedDateFilterKind.Today;
                _receivedFilterSingleRadio.Checked = _receivedDateFilterKind == CreatedDateFilterKind.Single;
                _receivedFilterRangeRadio.Checked = _receivedDateFilterKind == CreatedDateFilterKind.Range;

                if (_receivedDateFilterKind == CreatedDateFilterKind.None)
                {
                    _receivedFilterTodayRadio.Checked = false;
                    _receivedFilterSingleRadio.Checked = false;
                    _receivedFilterRangeRadio.Checked = false;
                }
            }
            finally
            {
                _isSyncingReceivedFilterControls = false;
            }

            UpdateReceivedDateFilterControlsState();
        }

        private void ReceivedFilterModeControlChanged(object? sender, EventArgs e)
        {
            if (_isSyncingReceivedFilterControls)
                return;

            if (_receivedFilterSingleModeCombo != null &&
                _receivedFilterSingleRadio != null &&
                ReferenceEquals(sender, _receivedFilterSingleModeCombo) &&
                !_receivedFilterSingleRadio.Checked)
                _receivedFilterSingleRadio.Checked = true;

            if (_receivedFilterSingleDatePicker != null &&
                _receivedFilterSingleRadio != null &&
                ReferenceEquals(sender, _receivedFilterSingleDatePicker) &&
                !_receivedFilterSingleRadio.Checked)
                _receivedFilterSingleRadio.Checked = true;

            if (_receivedFilterRangeFromDatePicker != null &&
                _receivedFilterRangeRadio != null &&
                ReferenceEquals(sender, _receivedFilterRangeFromDatePicker) &&
                !_receivedFilterRangeRadio.Checked)
                _receivedFilterRangeRadio.Checked = true;

            if (_receivedFilterRangeToDatePicker != null &&
                _receivedFilterRangeRadio != null &&
                ReferenceEquals(sender, _receivedFilterRangeToDatePicker) &&
                !_receivedFilterRangeRadio.Checked)
                _receivedFilterRangeRadio.Checked = true;

            UpdateReceivedDateFilterControlsState();
        }

        private void UpdateReceivedDateFilterControlsState()
        {
            if (_receivedFilterTodayRadio == null ||
                _receivedFilterSingleRadio == null ||
                _receivedFilterSingleModeCombo == null ||
                _receivedFilterSingleDatePicker == null ||
                _receivedFilterRangeRadio == null ||
                _receivedFilterRangeFromDatePicker == null ||
                _receivedFilterRangeToDatePicker == null ||
                _receivedFilterClearButton == null ||
                _receivedFilterApplyButton == null)
                return;

            var isSingle = _receivedFilterSingleRadio.Checked;
            var isRange = _receivedFilterRangeRadio.Checked;

            _receivedFilterSingleModeCombo.Enabled = isSingle;
            _receivedFilterSingleDatePicker.Enabled = isSingle;
            _receivedFilterRangeFromDatePicker.Enabled = isRange;
            _receivedFilterRangeToDatePicker.Enabled = isRange;

            var hasSelection = _receivedFilterTodayRadio.Checked || isSingle || isRange;
            var isRangeValid = !isRange || _receivedFilterRangeFromDatePicker.Value.Date <= _receivedFilterRangeToDatePicker.Value.Date;
            var canApply = hasSelection && isRangeValid;
            var canClear = _receivedDateFilterKind != CreatedDateFilterKind.None || hasSelection;

            _receivedFilterClearButton.Enabled = canClear;
            _receivedFilterApplyButton.Enabled = canApply;
            _receivedFilterClearButton.ForeColor = canClear
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _receivedFilterApplyButton.BackColor = canApply
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _receivedFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyReceivedDateFilterFromPopup(bool clearFilter)
        {
            if (_receivedFilterTodayRadio == null ||
                _receivedFilterSingleRadio == null ||
                _receivedFilterSingleModeCombo == null ||
                _receivedFilterSingleDatePicker == null ||
                _receivedFilterRangeRadio == null ||
                _receivedFilterRangeFromDatePicker == null ||
                _receivedFilterRangeToDatePicker == null)
                return;

            var nextKind = CreatedDateFilterKind.None;
            var nextSingleMode = _receivedDateSingleMode;
            var nextSingleDate = _receivedDateSingleValue.Date;
            var nextRangeFrom = _receivedDateRangeFrom.Date;
            var nextRangeTo = _receivedDateRangeTo.Date;

            if (!clearFilter)
            {
                if (_receivedFilterTodayRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Today;
                }
                else if (_receivedFilterSingleRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Single;
                    nextSingleMode = _receivedFilterSingleModeCombo.SelectedIndex switch
                    {
                        1 => CreatedDateSingleMode.Before,
                        2 => CreatedDateSingleMode.After,
                        _ => CreatedDateSingleMode.ExactDate
                    };
                    nextSingleDate = _receivedFilterSingleDatePicker.Value.Date;
                }
                else if (_receivedFilterRangeRadio.Checked)
                {
                    var fromDate = _receivedFilterRangeFromDatePicker.Value.Date;
                    var toDate = _receivedFilterRangeToDatePicker.Value.Date;
                    if (fromDate > toDate)
                        return;

                    nextKind = CreatedDateFilterKind.Range;
                    nextRangeFrom = fromDate;
                    nextRangeTo = toDate;
                }
                else
                {
                    return;
                }
            }

            var changed = _receivedDateFilterKind != nextKind ||
                          _receivedDateSingleMode != nextSingleMode ||
                          _receivedDateSingleValue.Date != nextSingleDate ||
                          _receivedDateRangeFrom.Date != nextRangeFrom ||
                          _receivedDateRangeTo.Date != nextRangeTo;

            _receivedDateFilterKind = nextKind;
            _receivedDateSingleMode = nextSingleMode;
            _receivedDateSingleValue = nextSingleDate;
            _receivedDateRangeFrom = nextRangeFrom;
            _receivedDateRangeTo = nextRangeTo;

            if (changed)
                HandleOrdersGridChanged();

            CloseReceivedCalendarDropDown();
            _receivedFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void ReceivedDateFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (_receivedFilterLabel == null || _receivedFilterGlyph == null)
                return;

            var labelRect = _receivedFilterLabel.RectangleToScreen(_receivedFilterLabel.ClientRectangle);
            var glyphRect = _receivedFilterGlyph.RectangleToScreen(_receivedFilterGlyph.ClientRectangle);
            var clickedTrigger = labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position);

            if (_isReceivedDateCalendarOpen && !clickedTrigger)
            {
                e.Cancel = true;
                return;
            }

            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && clickedTrigger)
                _suppressNextReceivedFilterLabelClick = true;

            if (!e.Cancel)
            {
                CloseReceivedCalendarDropDown();
                _isReceivedDateCalendarOpen = false;
            }
        }
    }
}

