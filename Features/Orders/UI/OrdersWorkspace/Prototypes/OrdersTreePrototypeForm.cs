using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Replica
{
    internal sealed class OrdersTreePrototypeForm : Form
    {
        private readonly OrdersTreePrototypeControl _prototypeControl;
        private readonly Func<IReadOnlyList<OrdersTreePrototypeNode>>? _snapshotProvider;
        private readonly Label _lblHint = new();

        public OrdersTreePrototypeForm(
            IReadOnlyList<OrdersTreePrototypeNode> rootNodes,
            Func<IReadOnlyList<OrdersTreePrototypeNode>>? snapshotProvider = null)
        {
            _snapshotProvider = snapshotProvider;
            _prototypeControl = new OrdersTreePrototypeControl(rootNodes);
            _prototypeControl.RefreshRequested += (_, _) => RefreshSnapshot();
            InitializeComponent();
        }

        public void LoadRoots(IReadOnlyList<OrdersTreePrototypeNode>? roots)
        {
            _prototypeControl.LoadRoots(roots);
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1480, 860);
            MinimumSize = new Size(1100, 600);
            StartPosition = FormStartPosition.CenterParent;
            Text = "Прототип таблицы заказов (OLV TreeListView)";
            KeyPreview = true;
            KeyDown += OrdersTreePrototypeForm_KeyDown;

            _lblHint.Dock = DockStyle.Bottom;
            _lblHint.Height = 28;
            _lblHint.TextAlign = ContentAlignment.MiddleRight;
            _lblHint.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _lblHint.ForeColor = Color.FromArgb(90, 97, 108);
            _lblHint.Padding = new Padding(8, 0, 10, 0);
            _lblHint.Text = "F5: обновить снимок из рабочей таблицы";

            Controls.Add(_lblHint);
            Controls.Add(_prototypeControl);

            ResumeLayout(performLayout: false);
        }

        private void OrdersTreePrototypeForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F5)
                return;

            RefreshSnapshot();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void RefreshSnapshot()
        {
            if (_snapshotProvider == null)
                return;

            try
            {
                var roots = _snapshotProvider.Invoke();
                _prototypeControl.LoadRoots(roots);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Не удалось обновить прототип:\n{ex.Message}",
                    "OLV prototype",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
