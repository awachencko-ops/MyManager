using System;
using System.Windows.Forms;

namespace Replica
{
    public partial class SimpleOrderForm : Form
    {
        public string OrderNumber { get; set; } = "";
        public DateTime OrderDate { get; set; } = OrderData.PlaceholderOrderDate;

        public SimpleOrderForm(OrderData? data = null)
        {
            InitializeComponent();
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
            _textNumber.TextChanged += (s, e) => ValidateForm();
            ValidateForm();
        }

        private void _btnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_textNumber.Text))
            {
                MessageBox.Show("Введите номер заказа.");
                return;
            }
            OrderNumber = _textNumber.Text.Trim();
            OrderDate = _datePicker.Value;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ValidateForm()
        {
            _btnOk.Enabled = !string.IsNullOrWhiteSpace(_textNumber.Text);
        }

        private void _btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
