using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Replica
{
    internal sealed class OrdersTreePrototypeForm : Form
    {
        private readonly OrdersTreePrototypeControl _prototypeControl;

        public OrdersTreePrototypeForm(IReadOnlyList<OrdersTreePrototypeNode> rootNodes)
        {
            _prototypeControl = new OrdersTreePrototypeControl(rootNodes);
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

            Controls.Add(_prototypeControl);

            ResumeLayout(performLayout: false);
        }
    }
}
