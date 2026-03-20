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
        private void ShowCreatedDateFilterDropDown()
        {
            EnsureCreatedDateFilterDropDown();
            SyncCreatedDateFilterPopupState();

            if (_createdFilterDropDown == null || _createdFilterGlyph == null)
                return;

            _createdFilterDropDown.Show(_createdFilterGlyph, new Point(0, _createdFilterGlyph.Height));
        }

        private void EnsureCreatedDateFilterDropDown()
        {
            if (_createdFilterDropDown != null &&
                _createdFilterTodayRadio != null &&
                _createdFilterSingleRadio != null &&
                _createdFilterSingleModeCombo != null &&
                _createdFilterSingleDatePicker != null &&
                _createdFilterRangeRadio != null &&
                _createdFilterRangeFromDatePicker != null &&
                _createdFilterRangeToDatePicker != null &&
                _createdFilterClearButton != null &&
                _createdFilterApplyButton != null)
                return;

            var popupWidth = Math.Max((_createdFilterLabel?.Width ?? 170) + 260, 440);
            var popupHeight = 206;
            var font = _createdFilterLabel?.Font ?? Font;
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

            _createdFilterTodayRadio = new RadioButton
            {
                Location = new Point(bulletX, row1Y),
                Size = new Size(130, 30),
                Text = "Сегодня",
                Font = font,
                AutoCheck = true
            };
            _createdFilterTodayRadio.CheckedChanged += CreatedFilterModeControlChanged;

            _createdFilterSingleRadio = new RadioButton
            {
                Location = new Point(bulletX, row2Y),
                Size = new Size(24, 30),
                Font = font,
                AutoCheck = true
            };
            _createdFilterSingleRadio.CheckedChanged += CreatedFilterModeControlChanged;

            _createdFilterSingleModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                IntegralHeight = false,
                Location = new Point(contentX, row2Y - 1),
                Size = new Size(singleModeWidth, 32),
                Font = font
            };
            _createdFilterSingleModeCombo.Items.AddRange(new object[] { "Точная дата", "До", "После" });
            _createdFilterSingleModeCombo.SelectedIndexChanged += CreatedFilterModeControlChanged;

            _createdFilterSingleDatePicker = CreateCreatedFilterDatePicker(new Point(singleDateX, row2Y - 1), new Size(singleDateWidth, 32), font);
            _createdFilterSingleDatePicker.ValueChanged += CreatedFilterModeControlChanged;
            AttachCreatedDatePickerCalendarEvents(_createdFilterSingleDatePicker);

            _createdFilterRangeRadio = new RadioButton
            {
                Location = new Point(bulletX, row3Y),
                Size = new Size(58, 30),
                Text = "От",
                Font = font,
                AutoCheck = true
            };
            _createdFilterRangeRadio.CheckedChanged += CreatedFilterModeControlChanged;

            _createdFilterRangeFromDatePicker = CreateCreatedFilterDatePicker(new Point(rangeFromX, row3Y - 1), new Size(rangeDateWidth, 32), font);
            _createdFilterRangeFromDatePicker.ValueChanged += CreatedFilterModeControlChanged;
            AttachCreatedDatePickerCalendarEvents(_createdFilterRangeFromDatePicker);

            var toLabel = new Label
            {
                Location = new Point(toLabelX, row3Y),
                Size = new Size(toLabelWidth, 28),
                Text = "До",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font
            };

            _createdFilterRangeToDatePicker = CreateCreatedFilterDatePicker(new Point(rangeToX, row3Y - 1), new Size(rangeToWidth, 32), font);
            _createdFilterRangeToDatePicker.ValueChanged += CreatedFilterModeControlChanged;
            AttachCreatedDatePickerCalendarEvents(_createdFilterRangeToDatePicker);

            _createdFilterClearButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 232, buttonsY),
                Size = new Size(104, 32),
                Text = "Очистить",
                BackColor = Color.White,
                ForeColor = Color.FromArgb(168, 197, 225)
            };
            _createdFilterClearButton.FlatAppearance.BorderSize = 0;
            _createdFilterClearButton.Click += (_, _) => ApplyCreatedDateFilterFromPopup(clearFilter: true);

            _createdFilterApplyButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Location = new Point(popupWidth - 122, buttonsY),
                Size = new Size(120, 32),
                Text = "Применить",
                BackColor = Color.FromArgb(176, 212, 242),
                ForeColor = Color.White
            };
            _createdFilterApplyButton.FlatAppearance.BorderSize = 0;
            _createdFilterApplyButton.Click += (_, _) => ApplyCreatedDateFilterFromPopup(clearFilter: false);

            panel.Controls.Add(_createdFilterTodayRadio);
            panel.Controls.Add(_createdFilterSingleRadio);
            panel.Controls.Add(_createdFilterSingleModeCombo);
            panel.Controls.Add(_createdFilterSingleDatePicker);
            panel.Controls.Add(_createdFilterRangeRadio);
            panel.Controls.Add(_createdFilterRangeFromDatePicker);
            panel.Controls.Add(toLabel);
            panel.Controls.Add(_createdFilterRangeToDatePicker);
            panel.Controls.Add(_createdFilterClearButton);
            panel.Controls.Add(_createdFilterApplyButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _createdFilterDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(4)
            };
            _createdFilterDropDown.Closing += CreatedDateFilterDropDown_Closing;
            _createdFilterDropDown.Items.Add(host);
            UpdateCreatedDateFilterControlsState();
        }

        private static DateTimePicker CreateCreatedFilterDatePicker(Point location, Size size, Font font)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy",
                Location = location,
                Size = size,
                Font = font
            };
        }

        private void AttachCreatedDatePickerCalendarEvents(DateTimePicker picker)
        {
            picker.DropDown += CreatedDatePicker_DropDown;
        }

        private void CreatedDatePicker_DropDown(object? sender, EventArgs e)
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

                ShowCreatedCalendarDropDown(picker);
            }));
        }

        private void EnsureCreatedCalendarDropDown()
        {
            if (_createdCalendarDropDown != null && _createdCalendar != null && _createdCalendarOkButton != null)
                return;

            _createdCalendar = new MonthCalendar
            {
                MaxSelectionCount = 1,
                ShowToday = false,
                ShowTodayCircle = false
            };

            _createdCalendarOkButton = new Button
            {
                Text = "OK",
                FlatStyle = FlatStyle.Standard
            };
            _createdCalendarOkButton.Click += CreatedCalendarOkButton_Click;

            var calendarWidth = _createdCalendar.Width;
            var buttonHeight = 32;
            var buttonTop = _createdCalendar.Height + 6;
            _createdCalendarOkButton.Location = new Point(0, buttonTop);
            _createdCalendarOkButton.Size = new Size(calendarWidth, buttonHeight);

            var panel = new Panel
            {
                BackColor = Color.White,
                Size = new Size(calendarWidth, buttonTop + buttonHeight),
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            panel.Controls.Add(_createdCalendar);
            panel.Controls.Add(_createdCalendarOkButton);

            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _createdCalendarDropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(2)
            };
            _createdCalendarDropDown.Closed += CreatedCalendarDropDown_Closed;
            _createdCalendarDropDown.Items.Add(host);
        }

        private void ShowCreatedCalendarDropDown(DateTimePicker picker)
        {
            EnsureCreatedCalendarDropDown();
            if (_createdCalendarDropDown == null || _createdCalendar == null)
                return;

            _createdCalendarTargetPicker = picker;
            var selectedDate = picker.Value.Date;
            _createdCalendar.SetDate(selectedDate);
            _createdCalendar.SelectionStart = selectedDate;
            _createdCalendar.SelectionEnd = selectedDate;

            if (_createdCalendarDropDown.Visible)
                _createdCalendarDropDown.Close(ToolStripDropDownCloseReason.CloseCalled);

            _isCreatedDateCalendarOpen = true;
            _createdCalendarDropDown.Show(picker, new Point(0, picker.Height));
        }

        private void CreatedCalendarOkButton_Click(object? sender, EventArgs e)
        {
            if (_createdCalendarTargetPicker != null && _createdCalendar != null)
            {
                var selectedDate = _createdCalendar.SelectionStart.Date;
                if (_createdCalendarTargetPicker.Value.Date != selectedDate)
                    _createdCalendarTargetPicker.Value = selectedDate;
            }

            CloseCreatedCalendarDropDown();
        }

        private void CloseCreatedCalendarDropDown()
        {
            if (_createdCalendarDropDown?.Visible == true)
                _createdCalendarDropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            else
            {
                _isCreatedDateCalendarOpen = false;
                _createdCalendarTargetPicker = null;
            }
        }

        private void CreatedCalendarDropDown_Closed(object? sender, ToolStripDropDownClosedEventArgs e)
        {
            _isCreatedDateCalendarOpen = false;
            _createdCalendarTargetPicker = null;
        }

        private void SyncCreatedDateFilterPopupState()
        {
            if (_createdFilterTodayRadio == null ||
                _createdFilterSingleRadio == null ||
                _createdFilterSingleModeCombo == null ||
                _createdFilterSingleDatePicker == null ||
                _createdFilterRangeRadio == null ||
                _createdFilterRangeFromDatePicker == null ||
                _createdFilterRangeToDatePicker == null)
                return;

            _isSyncingCreatedFilterControls = true;
            try
            {
                _createdFilterSingleModeCombo.SelectedIndex = _createdDateSingleMode switch
                {
                    CreatedDateSingleMode.Before => 1,
                    CreatedDateSingleMode.After => 2,
                    _ => 0
                };
                _createdFilterSingleDatePicker.Value = _createdDateSingleValue.Date;
                _createdFilterRangeFromDatePicker.Value = _createdDateRangeFrom.Date;
                _createdFilterRangeToDatePicker.Value = _createdDateRangeTo.Date;

                _createdFilterTodayRadio.Checked = _createdDateFilterKind == CreatedDateFilterKind.Today;
                _createdFilterSingleRadio.Checked = _createdDateFilterKind == CreatedDateFilterKind.Single;
                _createdFilterRangeRadio.Checked = _createdDateFilterKind == CreatedDateFilterKind.Range;

                if (_createdDateFilterKind == CreatedDateFilterKind.None)
                {
                    _createdFilterTodayRadio.Checked = false;
                    _createdFilterSingleRadio.Checked = false;
                    _createdFilterRangeRadio.Checked = false;
                }
            }
            finally
            {
                _isSyncingCreatedFilterControls = false;
            }

            UpdateCreatedDateFilterControlsState();
        }

        private void CreatedFilterModeControlChanged(object? sender, EventArgs e)
        {
            if (_isSyncingCreatedFilterControls)
                return;

            if (_createdFilterSingleModeCombo != null &&
                _createdFilterSingleRadio != null &&
                ReferenceEquals(sender, _createdFilterSingleModeCombo) &&
                !_createdFilterSingleRadio.Checked)
                _createdFilterSingleRadio.Checked = true;

            if (_createdFilterSingleDatePicker != null &&
                _createdFilterSingleRadio != null &&
                ReferenceEquals(sender, _createdFilterSingleDatePicker) &&
                !_createdFilterSingleRadio.Checked)
                _createdFilterSingleRadio.Checked = true;

            if (_createdFilterRangeFromDatePicker != null &&
                _createdFilterRangeRadio != null &&
                ReferenceEquals(sender, _createdFilterRangeFromDatePicker) &&
                !_createdFilterRangeRadio.Checked)
                _createdFilterRangeRadio.Checked = true;

            if (_createdFilterRangeToDatePicker != null &&
                _createdFilterRangeRadio != null &&
                ReferenceEquals(sender, _createdFilterRangeToDatePicker) &&
                !_createdFilterRangeRadio.Checked)
                _createdFilterRangeRadio.Checked = true;

            UpdateCreatedDateFilterControlsState();
        }

        private void UpdateCreatedDateFilterControlsState()
        {
            if (_createdFilterTodayRadio == null ||
                _createdFilterSingleRadio == null ||
                _createdFilterSingleModeCombo == null ||
                _createdFilterSingleDatePicker == null ||
                _createdFilterRangeRadio == null ||
                _createdFilterRangeFromDatePicker == null ||
                _createdFilterRangeToDatePicker == null ||
                _createdFilterClearButton == null ||
                _createdFilterApplyButton == null)
                return;

            var isSingle = _createdFilterSingleRadio.Checked;
            var isRange = _createdFilterRangeRadio.Checked;

            _createdFilterSingleModeCombo.Enabled = isSingle;
            _createdFilterSingleDatePicker.Enabled = isSingle;
            _createdFilterRangeFromDatePicker.Enabled = isRange;
            _createdFilterRangeToDatePicker.Enabled = isRange;

            var hasSelection = _createdFilterTodayRadio.Checked || isSingle || isRange;
            var isRangeValid = !isRange || _createdFilterRangeFromDatePicker.Value.Date <= _createdFilterRangeToDatePicker.Value.Date;
            var canApply = hasSelection && isRangeValid;
            var canClear = _createdDateFilterKind != CreatedDateFilterKind.None || hasSelection;

            _createdFilterClearButton.Enabled = canClear;
            _createdFilterApplyButton.Enabled = canApply;
            _createdFilterClearButton.ForeColor = canClear
                ? Color.FromArgb(77, 147, 222)
                : Color.FromArgb(168, 197, 225);
            _createdFilterApplyButton.BackColor = canApply
                ? Color.FromArgb(33, 127, 203)
                : Color.FromArgb(176, 212, 242);
            _createdFilterApplyButton.ForeColor = Color.White;
        }

        private void ApplyCreatedDateFilterFromPopup(bool clearFilter)
        {
            if (_createdFilterTodayRadio == null ||
                _createdFilterSingleRadio == null ||
                _createdFilterSingleModeCombo == null ||
                _createdFilterSingleDatePicker == null ||
                _createdFilterRangeRadio == null ||
                _createdFilterRangeFromDatePicker == null ||
                _createdFilterRangeToDatePicker == null)
                return;

            var nextKind = CreatedDateFilterKind.None;
            var nextSingleMode = _createdDateSingleMode;
            var nextSingleDate = _createdDateSingleValue.Date;
            var nextRangeFrom = _createdDateRangeFrom.Date;
            var nextRangeTo = _createdDateRangeTo.Date;

            if (!clearFilter)
            {
                if (_createdFilterTodayRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Today;
                }
                else if (_createdFilterSingleRadio.Checked)
                {
                    nextKind = CreatedDateFilterKind.Single;
                    nextSingleMode = _createdFilterSingleModeCombo.SelectedIndex switch
                    {
                        1 => CreatedDateSingleMode.Before,
                        2 => CreatedDateSingleMode.After,
                        _ => CreatedDateSingleMode.ExactDate
                    };
                    nextSingleDate = _createdFilterSingleDatePicker.Value.Date;
                }
                else if (_createdFilterRangeRadio.Checked)
                {
                    var fromDate = _createdFilterRangeFromDatePicker.Value.Date;
                    var toDate = _createdFilterRangeToDatePicker.Value.Date;
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

            var changed = _createdDateFilterKind != nextKind ||
                          _createdDateSingleMode != nextSingleMode ||
                          _createdDateSingleValue.Date != nextSingleDate ||
                          _createdDateRangeFrom.Date != nextRangeFrom ||
                          _createdDateRangeTo.Date != nextRangeTo;

            _createdDateFilterKind = nextKind;
            _createdDateSingleMode = nextSingleMode;
            _createdDateSingleValue = nextSingleDate;
            _createdDateRangeFrom = nextRangeFrom;
            _createdDateRangeTo = nextRangeTo;

            if (changed)
                HandleOrdersGridChanged();

            CloseCreatedCalendarDropDown();
            _createdFilterDropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void CreatedDateFilterDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (_createdFilterLabel == null || _createdFilterGlyph == null)
                return;

            var labelRect = _createdFilterLabel.RectangleToScreen(_createdFilterLabel.ClientRectangle);
            var glyphRect = _createdFilterGlyph.RectangleToScreen(_createdFilterGlyph.ClientRectangle);
            var clickedTrigger = labelRect.Contains(Cursor.Position) || glyphRect.Contains(Cursor.Position);

            if (_isCreatedDateCalendarOpen && !clickedTrigger)
            {
                e.Cancel = true;
                return;
            }

            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && clickedTrigger)
                _suppressNextCreatedFilterLabelClick = true;

            if (!e.Cancel)
            {
                CloseCreatedCalendarDropDown();
                _isCreatedDateCalendarOpen = false;
            }
        }

    }
}

