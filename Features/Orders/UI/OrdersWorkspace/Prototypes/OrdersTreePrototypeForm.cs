using BrightIdeasSoftware;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Replica
{
    internal sealed class OrdersTreePrototypeForm : Form
    {
        private readonly TreeListView _treeListView = new();
        private readonly ImageList _statusImageList = new();
        private readonly IReadOnlyList<OrdersTreePrototypeNode> _rootNodes;

        public OrdersTreePrototypeForm(IReadOnlyList<OrdersTreePrototypeNode> rootNodes)
        {
            _rootNodes = rootNodes ?? Array.Empty<OrdersTreePrototypeNode>();

            InitializeComponent();
            ConfigureTreeListView();
            PopulateTree();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1480, 860);
            MinimumSize = new Size(1100, 600);
            StartPosition = FormStartPosition.CenterParent;
            Text = "Прототип таблицы заказов (TreeListView)";

            Controls.Add(_treeListView);
            ResumeLayout(performLayout: false);
        }

        private void ConfigureTreeListView()
        {
            _treeListView.Dock = DockStyle.Fill;
            _treeListView.FullRowSelect = true;
            _treeListView.HideSelection = false;
            _treeListView.GridLines = true;
            _treeListView.MultiSelect = false;
            _treeListView.ShowGroups = false;
            _treeListView.View = View.Details;
            _treeListView.UseAlternatingBackColors = true;
            _treeListView.AlternateRowBackColor = Color.FromArgb(251, 252, 254);
            _treeListView.EmptyListMsg = "Нет заказов для отображения в прототипе.";
            _treeListView.EmptyListMsgFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            _treeListView.RowFormatter = PrototypeRowFormatter;
            _treeListView.CanExpandGetter = rowObject => rowObject is OrdersTreePrototypeNode node && node.HasChildren;
            _treeListView.ChildrenGetter = rowObject =>
                rowObject is OrdersTreePrototypeNode node
                    ? node.Children
                    : Array.Empty<OrdersTreePrototypeNode>();
            _treeListView.ItemActivate += TreeListView_ItemActivate;
            _treeListView.KeyDown += TreeListView_KeyDown;
            _treeListView.TreeColumnRenderer = new TreeListView.TreeRenderer
            {
                IsShowGlyphs = true,
                IsShowLines = true,
                UseTriangles = true
            };
            ConfigureStatusImageList();
            _treeListView.SmallImageList = _statusImageList;

            var columns = new[]
            {
                BuildColumn(
                    title: "Заказ / файл",
                    width: 320,
                    aspectGetter: row => ((OrdersTreePrototypeNode)row).Title,
                    imageGetter: row => ResolveStatusIconKey((OrdersTreePrototypeNode)row),
                    textAlign: HorizontalAlignment.Left,
                    fillsFreeSpace: true),
                BuildColumn("Состояние", 140, row => ((OrdersTreePrototypeNode)row).Status),
                BuildColumn("Прием файла", 220, row => ((OrdersTreePrototypeNode)row).Source),
                BuildColumn("Подготовка", 220, row => ((OrdersTreePrototypeNode)row).Prepared),
                BuildColumn("Проверка файлов", 150, row => ((OrdersTreePrototypeNode)row).PitStop),
                BuildColumn("Спуск полос", 130, row => ((OrdersTreePrototypeNode)row).Imposing),
                BuildColumn("Готов к печати", 220, row => ((OrdersTreePrototypeNode)row).Print),
                BuildColumn("Заказ принят", 120, row => ((OrdersTreePrototypeNode)row).Received, HorizontalAlignment.Center),
                BuildColumn("В препрессе", 120, row => ((OrdersTreePrototypeNode)row).Created, HorizontalAlignment.Center)
            };

            _treeListView.AllColumns.Clear();
            _treeListView.Columns.Clear();
            _treeListView.AllColumns.AddRange(columns);
            _treeListView.Columns.AddRange(columns);
        }

        private void PopulateTree()
        {
            _treeListView.BeginUpdate();
            try
            {
                _treeListView.Roots = _rootNodes;
            }
            finally
            {
                _treeListView.EndUpdate();
            }
        }

        private void TreeListView_ItemActivate(object? sender, EventArgs e)
        {
            ToggleSelectedNodeExpansion();
        }

        private void TreeListView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                ToggleSelectedNodeExpansion();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Right)
            {
                ExpandSelectedNode();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Left)
            {
                CollapseSelectedNode();
                e.Handled = true;
            }
        }

        private void ToggleSelectedNodeExpansion()
        {
            if (_treeListView.SelectedObject is not OrdersTreePrototypeNode node || !node.HasChildren)
                return;

            _treeListView.ToggleExpansion(node);
        }

        private void ExpandSelectedNode()
        {
            if (_treeListView.SelectedObject is not OrdersTreePrototypeNode node || !node.HasChildren)
                return;

            if (_treeListView.IsExpanded(node))
                return;

            _treeListView.Expand(node);
        }

        private void CollapseSelectedNode()
        {
            if (_treeListView.SelectedObject is not OrdersTreePrototypeNode node || !node.HasChildren)
                return;

            if (!_treeListView.IsExpanded(node))
                return;

            _treeListView.Collapse(node);
        }

        private static OLVColumn BuildColumn(
            string title,
            int width,
            AspectGetterDelegate aspectGetter,
            HorizontalAlignment textAlign = HorizontalAlignment.Left,
            bool fillsFreeSpace = false,
            ImageGetterDelegate? imageGetter = null)
        {
            return new OLVColumn(title, string.Empty)
            {
                Width = width,
                IsEditable = false,
                TextAlign = textAlign,
                FillsFreeSpace = fillsFreeSpace,
                AspectGetter = aspectGetter,
                ImageGetter = imageGetter
            };
        }

        private void ConfigureStatusImageList()
        {
            _statusImageList.ColorDepth = ColorDepth.Depth32Bit;
            _statusImageList.ImageSize = new Size(14, 14);
            _statusImageList.Images.Clear();

            _statusImageList.Images.Add("status-group", CreateStatusDot(Color.FromArgb(78, 122, 196)));
            _statusImageList.Images.Add("status-completed", CreateStatusDot(Color.FromArgb(52, 168, 83)));
            _statusImageList.Images.Add("status-processing", CreateStatusDot(Color.FromArgb(245, 166, 35)));
            _statusImageList.Images.Add("status-error", CreateStatusDot(Color.FromArgb(219, 68, 55)));
            _statusImageList.Images.Add("status-waiting", CreateStatusDot(Color.FromArgb(130, 141, 156)));
        }

        private static Bitmap CreateStatusDot(Color color)
        {
            var bitmap = new Bitmap(14, 14);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var dotBrush = new SolidBrush(color);
            using var ringPen = new Pen(Color.FromArgb(208, 216, 226));
            graphics.FillEllipse(dotBrush, 2, 2, 10, 10);
            graphics.DrawEllipse(ringPen, 1, 1, 11, 11);

            return bitmap;
        }

        private static string ResolveStatusIconKey(OrdersTreePrototypeNode node)
        {
            if (node == null)
                return "status-waiting";

            if (node.HasChildren)
                return "status-group";

            var status = (node.Status ?? string.Empty).Trim();
            if (status.Length == 0)
                return "status-waiting";

            if (status.IndexOf("ошиб", StringComparison.OrdinalIgnoreCase) >= 0)
                return "status-error";

            if (status.IndexOf("обрабаты", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("сборк", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "status-processing";
            }

            if (status.IndexOf("заверш", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("обработ", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("архив", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("готов", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "status-completed";
            }

            return "status-waiting";
        }

        private static void PrototypeRowFormatter(OLVListItem item)
        {
            if (item.RowObject is not OrdersTreePrototypeNode node)
                return;

            item.ForeColor = Color.FromArgb(32, 37, 44);
            if (!node.IsContainer)
            {
                item.BackColor = Color.FromArgb(255, 253, 248);
                return;
            }

            if (node.HasChildren)
                item.BackColor = Color.FromArgb(255, 252, 244);
        }
    }

    internal sealed class OrdersTreePrototypeNode
    {
        public OrdersTreePrototypeNode(
            string title,
            string status,
            string source,
            string prepared,
            string pitStop,
            string imposing,
            string print,
            string received,
            string created,
            bool isContainer,
            IReadOnlyList<OrdersTreePrototypeNode>? children)
        {
            Title = title ?? string.Empty;
            Status = status ?? string.Empty;
            Source = source ?? string.Empty;
            Prepared = prepared ?? string.Empty;
            PitStop = pitStop ?? string.Empty;
            Imposing = imposing ?? string.Empty;
            Print = print ?? string.Empty;
            Received = received ?? string.Empty;
            Created = created ?? string.Empty;
            IsContainer = isContainer;
            Children = children ?? Array.Empty<OrdersTreePrototypeNode>();
        }

        public string Title { get; }
        public string Status { get; }
        public string Source { get; }
        public string Prepared { get; }
        public string PitStop { get; }
        public string Imposing { get; }
        public string Print { get; }
        public string Received { get; }
        public string Created { get; }
        public bool IsContainer { get; }
        public IReadOnlyList<OrdersTreePrototypeNode> Children { get; }
        public bool HasChildren => Children.Count > 0;
    }
}
