using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
        private readonly Button _btnRefresh = new();
        private readonly Label _lblQuickFilter = new();
        private readonly TextBox _tbQuickFilter = new();
        private readonly Label _lblSummary = new();
        private readonly TreeListView _treeListView = new();
        private readonly ImageList _statusImageList = new();
        private readonly ContextMenuStrip _rowContextMenu = new();
        private readonly Dictionary<string, Image> _contextMenuIconCache = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<OrdersTreePrototypeNode> _rootNodes;
        private HashSet<string> _expandedNodeKeys = new(StringComparer.Ordinal);
        private string? _selectedNodeKey;
        private OLVColumn? _defaultSortColumn;
        private OLVColumn? _sourceStageColumn;
        private OLVColumn? _preparedStageColumn;
        private OLVColumn? _printStageColumn;
        private Rectangle _dragBoxFromMouseDown = Rectangle.Empty;
        private OrdersTreePrototypeNode? _dragSourceNode;
        private int _dragSourceStage = OrderStages.None;

        public event EventHandler? RefreshRequested;
        public event EventHandler<OrdersPrototypeStageCellClickEventArgs>? StageCellClick;
        public event EventHandler<OrdersPrototypeStageCellContextMenuEventArgs>? StageCellContextMenuRequested;
        public event EventHandler<OrdersPrototypeStageFileDropEventArgs>? StageFileDropRequested;

        private const string InternalDragSourceRowTagData = "ReplicaOlvSourceRowTag";
        private const string InternalDragSourceStageData = "ReplicaOlvSourceStage";
        private static readonly Color OrdersRowBaseBackColor = Color.FromArgb(255, 255, 255);
        private static readonly Color OrdersRowZebraBackColor = Color.FromArgb(252, 253, 254);
        private static readonly Color OrdersRowHoverBackColor = Color.FromArgb(248, 250, 252);
        private static readonly Color OrdersRowSelectedBackColor = Color.FromArgb(243, 247, 251);
        private static readonly Color OrdersGridLineColor = Color.FromArgb(231, 235, 240);
        private static readonly Color OrdersLinkTextColor = Color.FromArgb(95, 126, 168);
        private static readonly Color OrdersHeaderBackColor = Color.White;
        private static readonly Color OrdersHeaderTextColor = Color.Black;
        private static readonly Color OrdersActiveMarkerColor = Color.FromArgb(122, 167, 217);
        private static readonly Color GroupOrderRowBackColor = Color.FromArgb(255, 252, 244);
        private static readonly Color GroupOrderRowSelectedBackColor = Color.FromArgb(255, 246, 232);
        private static readonly Color GroupOrderItemRowBaseBackColor = Color.FromArgb(255, 255, 255);
        private static readonly Color GroupOrderItemRowZebraBackColor = Color.FromArgb(255, 253, 248);
        private static readonly Color GroupOrderItemRowSelectedBackColor = Color.FromArgb(255, 248, 238);

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

            _btnExpandAll.Text = "Развернуть";
            _btnExpandAll.Size = new Size(140, 32);
            _btnExpandAll.Location = new Point(8, 6);
            _btnExpandAll.Click += (_, _) => _treeListView.ExpandAll();

            _btnCollapseAll.Text = "Свернуть всё";
            _btnCollapseAll.Size = new Size(130, 32);
            _btnCollapseAll.Location = new Point(156, 6);
            _btnCollapseAll.Click += (_, _) => _treeListView.CollapseAll();

            _btnResetSort.Text = "Сбросить";
            _btnResetSort.Size = new Size(125, 32);
            _btnResetSort.Location = new Point(292, 6);
            _btnResetSort.Click += (_, _) => ApplyDefaultSort();

            _btnRefresh.Text = "Обновить";
            _btnRefresh.Size = new Size(125, 32);
            _btnRefresh.Location = new Point(423, 6);
            _btnRefresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

            _lblQuickFilter.Text = "Быстрый фильтр:";
            _lblQuickFilter.AutoSize = true;
            _lblQuickFilter.Location = new Point(556, 12);

            _tbQuickFilter.Location = new Point(674, 8);
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
            _topPanel.Controls.Add(_btnRefresh);
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
            _treeListView.AllowColumnReorder = false;
            _treeListView.MultiSelect = false;
            _treeListView.ShowGroups = false;
            _treeListView.View = View.Details;
            _treeListView.AllowDrop = true;
            _treeListView.OwnerDraw = true;
            _treeListView.ShowSortIndicators = true;
            _treeListView.UseAlternatingBackColors = true;
            _treeListView.BackColor = OrdersRowBaseBackColor;
            _treeListView.AlternateRowBackColor = OrdersRowZebraBackColor;
            _treeListView.RowHeight = OrdersWorkspaceGridStyle.RowHeight;
            _treeListView.CellPadding = new Rectangle(
                OrdersWorkspaceGridStyle.HorizontalPadding,
                0,
                OrdersWorkspaceGridStyle.HorizontalPadding,
                0);
            _treeListView.TintSortColumn = true;
            _treeListView.SelectedBackColor = OrdersRowSelectedBackColor;
            _treeListView.UnfocusedSelectedBackColor = OrdersRowSelectedBackColor;
            _treeListView.BorderStyle = BorderStyle.None;
            _treeListView.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _treeListView.HeaderStyle = ColumnHeaderStyle.Clickable;
            _treeListView.HeaderUsesThemes = false;
            _treeListView.HeaderWordWrap = false;
            _treeListView.HeaderFormatStyle = new HeaderFormatStyle
            {
                Normal = new HeaderStateStyle
                {
                    BackColor = OrdersHeaderBackColor,
                    ForeColor = OrdersHeaderTextColor,
                    FrameColor = OrdersGridLineColor
                },
                Hot = new HeaderStateStyle
                {
                    BackColor = OrdersHeaderBackColor,
                    ForeColor = OrdersHeaderTextColor,
                    FrameColor = OrdersGridLineColor
                },
                Pressed = new HeaderStateStyle
                {
                    BackColor = OrdersHeaderBackColor,
                    ForeColor = OrdersHeaderTextColor,
                    FrameColor = OrdersGridLineColor
                }
            };
            _treeListView.UseHotItem = true;
            _treeListView.HotItemStyle = new HotItemStyle
            {
                BackColor = OrdersRowHoverBackColor
            };
            _treeListView.CellToolTipGetter = BuildStageCellToolTip;
            _treeListView.CellOver += TreeListView_CellOver;
            _treeListView.EmptyListMsg = "Нет заказов для отображения в прототипе.";
            _treeListView.EmptyListMsgFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            _treeListView.RowFormatter = PrototypeRowFormatter;
            _treeListView.CanExpandGetter = rowObject => rowObject is OrdersTreePrototypeNode node && node.HasChildren;
            _treeListView.ChildrenGetter = rowObject =>
                rowObject is OrdersTreePrototypeNode node
                    ? node.Children
                    : Array.Empty<OrdersTreePrototypeNode>();
            _treeListView.MouseDown += TreeListView_MouseDown;
            _treeListView.ItemActivate += TreeListView_ItemActivate;
            _treeListView.CellClick += TreeListView_CellClick;
            _treeListView.KeyDown += TreeListView_KeyDown;
            _treeListView.MouseMove += TreeListView_MouseMove;
            _treeListView.MouseUp += TreeListView_MouseUp;
            _treeListView.DragEnter += TreeListView_DragEnter;
            _treeListView.DragOver += TreeListView_DragOver;
            _treeListView.DragDrop += TreeListView_DragDrop;
            _treeListView.MouseLeave += (_, _) => _treeListView.Cursor = Cursors.Default;
            ConfigureStatusImageList();

            var colStatus = BuildColumn(
                title: "Состояние",
                width: 170,
                aspectGetter: row => ((OrdersTreePrototypeNode)row).Status,
                textAlign: HorizontalAlignment.Left,
                fillsFreeSpace: false);
            colStatus.Renderer = new StatusCellRenderer(_statusImageList);
            var colOrderNumber = BuildColumn(
                title: "№ заказа",
                width: 160,
                aspectGetter: row => ((OrdersTreePrototypeNode)row).Title,
                textAlign: HorizontalAlignment.Left,
                fillsFreeSpace: false);
            var colSource = BuildColumn("Исходные", 220, row => ((OrdersTreePrototypeNode)row).Source);
            var colPrepared = BuildColumn(
                "Заголовок задания",
                300,
                row => ((OrdersTreePrototypeNode)row).Prepared,
                HorizontalAlignment.Left,
                fillsFreeSpace: true);
            var colPitStop = BuildColumn("Проверка файлов", 180, row => ((OrdersTreePrototypeNode)row).PitStop);
            var colImposing = BuildColumn("Спуск полос", 175, row => ((OrdersTreePrototypeNode)row).Imposing);
            var colPrint = BuildColumn("Готов к печати", 170, row => ((OrdersTreePrototypeNode)row).Print);
            var colReceived = BuildColumn("Заказ принят", 130, row => ((OrdersTreePrototypeNode)row).ReceivedSortTicks, HorizontalAlignment.Center);
            var colCreated = BuildColumn("В препрессе", 130, row => ((OrdersTreePrototypeNode)row).CreatedSortTicks, HorizontalAlignment.Center);
            colReceived.AspectToStringConverter = FormatDateFromSortTicks;
            colCreated.AspectToStringConverter = FormatDateFromSortTicks;

            var allColumns = new[]
            {
                colStatus,
                colOrderNumber,
                colSource,
                colPrepared,
                colPitStop,
                colImposing,
                colPrint,
                colReceived,
                colCreated
            };

            var visibleColumns = new[]
            {
                colStatus,
                colOrderNumber,
                colPrepared,
                colPitStop,
                colImposing,
                colPrint,
                colReceived,
                colCreated
            };

            _treeListView.AllColumns.Clear();
            _treeListView.Columns.Clear();
            _treeListView.AllColumns.AddRange(allColumns);
            _treeListView.Columns.AddRange(visibleColumns);
            _sourceStageColumn = colSource;
            _preparedStageColumn = colPrepared;
            _printStageColumn = colPrint;
            _defaultSortColumn = colCreated;
            _treeListView.TreeColumnRenderer = new TreeListView.TreeRenderer
            {
                Column = colOrderNumber,
                IsShowGlyphs = true,
                IsShowLines = false,
                UseTriangles = true
            };
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
            SaveInteractionState();
            var filteredRoots = BuildFilteredRoots(_tbQuickFilter.Text);
            PopulateTree(filteredRoots);
            ApplyDefaultSort();
            RestoreInteractionState(filteredRoots);
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

        private void TreeListView_CellClick(object? sender, CellClickEventArgs e)
        {
            if (e.Model is not OrdersTreePrototypeNode node)
                return;

            var stage = ResolveStageByColumnIndex(e.ColumnIndex);
            if (!OrderStages.IsFileStage(stage))
                return;

            StageCellClick?.Invoke(
                this,
                new OrdersPrototypeStageCellClickEventArgs(node, stage, e.ColumnIndex));
        }

        private void TreeListView_MouseDown(object? sender, MouseEventArgs e)
        {
            _dragBoxFromMouseDown = Rectangle.Empty;
            _dragSourceNode = null;
            _dragSourceStage = OrderStages.None;

            if (e.Button != MouseButtons.Left)
                return;

            if (!TryResolveStageCellFromPoint(e.Location, out var node, out _, out var stage))
                return;

            var sourcePath = GetNodeStagePath(node, stage);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return;

            _dragSourceNode = node;
            _dragSourceStage = stage;

            var dragSize = SystemInformation.DragSize;
            _dragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
        }

        private void TreeListView_MouseMove(object? sender, MouseEventArgs e)
        {
            UpdateCursorByHotCell(e.Location);

            if ((e.Button & MouseButtons.Left) != MouseButtons.Left)
                return;

            if (_dragSourceNode == null || _dragSourceStage == OrderStages.None)
                return;

            if (_dragBoxFromMouseDown != Rectangle.Empty && _dragBoxFromMouseDown.Contains(e.Location))
                return;

            var sourcePath = GetNodeStagePath(_dragSourceNode, _dragSourceStage);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return;

            var dragData = new DataObject();
            dragData.SetData(DataFormats.FileDrop, new[] { sourcePath });
            dragData.SetData(InternalDragSourceRowTagData, _dragSourceNode.RowTag ?? string.Empty);
            dragData.SetData(InternalDragSourceStageData, _dragSourceStage);

            _dragBoxFromMouseDown = Rectangle.Empty;
            _treeListView.DoDragDrop(dragData, DragDropEffects.Copy);
        }

        private void TreeListView_CellOver(object? sender, CellOverEventArgs e)
        {
            if (e.Model is not OrdersTreePrototypeNode node)
            {
                _treeListView.Cursor = Cursors.Default;
                return;
            }

            var stage = ResolveStageByColumnIndex(e.ColumnIndex);
            var canInteract = OrderStages.IsFileStage(stage) && !node.HasChildren;
            _treeListView.Cursor = canInteract ? Cursors.Hand : Cursors.Default;
        }

        private void TreeListView_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = TryGetFirstDroppedFilePath(e.Data, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void TreeListView_DragOver(object? sender, DragEventArgs e)
        {
            if (!TryGetFirstDroppedFilePath(e.Data, out var sourceFile))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (!TryResolveStageCellFromDragPoint(new Point(e.X, e.Y), out var node, out _, out var stage))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (node.HasChildren)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (TryGetInternalDragSource(e.Data, out var sourceRowTag, out var sourceStage)
                && sourceStage == stage
                && string.Equals(sourceRowTag, node.RowTag, StringComparison.Ordinal))
            {
                var targetPath = GetNodeStagePath(node, stage);
                e.Effect = PathsEqual(targetPath, sourceFile)
                    ? DragDropEffects.None
                    : DragDropEffects.Copy;
                return;
            }

            e.Effect = DragDropEffects.Copy;
        }

        private void TreeListView_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetFirstDroppedFilePath(e.Data, out var sourceFile))
                return;

            if (!TryResolveStageCellFromDragPoint(new Point(e.X, e.Y), out var node, out _, out var stage))
                return;

            if (node.HasChildren)
                return;

            TryGetInternalDragSource(e.Data, out var sourceRowTag, out var sourceStage);

            StageFileDropRequested?.Invoke(
                this,
                new OrdersPrototypeStageFileDropEventArgs(
                    node: node,
                    stage: stage,
                    sourceFilePath: sourceFile,
                    sourceRowTag: sourceRowTag,
                    sourceStage: sourceStage));
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

        private void SaveInteractionState()
        {
            if (_treeListView.SelectedObject is OrdersTreePrototypeNode selectedNode)
                _selectedNodeKey = BuildNodeIdentity(selectedNode);
            else
                _selectedNodeKey = null;

            if (_treeListView.Roots is not IEnumerable roots)
                return;

            var expanded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Flatten(roots.OfType<OrdersTreePrototypeNode>()))
            {
                if (!node.HasChildren || !_treeListView.IsExpanded(node))
                    continue;

                var key = BuildNodeIdentity(node);
                if (!string.IsNullOrWhiteSpace(key))
                    expanded.Add(key);
            }

            _expandedNodeKeys = expanded;
        }

        private void RestoreInteractionState(IReadOnlyList<OrdersTreePrototypeNode> roots)
        {
            foreach (var node in Flatten(roots))
            {
                if (!node.HasChildren)
                    continue;

                var key = BuildNodeIdentity(node);
                if (string.IsNullOrWhiteSpace(key) || !_expandedNodeKeys.Contains(key))
                    continue;

                _treeListView.Expand(node);
            }

            if (string.IsNullOrWhiteSpace(_selectedNodeKey))
                return;

            var selectedNode = Flatten(roots)
                .FirstOrDefault(node => string.Equals(BuildNodeIdentity(node), _selectedNodeKey, StringComparison.Ordinal));
            if (selectedNode == null)
                return;

            _treeListView.SelectedObject = selectedNode;
        }

        private static string BuildNodeIdentity(OrdersTreePrototypeNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.RowTag))
                return node.RowTag;

            if (!string.IsNullOrWhiteSpace(node.OrderInternalId) || !string.IsNullOrWhiteSpace(node.ItemId))
                return $"{node.OrderInternalId}|{node.ItemId}";

            return node.Title ?? string.Empty;
        }

        private static int ResolveStageByColumnIndex(int columnIndex)
        {
            return columnIndex switch
            {
                2 => OrderStages.Prepared,
                5 => OrderStages.Print,
                _ => OrderStages.None
            };
        }

        private bool TryResolveStageCellFromPoint(
            Point point,
            out OrdersTreePrototypeNode node,
            out int columnIndex,
            out int stage)
        {
            node = null!;
            columnIndex = -1;
            stage = OrderStages.None;

            var hit = _treeListView.OlvHitTest(point.X, point.Y);
            if (hit?.RowObject is not OrdersTreePrototypeNode hitNode)
                return false;

            columnIndex = hit.ColumnIndex;
            stage = ResolveStageByColumnIndex(columnIndex);
            if (!OrderStages.IsFileStage(stage))
                return false;

            node = hitNode;
            return true;
        }

        private bool TryResolveStageCellFromDragPoint(
            Point screenPoint,
            out OrdersTreePrototypeNode node,
            out int columnIndex,
            out int stage)
        {
            var clientPoint = _treeListView.PointToClient(screenPoint);
            return TryResolveStageCellFromPoint(clientPoint, out node, out columnIndex, out stage);
        }

        private static string GetNodeStagePath(OrdersTreePrototypeNode node, int stage)
        {
            if (node == null)
                return string.Empty;

            return stage switch
            {
                OrderStages.Source => node.SourcePath ?? string.Empty,
                OrderStages.Prepared => node.PreparedPath ?? string.Empty,
                OrderStages.Print => node.PrintPath ?? string.Empty,
                _ => string.Empty
            };
        }

        private static bool TryGetFirstDroppedFilePath(IDataObject? data, out string sourceFilePath)
        {
            sourceFilePath = string.Empty;
            if (data?.GetDataPresent(DataFormats.FileDrop) != true)
                return false;

            if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
                return false;

            var first = files[0];
            if (string.IsNullOrWhiteSpace(first))
                return false;

            sourceFilePath = first.Trim().Trim('"');
            return !string.IsNullOrWhiteSpace(sourceFilePath);
        }

        private static bool TryGetInternalDragSource(IDataObject? data, out string sourceRowTag, out int sourceStage)
        {
            sourceRowTag = string.Empty;
            sourceStage = OrderStages.None;

            if (data == null)
                return false;

            if (data.GetData(InternalDragSourceRowTagData) is not string rowTag)
                return false;

            if (data.GetData(InternalDragSourceStageData) is not int stage)
                return false;

            sourceRowTag = rowTag ?? string.Empty;
            sourceStage = stage;
            return !string.IsNullOrWhiteSpace(sourceRowTag) && OrderStages.IsFileStage(sourceStage);
        }

        private static bool PathsEqual(string? leftPath, string? rightPath)
        {
            if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
                return false;

            static string Normalize(string path)
            {
                try
                {
                    return Path.GetFullPath(path)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    return path.Trim();
                }
            }

            return string.Equals(
                Normalize(leftPath),
                Normalize(rightPath),
                StringComparison.OrdinalIgnoreCase);
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

        private void TreeListView_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            var hit = _treeListView.OlvHitTest(e.X, e.Y);
            if (hit?.RowObject is not OrdersTreePrototypeNode node)
                return;

            _treeListView.SelectedObject = node;

            var stage = ResolveStageByColumnIndex(hit.ColumnIndex);
            if (OrderStages.IsFileStage(stage))
            {
                var screenLocation = _treeListView.PointToScreen(e.Location);
                StageCellContextMenuRequested?.Invoke(
                    this,
                    new OrdersPrototypeStageCellContextMenuEventArgs(node, stage, hit.ColumnIndex, screenLocation));
                return;
            }

            ShowRowContextMenu(node, e.Location);
        }

        private void ShowRowContextMenu(OrdersTreePrototypeNode node, Point location)
        {
            _rowContextMenu.Items.Clear();

            if (node.HasChildren)
            {
                var isExpanded = _treeListView.IsExpanded(node);
                var caption = isExpanded ? "Свернуть группу" : "Развернуть группу";
                AddRowContextMenuItem(caption, () =>
                {
                    if (isExpanded)
                        _treeListView.Collapse(node);
                    else
                        _treeListView.Expand(node);
                }, iconFolder: "folder open", iconHint: "folder");
                _rowContextMenu.Items.Add(new ToolStripSeparator());
            }

            var bestStagePath = GetFirstExistingStageFilePath(node);
            if (!string.IsNullOrWhiteSpace(bestStagePath))
            {
                AddRowContextMenuItem(
                    "Открыть файл",
                    () => OpenWithShell(bestStagePath),
                    iconFolder: "file export",
                    iconHint: "file_export");
            }

            var bestStageFolder = GetFirstExistingStageFolderPath(node);
            if (!string.IsNullOrWhiteSpace(bestStageFolder))
            {
                AddRowContextMenuItem(
                    "Открыть папку файла",
                    () => OpenWithShell(bestStageFolder),
                    iconFolder: "folder open",
                    iconHint: "folder_open");
            }

            if (!string.IsNullOrWhiteSpace(bestStagePath) || !string.IsNullOrWhiteSpace(bestStageFolder))
                _rowContextMenu.Items.Add(new ToolStripSeparator());

            if (!string.IsNullOrWhiteSpace(node.OrderNumber))
                AddRowContextMenuItem(
                    "Копировать номер заказа",
                    () => TryCopyToClipboard(node.OrderNumber),
                    iconFolder: "files",
                    iconHint: "files");

            if (!node.HasChildren && !node.IsContainer && !string.IsNullOrWhiteSpace(node.Title))
                AddRowContextMenuItem(
                    "Копировать имя файла",
                    () => TryCopyToClipboard(node.Title),
                    iconFolder: "file export",
                    iconHint: "file_export");

            if (!string.IsNullOrWhiteSpace(node.Status))
                AddRowContextMenuItem(
                    "Копировать статус",
                    () => TryCopyToClipboard(node.Status),
                    iconFolder: "check",
                    iconHint: "check");

            if (!string.IsNullOrWhiteSpace(node.SourcePath))
                AddRowContextMenuItem(
                    "Копировать путь приема",
                    () => TryCopyToClipboard(node.SourcePath),
                    iconFolder: "move to inbox",
                    iconHint: "move_to_inbox");

            if (!string.IsNullOrWhiteSpace(node.PreparedPath))
                AddRowContextMenuItem(
                    "Копировать путь подготовки",
                    () => TryCopyToClipboard(node.PreparedPath),
                    iconFolder: "move to inbox",
                    iconHint: "move_to_inbox");

            if (!string.IsNullOrWhiteSpace(node.PrintPath))
                AddRowContextMenuItem(
                    "Копировать путь печати",
                    () => TryCopyToClipboard(node.PrintPath),
                    iconFolder: "move to inbox",
                    iconHint: "move_to_inbox");

            if (_rowContextMenu.Items.Count > 0
                && _rowContextMenu.Items[_rowContextMenu.Items.Count - 1] is ToolStripSeparator)
            {
                _rowContextMenu.Items.RemoveAt(_rowContextMenu.Items.Count - 1);
            }

            if (_rowContextMenu.Items.Count == 0)
                return;

            _rowContextMenu.Show(_treeListView, location);
        }

        private void AddRowContextMenuItem(
            string text,
            Action onClick,
            string? iconFolder = null,
            string? iconHint = null)
        {
            var item = new ToolStripMenuItem(text);
            if (!string.IsNullOrWhiteSpace(iconFolder))
            {
                var icon = TryGetRowContextMenuIcon(iconFolder, iconHint ?? string.Empty);
                if (icon != null)
                    item.Image = icon;
            }
            item.Click += (_, _) => onClick();
            _rowContextMenu.Items.Add(item);
        }

        private static string GetFirstExistingStageFilePath(OrdersTreePrototypeNode node)
        {
            if (node == null)
                return string.Empty;

            foreach (var stage in new[] { OrderStages.Source, OrderStages.Prepared, OrderStages.Print })
            {
                var path = GetNodeStagePath(node, stage);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        private static string GetFirstExistingStageFolderPath(OrdersTreePrototypeNode node)
        {
            if (node == null)
                return string.Empty;

            foreach (var stage in new[] { OrderStages.Source, OrderStages.Prepared, OrderStages.Print })
            {
                var path = GetNodeStagePath(node, stage);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        var existingFolder = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(existingFolder))
                            return existingFolder;
                    }

                    var fallbackFolder = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(fallbackFolder) && Directory.Exists(fallbackFolder))
                        return fallbackFolder;
                }
                catch
                {
                    // Ignore malformed file paths from source data.
                }
            }

            return string.Empty;
        }

        private static void OpenWithShell(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Keep prototype stable if shell open fails on missing path/association.
            }
        }

        private Image? TryGetRowContextMenuIcon(string iconFolder, string iconHint)
        {
            if (string.IsNullOrWhiteSpace(iconFolder))
                return null;

            var cacheKey = $"{iconFolder}|{iconHint}";
            if (_contextMenuIconCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var icon = TryLoadStatusIcon(iconFolder, iconHint);
            if (icon == null)
                return null;

            _contextMenuIconCache[cacheKey] = icon;
            return icon;
        }

        private void TryCopyToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                // Clipboard can be temporarily unavailable; keep prototype stable.
            }
        }

        private void ConfigureStatusImageList()
        {
            _statusImageList.ColorDepth = ColorDepth.Depth32Bit;
            _statusImageList.ImageSize = new Size(16, 16);
            _statusImageList.Images.Clear();

            AddStatusImage("status-group", iconFolder: "files", fileHint: "files", fallbackColor: Color.FromArgb(78, 122, 196));
            AddStatusImage("status-completed", iconFolder: "check", fileHint: "check", fallbackColor: Color.FromArgb(52, 168, 83));
            AddStatusImage("status-processing", iconFolder: "upload", fileHint: "upload", fallbackColor: Color.FromArgb(245, 166, 35));
            AddStatusImage("status-error", iconFolder: "error", fileHint: "error", fallbackColor: Color.FromArgb(219, 68, 55));
            AddStatusImage("status-waiting", iconFolder: "file export", fileHint: "file_export", fallbackColor: Color.FromArgb(130, 141, 156));
            AddStatusImage("status-building", iconFolder: "cards", fileHint: "cards", fallbackColor: Color.FromArgb(142, 110, 216));
            AddStatusImage("status-cancelled", iconFolder: "cancel", fileHint: "cancel", fallbackColor: Color.FromArgb(145, 145, 145));
            AddStatusImage("status-archived", iconFolder: "archive", fileHint: "archive", fallbackColor: Color.FromArgb(120, 140, 160));
            AddStatusImage("status-processed", iconFolder: "file export", fileHint: "file_export", fallbackColor: Color.FromArgb(110, 160, 210));
        }

        private void AddStatusImage(string key, string iconFolder, string fileHint, Color fallbackColor)
        {
            var icon = TryLoadStatusIcon(iconFolder, fileHint);
            if (icon != null)
            {
                _statusImageList.Images.Add(key, icon);
                return;
            }

            _statusImageList.Images.Add(key, CreateStatusDot(fallbackColor));
        }

        private static Bitmap? TryLoadStatusIcon(string iconFolder, string fileHint)
        {
            var icon = OrdersWorkspaceIconCatalog.LoadIcon(iconFolder, fileHint, size: 16);
            return icon != null ? new Bitmap(icon) : null;
        }

        private string BuildStageCellToolTip(OLVColumn column, object modelObject)
        {
            if (column == null || modelObject is not OrdersTreePrototypeNode node)
                return string.Empty;

            var stage = ResolveStageByColumn(column);
            if (!OrderStages.IsFileStage(stage))
                return string.Empty;

            if (node.HasChildren)
                return "Для group-order файл назначается только в дочерних строках item.";

            var stagePath = GetNodeStagePath(node, stage);
            if (string.IsNullOrWhiteSpace(stagePath))
                return "Файл не назначен. Кликните по ячейке для добавления.";

            if (File.Exists(stagePath))
                return stagePath;

            return $"Файл не найден: {stagePath}";
        }

        private int ResolveStageByColumn(OLVColumn? column)
        {
            if (column == null)
                return OrderStages.None;

            if (ReferenceEquals(column, _sourceStageColumn))
                return OrderStages.Source;
            if (ReferenceEquals(column, _preparedStageColumn))
                return OrderStages.Prepared;
            if (ReferenceEquals(column, _printStageColumn))
                return OrderStages.Print;

            return OrderStages.None;
        }

        private void UpdateCursorByHotCell(Point location)
        {
            if (!TryResolveStageCellFromPoint(location, out var node, out _, out var stage))
            {
                _treeListView.Cursor = Cursors.Default;
                return;
            }

            var canInteract = OrderStages.IsFileStage(stage) && !node.HasChildren;
            _treeListView.Cursor = canInteract ? Cursors.Hand : Cursors.Default;
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

            var normalized = WorkflowStatusNames.Normalize(node.Status);
            if (string.IsNullOrWhiteSpace(normalized))
                return "status-waiting";

            if (string.Equals(normalized, WorkflowStatusNames.Error, StringComparison.Ordinal))
                return "status-error";
            if (string.Equals(normalized, WorkflowStatusNames.Processing, StringComparison.Ordinal))
                return "status-processing";
            if (string.Equals(normalized, WorkflowStatusNames.Building, StringComparison.Ordinal))
                return "status-building";
            if (string.Equals(normalized, WorkflowStatusNames.Cancelled, StringComparison.Ordinal))
                return "status-cancelled";
            if (string.Equals(normalized, WorkflowStatusNames.Archived, StringComparison.Ordinal))
                return "status-archived";
            if (string.Equals(normalized, WorkflowStatusNames.Processed, StringComparison.Ordinal))
                return "status-processed";
            if (string.Equals(normalized, WorkflowStatusNames.Completed, StringComparison.Ordinal)
                || string.Equals(normalized, WorkflowStatusNames.Printed, StringComparison.Ordinal))
            {
                return "status-completed";
            }

            return "status-waiting";
        }

        private static Color ResolveStatusIconBackColor(OrdersTreePrototypeNode node)
        {
            if (node == null)
                return Color.White;

            if (node.HasChildren)
                return Color.FromArgb(247, 200, 119);

            var normalized = WorkflowStatusNames.Normalize(node.Status);
            if (string.IsNullOrWhiteSpace(normalized))
                return Color.White;

            if (string.Equals(normalized, WorkflowStatusNames.Completed, StringComparison.Ordinal)
                || string.Equals(normalized, WorkflowStatusNames.Printed, StringComparison.Ordinal))
            {
                return Color.FromArgb(198, 234, 198);
            }

            if (string.Equals(normalized, WorkflowStatusNames.Processed, StringComparison.Ordinal))
                return Color.FromArgb(255, 232, 205);

            if (string.Equals(normalized, WorkflowStatusNames.Processing, StringComparison.Ordinal))
                return Color.FromArgb(255, 248, 205);

            if (string.Equals(normalized, WorkflowStatusNames.Error, StringComparison.Ordinal))
                return Color.FromArgb(255, 204, 204);

            return Color.White;
        }

        private static Image? ResolveStatusImage(ImageList imageList, OrdersTreePrototypeNode node)
        {
            if (imageList == null)
                return null;

            var key = ResolveStatusIconKey(node);
            return imageList.Images.ContainsKey(key)
                ? imageList.Images[key]
                : null;
        }

        private static void DrawStatusCellCore(
            Graphics g,
            Rectangle r,
            OrdersTreePrototypeNode node,
            bool isSelected,
            Color rowBackColor,
            Font? font,
            ImageList imageList)
        {
            var contentBounds = Rectangle.Inflate(r, -1, -1);
            if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
                return;

            using (var backBrush = new SolidBrush(rowBackColor))
            {
                g.FillRectangle(backBrush, contentBounds);
            }

            const int markerWidth = 3;
            var markerRect = new Rectangle(contentBounds.Left, contentBounds.Top, markerWidth, contentBounds.Height);
            if (isSelected)
            {
                using var markerBrush = new SolidBrush(OrdersActiveMarkerColor);
                g.FillRectangle(markerBrush, markerRect);
            }

            var iconBackWidth = Math.Min(40, Math.Max(28, contentBounds.Width / 4));
            var iconBackRect = new Rectangle(
                markerRect.Right,
                contentBounds.Top,
                iconBackWidth,
                contentBounds.Height);
            using (var iconBackBrush = new SolidBrush(ResolveStatusIconBackColor(node)))
            {
                g.FillRectangle(iconBackBrush, iconBackRect);
            }

            var icon = ResolveStatusImage(imageList, node);
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var iconSize = Math.Max(12, Math.Min(18, iconBackRect.Height - 8));
                var iconRect = new Rectangle(
                    iconBackRect.Left + (iconBackRect.Width - iconSize) / 2,
                    iconBackRect.Top + (iconBackRect.Height - iconSize) / 2,
                    iconSize,
                    iconSize);
                g.DrawImage(icon, iconRect);
            }

            var textBounds = new Rectangle(
                iconBackRect.Right + 8,
                contentBounds.Top,
                Math.Max(0, contentBounds.Right - (iconBackRect.Right + 10)),
                contentBounds.Height);
            TextRenderer.DrawText(
                g,
                node.Status ?? string.Empty,
                font ?? SystemFonts.MessageBoxFont,
                textBounds,
                Color.Black,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private sealed class StatusCellRenderer : BaseRenderer
        {
            private readonly ImageList _imageList;

            public StatusCellRenderer(ImageList imageList)
            {
                _imageList = imageList ?? throw new ArgumentNullException(nameof(imageList));
            }

            public override bool RenderSubItem(
                DrawListViewSubItemEventArgs e,
                Graphics g,
                Rectangle r,
                object rowObject)
            {
                if (rowObject is not OrdersTreePrototypeNode node)
                    return base.RenderSubItem(e, g, r, rowObject);

                var rowBackColor = e.SubItem?.BackColor ?? e.Item?.BackColor ?? OrdersRowBaseBackColor;
                var font = e.SubItem?.Font ?? e.Item?.Font;
                var isSelected = e.Item?.Selected ?? false;
                DrawStatusCellCore(g, r, node, isSelected, rowBackColor, font, _imageList);
                return true;
            }

            public override bool RenderItem(
                DrawListViewItemEventArgs e,
                Graphics g,
                Rectangle r,
                object rowObject)
            {
                if (rowObject is not OrdersTreePrototypeNode node)
                    return base.RenderItem(e, g, r, rowObject);

                var rowBackColor = e.Item?.BackColor ?? OrdersRowBaseBackColor;
                var font = e.Item?.Font;
                var isSelected = e.Item?.Selected ?? false;
                DrawStatusCellCore(g, r, node, isSelected, rowBackColor, font, _imageList);
                return true;
            }
        }

        private static string FormatDateFromSortTicks(object value)
        {
            var ticks = 0L;
            try
            {
                ticks = Convert.ToInt64(value);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            if (ticks <= 0)
                return string.Empty;

            return new DateTime(ticks).ToString("dd.MM.yyyy");
        }

        private void PrototypeRowFormatter(OLVListItem item)
        {
            if (item.RowObject is not OrdersTreePrototypeNode node)
                return;

            item.UseItemStyleForSubItems = false;
            item.ForeColor = Color.Black;

            if (node.HasChildren)
            {
                item.BackColor = item.Selected ? GroupOrderRowSelectedBackColor : GroupOrderRowBackColor;
                PaintStatusSubItem(item, node);
                PaintStageSubItems(item, node, isGroupContainer: true);
                return;
            }

            if (!node.IsContainer)
            {
                var baseBack = item.Index % 2 == 0 ? GroupOrderItemRowBaseBackColor : GroupOrderItemRowZebraBackColor;
                item.BackColor = item.Selected ? GroupOrderItemRowSelectedBackColor : baseBack;
                PaintStatusSubItem(item, node);
                PaintStageSubItems(item, node, isGroupContainer: false);
                return;
            }

            item.BackColor = item.Selected ? OrdersRowSelectedBackColor : (item.Index % 2 == 0 ? OrdersRowBaseBackColor : OrdersRowZebraBackColor);
            PaintStatusSubItem(item, node);
            PaintStageSubItems(item, node, isGroupContainer: false);
        }

        private static void PaintStatusSubItem(OLVListItem item, OrdersTreePrototypeNode node)
        {
            if (item.SubItems.Count <= 0)
                return;

            var subItem = item.SubItems[0];
            subItem.ForeColor = Color.Black;
            subItem.BackColor = item.BackColor;
        }

        private static void PaintStageSubItems(OLVListItem item, OrdersTreePrototypeNode node, bool isGroupContainer)
        {
            PaintStageSubItem(item, node, stage: OrderStages.Prepared, subItemIndex: 2, isGroupContainer);
            PaintStageSubItem(item, node, stage: OrderStages.Print, subItemIndex: 5, isGroupContainer);
        }

        private static void PaintStageSubItem(
            OLVListItem item,
            OrdersTreePrototypeNode node,
            int stage,
            int subItemIndex,
            bool isGroupContainer)
        {
            if (item.SubItems.Count <= subItemIndex)
                return;

            var subItem = item.SubItems[subItemIndex];
            subItem.BackColor = item.BackColor;
            subItem.ForeColor = Color.Black;

            if (isGroupContainer)
            {
                subItem.ForeColor = Color.FromArgb(128, 128, 128);
                return;
            }

            var stagePath = stage switch
            {
                OrderStages.Source => node.SourcePath,
                OrderStages.Prepared => node.PreparedPath,
                OrderStages.Print => node.PrintPath,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(stagePath))
                return;

            if (File.Exists(stagePath))
            {
                subItem.ForeColor = OrdersLinkTextColor;
                return;
            }

            subItem.ForeColor = Color.Firebrick;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var image in _contextMenuIconCache.Values)
                    image.Dispose();
                _contextMenuIconCache.Clear();
                _rowContextMenu.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
