using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MyManager
{
    public sealed class OrderLogViewerForm : Form
    {
        private readonly string _path;
        private readonly TextBox _text;

        public OrderLogViewerForm(string path, string orderId)
        {
            _path = path;
            Text = $"Лог заказа: {orderId}";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 520);
            MinimumSize = new Size(640, 400);

            _text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f)
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(6)
            };

            var btnRefresh = new Button { Text = "Обновить", AutoSize = true };
            btnRefresh.Click += (s, e) => LoadLog();

            var btnOpen = new Button { Text = "Открыть файл", AutoSize = true };
            btnOpen.Click += (s, e) =>
            {
                if (File.Exists(_path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _path, UseShellExecute = true });
            };

            panel.Controls.Add(btnOpen);
            panel.Controls.Add(btnRefresh);

            Controls.Add(_text);
            Controls.Add(panel);

            LoadLog();
        }

        private void LoadLog()
        {
            _text.Text = File.Exists(_path)
                ? File.ReadAllText(_path)
                : "Лог заказа пока не создан.";
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        }
    }
}
