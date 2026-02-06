using System;
using System.Windows.Forms;

namespace MyManager
{
    public partial class SimpleOrderForm : Form
    {
        public string OrderNumber { get; private set; } = "";
        public DateTime OrderDate { get; private set; } = DateTime.Now;

        public SimpleOrderForm(OrderData? data = null)
        {
            InitializeComponent();

            if (data != null)
            {
                _textNumber.Text = data.Id;
                if (data.OrderDate != default) _datePicker.Value = data.OrderDate;
            }
        }

        private void _btnOk_Click(object sender, EventArgs e)
        {
            string num = _textNumber.Text.Trim();
            if (string.IsNullOrWhiteSpace(num))
            {
                MessageBox.Show("Введите номер заказа.");
                return;
            }

            OrderNumber = num;
            OrderDate = _datePicker.Value;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void _btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}