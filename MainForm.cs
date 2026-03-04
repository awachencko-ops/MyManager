using System;
using System.Windows.Forms;

namespace MyManager
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

            // просто чтобы было видно, что всё живое
            Load += (_, __) =>
            {
                var root = new TreeNode("C60-C70-713D");
                root.Nodes.Add("Все задания");
                root.Nodes.Add("Удержанные");
                root.Nodes.Add("Напечатано");
                root.Nodes.Add("В архиве");
                root.Nodes.Add("Выполняется печать");
                treeView1.Nodes.Add(root);
                root.Expand();
            };
        }

        // обработчик нажатия кнопок в ToolStrip
        private void TsMainActions_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            // можно раскидать switch по кнопкам при необходимости
            // MessageBox.Show($"Нажато: {e.ClickedItem.Text}");
        }
    }
}