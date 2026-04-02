using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BrightIdeasSoftware;

namespace Replica
{
    internal sealed class OrdersTreePrototypeControl : UserControl
    {
        private readonly Panel _topPanel = new();
        private readonly Button _btnExpandAll = new();
        private readonly Button _btnCollapseAll = new();
        private readonly Button _btnResetSort = new();
        private readonly Label _lblQuickFilter = new();
        private readonly TextBox _tbQuickFilter = new();
        private readonly Label _lblSummary = new();
        private readonly TreeListView _treeListView = new();
        private readonly ImageList _statusImageList = new();
        private IReadOnlyList<OrdersTreePrototypeNode> _rootNodes;
        private OLVColumn? _defaultSortColumn;

        public OrdersTreePrototypeControl(IReadOnlyList<OrdersTreePrototypeNode>? rootNodes)
        {
            _rootNodes = rootNodes ?? Array.Empty<OrdersTreePrototypeNode>();

            InitializeControlLayout();
            ConfigureToolbar();
            ConfigureTreeListView();
            ApplyQuickFilter();
        }

        public void LoadRoots(IReadOnlyList<OrdersTreePrototypeNode>? roots)
        {
            _rootNodes = roots ?? Array.Empty<OrdersTreePrototypeNode>();
            ApplyQuickFilter();
        }

        private void InitializeControlLayout()
        {
            Dock = DockStyle.Fill;
            Controls.Add(_treeListView);
            Controls.Add(_topPanel);
        }

        private void ConfigureToolbar()
        {
            _topPanel.Dock = DockStyle.Top;
            _topPanel.Height = 44;
            _topPanel.Padding = new Padding(8, 6, 8, 6);

            _btnExpandAll.Text = "Развернуть всё";
            _btnExpandAll.Size = new Size(140, 32);
            _btnExpandAll.Location = new Point(8, 6);
            _btnExpandAll.Click += (_, _) => _treeListView.ExpandAll();

            _btnCollapseAll.Text = "Свернуть всё";
            _btnCollapseAll.Size = new Size(130, 32);
            _btnCollapseAll.Location = new Point(156, 6);
            _btnCollapseAll.Click += (_, _) => _treeListView.CollapseAll();

            _btnResetSort.Text = "Сбросить сорт";
            _btnResetSort.Size = new Size(125, 32);
            _btnResetSort.Location = new Point(292, 6);
            _btnResetSort.Click += (_, _) => ApplyDefaultSort();

            _lblQuickFilter.Text = "Быстрый фильтр:";
            _lblQuickFilter.AutoSize = true;
            _lblQuickFilter.Location = new Point(434, 12);

            _tbQuickFilter.Location = new Point(552, 8);
            _tbQuickFilter.Size = new Size(320, 31);
            _tbQuickFilter.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _tbQuickFilter.TextChanged += (_, _) => ApplyQuickFilter();

            _lblSummary.AutoSize = true;
            _lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblSummary.Font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
            _lblSummary.Location = new Point(890, 12);

            _topPanel.Controls.Add(_btnExpandAll);
            _topPanel.Controls.Add(_btnCollapseAll);
            _topPanel.Controls.Add(_btnResetSort);
            _topPanel.Controls.Add(_lblQuickFilter);
            _topPanel.Controls.Add(_tbQuickFilter);
            _topPanel.Controls.Add(_lblSummary);

            _topPanel.Resize += (_, _) =>
            {
                var summaryX = Math.Max(880, _topPanel.Width - _lblSummary.Width - 12);
                _lblSummary.Location = new Point(summaryX, _lblSummary.Location.Y);
                _tbQuickFilter.Width = Math.Max(220, summaryX - _tbQuickFilter.Left - 16);
            };
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
            _treeListView.ShowSortIndicators = true;
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
            _defaultSortColumn = columns[8];
            ApplyDefaultSort();
        }

        private void PopulateTree(IReadOnlyList<OrdersTreePrototypeNode> roots)
        {
            _treeListView.BeginUpdate();
            try
            {
                _treeListView.Roots = roots;
            }
            finally
            {
                _treeListView.EndUpdate();
            }

            UpdateSummary(roots);
        }

        private void ApplyQuickFilter()
        {
            var filteredRoots = BuildFilteredRoots(_tbQuickFilter.Text);
            PopulateTree(filteredRoots);
            ApplyDefaultSort();
        }

        private void ApplyDefaultSort()
        {
            if (_defaultSortColumn == null)
                return;

            _treeListView.Sort(_defaultSortColumn, SortOrder.Descending);
        }

        private IReadOnlyList<OrdersTreePrototypeNode> BuildFilteredRoots(string? query)
        {
            var normalized = (query ?? string.Empty).Trim();
            if (normalized.Length == 0)
                return _rootNodes;

            var result = new List<OrdersTreePrototypeNode>();
            foreach (var root in _rootNodes)
            {
                if (TryBuildFilteredNode(root, normalized, out var filtered))
                    result.Add(filtered);
            }

            return result;
        }

        private static bool TryBuildFilteredNode(
            OrdersTreePrototypeNode source,
            string query,
            out OrdersTreePrototypeNode filtered)
        {
            var sourceMatches = NodeMatchesQuery(source, query);
            if (sourceMatches)
            {
                filtered = source;
                return true;
            }

            if (!source.HasChildren)
            {
                filtered = source;
                return false;
            }

            var childMatches = new List<OrdersTreePrototypeNode>();
            foreach (var child in source.Children)
            {
                if (TryBuildFilteredNode(child, query, out var filteredChild))
                    childMatches.Add(filteredChild);
            }

            if (childMatches.Count > 0)
            {
                filtered = source.WithChildren(childMatches);
                return true;
            }

            filtered = source;
            return false;
        }

        private static bool NodeMatchesQuery(OrdersTreePrototypeNode node, string query)
        {
            return ContainsIgnoreCase(node.Title, query)
                || ContainsIgnoreCase(node.Status, query)
                || ContainsIgnoreCase(node.Source, query)
                || ContainsIgnoreCase(node.Prepared, query)
                || ContainsIgnoreCase(node.PitStop, query)
                || ContainsIgnoreCase(node.Imposing, query)
                || ContainsIgnoreCase(node.Print, query)
                || ContainsIgnoreCase(node.Received, query)
                || ContainsIgnoreCase(node.Created, query);
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateSummary(IReadOnlyList<OrdersTreePrototypeNode> roots)
        {
            var all = Flatten(roots);
            var groups = 0;
            var files = 0;
            foreach (var node in all)
            {
                if (node.HasChildren)
                    groups++;
                else
                    files++;
            }

            _lblSummary.Text = $"Строк: {groups + files} | Групп: {groups} | Одиночных: {files}";
        }

        private static IEnumerable<OrdersTreePrototypeNode> Flatten(IEnumerable<OrdersTreePrototypeNode> roots)
        {
            foreach (var root in roots)
            {
                yield return root;

                foreach (var child in Flatten(root.Children))
                    yield return child;
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
}
