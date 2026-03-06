using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyManager
{
    internal sealed class OrderNumberSearchDialog : Form
    {
        private readonly TextBox _txtOrderNo;

        public string SearchText { get; private set; } = string.Empty;

        public OrderNumberSearchDialog(string currentValue)
        {
            Text = "\u041F\u043E\u0438\u0441\u043A \u043F\u043E \u043D\u043E\u043C\u0435\u0440\u0443 \u0437\u0430\u043A\u0430\u0437\u0430";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 132);

            var lblOrderNo = new Label
            {
                AutoSize = true,
                Location = new Point(16, 18),
                Text = "\u041D\u043E\u043C\u0435\u0440 \u0437\u0430\u043A\u0430\u0437\u0430:"
            };

            _txtOrderNo = new TextBox
            {
                Location = new Point(16, 42),
                Size = new Size(388, 31),
                Text = currentValue ?? string.Empty
            };

            var btnApply = new Button
            {
                Text = "\u041D\u0430\u0439\u0442\u0438",
                Location = new Point(136, 88),
                Size = new Size(86, 32)
            };
            btnApply.Click += (_, _) =>
            {
                SearchText = (_txtOrderNo.Text ?? string.Empty).Trim();
                DialogResult = DialogResult.OK;
                Close();
            };

            var btnClear = new Button
            {
                Text = "\u0421\u0431\u0440\u043E\u0441",
                Location = new Point(228, 88),
                Size = new Size(86, 32)
            };
            btnClear.Click += (_, _) =>
            {
                SearchText = string.Empty;
                DialogResult = DialogResult.OK;
                Close();
            };

            var btnCancel = new Button
            {
                Text = "\u041E\u0442\u043C\u0435\u043D\u0430",
                Location = new Point(320, 88),
                Size = new Size(84, 32),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(lblOrderNo);
            Controls.Add(_txtOrderNo);
            Controls.Add(btnApply);
            Controls.Add(btnClear);
            Controls.Add(btnCancel);

            AcceptButton = btnApply;
            CancelButton = btnCancel;
        }
    }
}
