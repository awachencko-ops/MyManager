using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Replica
{
    public partial class SimpleOrderForm : Form
    {
        public string OrderNumber { get; set; } = "";
        public DateTime OrderDate { get; set; } = OrderData.PlaceholderOrderDate;

        // date input typed digits counter (counts digits typed into date field)
        // thresholds: 2 -> move to month, 4 -> move to year, 8 -> year complete -> focus OK
        private int _dateTypedDigits = 0;
        // when true, automatic right-shift on thresholds is enabled; any manual navigation or mouse
        // interaction disables this until the control is re-entered
        private bool _dateAutoEnabled = true;
        // suppress automatic disabling when we programmatically send navigation keys
        private int _suppressAutoDisable = 0;

        public SimpleOrderForm(OrderData? data = null)
        {
            InitializeComponent();

            // Order number: only digits, max 5
            _textNumber.MaxLength = 5;
            _textNumber.KeyPress += _textNumber_KeyPress;
            _textNumber.TextChanged += _textNumber_TextChanged;
            _textNumber.KeyDown += _textNumber_KeyDown;

            // Date picker handlers
            _datePicker.Enter += _datePicker_Enter;
            _datePicker.KeyPress += _datePicker_KeyPress;
            _datePicker.KeyDown += _datePicker_KeyDown;
            //_datePicker.MouseDown += _datePicker_MouseDown;
            _datePicker.ValueChanged += _datePicker_ValueChanged;
            _datePicker.CloseUp += _datePicker_CloseUp;

            if (data != null)
            {
                _textNumber.Text = data.Id;
                _datePicker.Value = data.OrderDate == default
                    ? OrderData.PlaceholderOrderDate
                    : data.OrderDate;
            }
            else
            {
                _datePicker.Value = OrderData.PlaceholderOrderDate;
            }

            ValidateForm();
        }

        private void _btnOk_Click(object sender, EventArgs e)
        {
            if (_textNumber.Text.Length != 5)
            {
                MessageBox.Show("Введите ровно 5 цифр номера заказа.");
                return;
            }

            OrderNumber = _textNumber.Text.Trim();
            OrderDate = _datePicker.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ValidateForm()
        {
            _btnOk.Enabled = _textNumber.Text.Length == 5;
        }

        private void _btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // ---------------- number handlers ----------------
        private void _textNumber_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            if (!char.IsDigit(e.KeyChar)) e.Handled = true;
        }

        private void _textNumber_TextChanged(object? sender, EventArgs e)
        {
            // keep only digits
            var digitsOnly = new string((_textNumber.Text ?? string.Empty).Where(char.IsDigit).ToArray());
            if (!string.Equals(digitsOnly, _textNumber.Text, StringComparison.Ordinal))
            {
                var caret = _textNumber.SelectionStart;
                _textNumber.Text = digitsOnly;
                _textNumber.SelectionStart = Math.Min(caret, _textNumber.TextLength);
            }

            // enforce length
            if (_textNumber.TextLength > 5)
            {
                _textNumber.Text = _textNumber.Text.Substring(0, 5);
                _textNumber.SelectionStart = _textNumber.TextLength;
            }

            ValidateForm();

            if (_textNumber.TextLength == 5 && _textNumber.Focused)
            {
                // move focus to date (start of editing)
                BeginInvoke(new Action(() => _datePicker.Focus()));
            }
        }

        private void _textNumber_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Down) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            _datePicker.Focus();
        }

        // ---------------- date handlers ----------------
        private void _datePicker_Enter(object? sender, EventArgs e)
        {
            // reset typed-digit counter when entering date field
            _dateTypedDigits = 0;
            _dateAutoEnabled = true;
        }

        private void _datePicker_KeyDown(object? sender, KeyEventArgs e)
        {
            // Detect manual navigation or edits — disable auto behavior, but ignore when suppression active
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (_suppressAutoDisable > 0)
                {
                    _suppressAutoDisable = Math.Max(0, _suppressAutoDisable - 1);
                }
                else
                {
                    _dateAutoEnabled = false;
                    return;
                }
            }

            // Backspace/Delete considered manual deviation too, but still adjust counter
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                if (_suppressAutoDisable > 0)
                {
                    _suppressAutoDisable = Math.Max(0, _suppressAutoDisable - 1);
                }
                else
                {
                    _dateAutoEnabled = false;
                    if (_dateTypedDigits > 0) _dateTypedDigits = Math.Max(0, _dateTypedDigits - 1);
                    return;
                }
            }
        }

        private void _datePicker_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            if (!char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
                return;
            }
            // digit typed into date control
            _dateTypedDigits++;

            if (!_dateAutoEnabled)
            {
                // auto behaviour disabled by manual navigation; just track typed digits
                return;
            }

            if (_dateTypedDigits == 2 || _dateTypedDigits == 4)
            {
                // move right to next segment
                // mark suppression so our own simulated key doesn't disable auto behaviour
                _suppressAutoDisable++;
                BeginInvoke(new Action(() =>
                {
                    _datePicker.Focus();
                    SendRightToDatePickerChild();
                }));
            }

            if (_dateTypedDigits >= 8)
            {
                // year complete
                BeginInvoke(new Action(() => _btnOk.Focus()));
            }
        }

        private void _datePicker_ValueChanged(object? sender, EventArgs e)
        {
            // keep segments in sync optionally (no extra action required)
        }

        private void _datePicker_CloseUp(object? sender, EventArgs e)
        {
            // when user picks via calendar, set segment state as finished
            _dateTypedDigits = 8;
        }

        // Send a WM_KEYDOWN/WM_KEYUP right arrow specifically to the internal edit child of DateTimePicker
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_RIGHT = 0x27;

        private void SendRightToDatePickerChild()
        {
            try
            {
                var h = _datePicker.Handle;
                // DateTimePicker's edit control class is 'SysDateTimePick32' with child 'Edit'
                var child = FindWindowEx(h, IntPtr.Zero, "SysDateTimePick32", null);
                if (child == IntPtr.Zero)
                {
                    // try to find Edit child directly
                    child = FindWindowEx(h, IntPtr.Zero, "Edit", null);
                }

                if (child != IntPtr.Zero)
                {
                    PostMessage(child, WM_KEYDOWN, new IntPtr(VK_RIGHT), IntPtr.Zero);
                    PostMessage(child, WM_KEYUP, new IntPtr(VK_RIGHT), IntPtr.Zero);
                }
                else
                {
                    // fallback
                    SendKeys.SendWait("{RIGHT}");
                }
            }
            catch
            {
                SendKeys.SendWait("{RIGHT}");
            }
        }
    }
}
