using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyManager
{
    public class SimpleOrderForm : Form
    {
        private readonly TextBox _textNumber = new TextBox();
        private readonly DateTimePicker _datePicker = new DateTimePicker();
        private readonly Button _btnOk = new Button();
        private readonly Button _btnCancel = new Button();

        public string OrderNumber { get; private set; } = "";
        public DateTime OrderDate { get; private set; } = DateTime.Now;

        public SimpleOrderForm(OrderData data = null)
        {
            Text = "Данные заказа";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 180);

            var labelNumber = new Label { Text = "Номер заказа:", AutoSize = true, Location = new Point(20, 20) };
            _textNumber.Location = new Point(20, 45);
            _textNumber.Width = 300;

            var labelDate = new Label { Text = "Дата заказа:", AutoSize = true, Location = new Point(20, 80) };
            _datePicker.Location = new Point(20, 105);
            _datePicker.Width = 200;
            _datePicker.Format = DateTimePickerFormat.Short;

            _btnOk.Text = "ОК";
            _btnOk.Location = new Point(160, 140);
            _btnOk.Click += OnOk;

            _btnCancel.Text = "Отмена";
            _btnCancel.Location = new Point(245, 140);
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { labelNumber, _textNumber, labelDate, _datePicker, _btnOk, _btnCancel });
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            if (data != null)
            {
                _textNumber.Text = data.Id;
                _datePicker.Value = data.OrderDate == default ? DateTime.Now : data.OrderDate;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            string number = (_textNumber.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(number))
            {
                MessageBox.Show("Введите номер заказа.");
                return;
            }

            OrderNumber = number;
            OrderDate = _datePicker.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
