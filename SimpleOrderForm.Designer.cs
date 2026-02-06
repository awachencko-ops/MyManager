namespace MyManager
{
    partial class SimpleOrderForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _textNumber = new TextBox();
            _datePicker = new DateTimePicker();
            _btnOk = new Button();
            _btnCancel = new Button();
            labelNum = new Label();
            labelDate = new Label();
            SuspendLayout();
            // 
            // _textNumber
            // 
            _textNumber.Location = new Point(195, 44);
            _textNumber.Name = "_textNumber";
            _textNumber.Size = new Size(208, 31);
            _textNumber.TabIndex = 0;
            // 
            // _datePicker
            // 
            _datePicker.Format = DateTimePickerFormat.Short;
            _datePicker.Location = new Point(195, 96);
            _datePicker.Name = "_datePicker";
            _datePicker.Size = new Size(208, 31);
            _datePicker.TabIndex = 1;
            // 
            // _btnOk
            // 
            _btnOk.Location = new Point(306, 184);
            _btnOk.Name = "_btnOk";
            _btnOk.Size = new Size(97, 35);
            _btnOk.TabIndex = 2;
            _btnOk.Text = "ОК";
            _btnOk.UseVisualStyleBackColor = true;
            _btnOk.Click += _btnOk_Click;
            // 
            // _btnCancel
            // 
            _btnCancel.Location = new Point(197, 184);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(97, 35);
            _btnCancel.TabIndex = 3;
            _btnCancel.Text = "Отмена";
            _btnCancel.UseVisualStyleBackColor = true;
            _btnCancel.Click += _btnCancel_Click;
            // 
            // labelNum
            // 
            labelNum.AutoSize = true;
            labelNum.Location = new Point(36, 48);
            labelNum.Name = "labelNum";
            labelNum.Size = new Size(130, 25);
            labelNum.TabIndex = 1;
            labelNum.Text = "Номер заказа:";
            // 
            // labelDate
            // 
            labelDate.AutoSize = true;
            labelDate.Location = new Point(36, 102);
            labelDate.Name = "labelDate";
            labelDate.Size = new Size(110, 25);
            labelDate.TabIndex = 0;
            labelDate.Text = "Дата заказа:";
            // 
            // SimpleOrderForm
            // 
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
            ClientSize = new Size(448, 273);
            Controls.Add(labelDate);
            Controls.Add(labelNum);
            Controls.Add(_btnCancel);
            Controls.Add(_btnOk);
            Controls.Add(_datePicker);
            Controls.Add(_textNumber);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SimpleOrderForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Данные заказа";
            ResumeLayout(false);
            PerformLayout();
        }

        private TextBox _textNumber;
        private DateTimePicker _datePicker;
        private Button _btnOk;
        private Button _btnCancel;
        private Label labelNum;
        private Label labelDate;
    }
}