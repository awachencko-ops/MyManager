using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Svg;

namespace MyManager
{
    public partial class MainForm
    {
        private void InitializeStatusFilter()
        {
            lblFStatus.Click += LblFStatus_Click;
            picFStatusGlyph.Click += LblFStatus_Click;
            ApplyStatusFilterChevronIcon();
            UpdateStatusFilterCaption();
        }

        private void ApplyStatusFilterChevronIcon()
        {
            using var icon = CreateDropDownGlyphIcon(24);
            picFStatusGlyph.Image?.Dispose();
            picFStatusGlyph.Image = (Image)icon.Clone();
            lblFStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblFStatus.Padding = new Padding(0, 3, 0, 0);
        }

        private void InitializeOrderNoSearch()
        {
            lblFOrderNo.Click += LblFOrderNo_Click;
            picFOrderNoGlyph.Click += LblFOrderNo_Click;
            ApplyOrderNoSearchIcon();
            UpdateOrderNoSearchCaption();
        }

        private void InitializeUserFilter()
        {
            var insertIndex = flpFilters.Controls.IndexOf(cbUser);
            if (insertIndex >= 0)
                flpFilters.Controls.Remove(cbUser);

            cbUser.Visible = false;

            _userFilterGlyph = new PictureBox
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 0, 0, 0),
                Name = "picFUserGlyph",
                Size = new Size(24, 33),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _userFilterLabel = new Label
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 3, 0),
                Name = "lblFUser",
                Size = new Size(150, 33),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _userFilterGlyph.Click += LblFUser_Click;
            _userFilterLabel.Click += LblFUser_Click;

            flpFilters.Controls.Add(_userFilterGlyph);
            flpFilters.Controls.Add(_userFilterLabel);
            if (insertIndex >= 0)
            {
                flpFilters.Controls.SetChildIndex(_userFilterGlyph, insertIndex);
                flpFilters.Controls.SetChildIndex(_userFilterLabel, insertIndex + 1);
            }

            ApplyUserFilterChevronIcon();
            UpdateUserFilterCaption();
        }

        private void InitializeCreatedDateFilter()
        {
            var insertIndex = flpFilters.Controls.IndexOf(cbFCreated);
            if (insertIndex >= 0)
                flpFilters.Controls.Remove(cbFCreated);

            cbFCreated.Visible = false;

            _createdFilterGlyph = new PictureBox
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 0, 0, 0),
                Name = "picFCreatedGlyph",
                Size = new Size(24, 33),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _createdFilterLabel = new Label
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 3, 0),
                Name = "lblFCreated",
                Size = new Size(170, 33),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _createdFilterGlyph.Click += LblFCreated_Click;
            _createdFilterLabel.Click += LblFCreated_Click;

            flpFilters.Controls.Add(_createdFilterGlyph);
            flpFilters.Controls.Add(_createdFilterLabel);
            if (insertIndex >= 0)
            {
                flpFilters.Controls.SetChildIndex(_createdFilterGlyph, insertIndex);
                flpFilters.Controls.SetChildIndex(_createdFilterLabel, insertIndex + 1);
            }

            ApplyCreatedDateFilterChevronIcon();
            UpdateCreatedDateFilterCaption();
        }

        private void InitializeReceivedDateFilter()
        {
            var insertIndex = flpFilters.Controls.IndexOf(cbFReceived);
            if (insertIndex >= 0)
                flpFilters.Controls.Remove(cbFReceived);

            cbFReceived.Visible = false;

            _receivedFilterGlyph = new PictureBox
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 0, 0, 0),
                Name = "picFReceivedGlyph",
                Size = new Size(24, 33),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _receivedFilterLabel = new Label
            {
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 3, 0),
                Name = "lblFReceived",
                Size = new Size(170, 33),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _receivedFilterGlyph.Click += LblFReceived_Click;
            _receivedFilterLabel.Click += LblFReceived_Click;

            flpFilters.Controls.Add(_receivedFilterGlyph);
            flpFilters.Controls.Add(_receivedFilterLabel);
            if (insertIndex >= 0)
            {
                flpFilters.Controls.SetChildIndex(_receivedFilterGlyph, insertIndex);
                flpFilters.Controls.SetChildIndex(_receivedFilterLabel, insertIndex + 1);
            }

            ApplyReceivedDateFilterChevronIcon();
            UpdateReceivedDateFilterCaption();
        }

        private void ApplyOrderNoSearchIcon()
        {
            using var icon = CreateDropDownGlyphIcon(24);
            picFOrderNoGlyph.Image?.Dispose();
            picFOrderNoGlyph.Image = (Image)icon.Clone();
            lblFOrderNo.TextAlign = ContentAlignment.MiddleLeft;
            lblFOrderNo.Padding = new Padding(0, 3, 0, 0);
        }

        private void ApplyUserFilterChevronIcon()
        {
            if (_userFilterGlyph == null || _userFilterLabel == null)
                return;

            using var icon = CreateDropDownGlyphIcon(24);
            _userFilterGlyph.Image?.Dispose();
            _userFilterGlyph.Image = (Image)icon.Clone();
            _userFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            _userFilterLabel.Padding = new Padding(0, 3, 0, 0);
        }

        private void ApplyCreatedDateFilterChevronIcon()
        {
            if (_createdFilterGlyph == null || _createdFilterLabel == null)
                return;

            using var icon = CreateDropDownGlyphIcon(24);
            _createdFilterGlyph.Image?.Dispose();
            _createdFilterGlyph.Image = (Image)icon.Clone();
            _createdFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            _createdFilterLabel.Padding = new Padding(0, 3, 0, 0);
        }

        private void ApplyReceivedDateFilterChevronIcon()
        {
            if (_receivedFilterGlyph == null || _receivedFilterLabel == null)
                return;

            using var icon = CreateDropDownGlyphIcon(24);
            _receivedFilterGlyph.Image?.Dispose();
            _receivedFilterGlyph.Image = (Image)icon.Clone();
            _receivedFilterLabel.TextAlign = ContentAlignment.MiddleLeft;
            _receivedFilterLabel.Padding = new Padding(0, 3, 0, 0);
        }

        private static Bitmap CreateDropDownGlyphIcon(int iconSize)
        {
            var svgPath = ResolveDropDownGlyphSvgPath();
            if (!string.IsNullOrWhiteSpace(svgPath))
            {
                try
                {
                    var svg = SvgDocument.Open<SvgDocument>(svgPath);
                    svg.Width = iconSize;
                    svg.Height = iconSize;
                    var rendered = svg.Draw(iconSize, iconSize);
                    if (rendered != null)
                        return rendered;
                }
                catch
                {
                    // Если SVG недоступен/поврежден, используем внутреннюю отрисовку.
                }
            }

            return CreateFallbackDropDownGlyphIcon(iconSize);
        }

        private static string? ResolveDropDownGlyphSvgPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Icons", "arrow_drop_down.svg"),
                Path.Combine(AppContext.BaseDirectory, "arrow_drop_down.svg")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static Bitmap CreateFallbackDropDownGlyphIcon(int iconSize)
        {
            var bitmap = new Bitmap(iconSize, iconSize);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var centerX = iconSize / 2f;
            var centerY = (iconSize / 2f) - 0.8f;
            var halfWidth = iconSize * 0.24f;
            var vertical = iconSize * 0.17f;

            using var pen = new Pen(Color.FromArgb(31, 31, 31), 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawLines(
                pen,
                [
                    new PointF(centerX - halfWidth, centerY - vertical),
                    new PointF(centerX, centerY + vertical),
                    new PointF(centerX + halfWidth, centerY - vertical)
                ]);

            return bitmap;
        }

        private void PopulateQueueTree()
        {
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();

            foreach (var userName in _users)
            {
                var userNode = new TreeNode(userName);
                foreach (var statusName in QueueStatuses)
                    userNode.Nodes.Add(statusName);

                userNode.Expand();
                treeView1.Nodes.Add(userNode);
            }

            treeView1.EndUpdate();
        }

        // cbQueue всегда содержит статусы выбранного в дереве пользователя.
        private void SelectUser(TreeNode userNode, string? preferredStatus = null)
        {
            _currentUserName = userNode.Text;

            var targetStatus = string.IsNullOrWhiteSpace(preferredStatus)
                ? QueueStatuses[0]
                : preferredStatus;

            FillQueueCombo(targetStatus);
            treeView1.Invalidate();
        }

        private void TreeView1_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (_isSyncingQueueSelection || e.Node == null)
                return;

            var userNode = e.Node.Level == 0 ? e.Node : e.Node.Parent;
            if (userNode == null)
                return;

            var preferredStatus = e.Node.Level == 0
                ? GetSelectedQueueStatusName()
                : e.Node.Text;

            _isSyncingQueueSelection = true;
            SelectUser(userNode, preferredStatus);
            _isSyncingQueueSelection = false;
            HandleOrdersGridChanged();
        }

        private void CbQueue_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isSyncingQueueSelection || cbQueue.SelectedItem is not QueueStatusItem selectedItem)
                return;

            var selectedStatus = selectedItem.StatusName;
            var userNode = FindUserNode(_currentUserName);
            if (userNode == null && treeView1.SelectedNode != null)
                userNode = treeView1.SelectedNode.Level == 0 ? treeView1.SelectedNode : treeView1.SelectedNode.Parent;

            if (userNode == null)
                return;

            var statusNode = FindStatusNode(userNode, selectedStatus);
            if (statusNode == null)
                return;

            _isSyncingQueueSelection = true;
            userNode.Expand();
            treeView1.SelectedNode = statusNode;
            statusNode.EnsureVisible();
            _isSyncingQueueSelection = false;
            HandleOrdersGridChanged();
        }

        private TreeNode? FindUserNode(string userName)
        {
            foreach (TreeNode node in treeView1.Nodes)
            {
                if (string.Equals(node.Text, userName, StringComparison.Ordinal))
                    return node;
            }

            return null;
        }

        private static TreeNode? FindStatusNode(TreeNode userNode, string statusName)
        {
            foreach (TreeNode child in userNode.Nodes)
            {
                if (string.Equals(child.Text, statusName, StringComparison.Ordinal))
                    return child;
            }

            return null;
        }

        private void TreeView1_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null)
                return;

            var isRoot = e.Node.Level == 0;
            var isSelected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
            var isHovered = !isRoot && ReferenceEquals(e.Node, _hoveredQueueNode);
            var rowRect = new Rectangle(0, e.Bounds.Top, treeView1.ClientSize.Width, e.Bounds.Height);

            var backColor = isRoot ? QueueHeaderBackColor : QueuePanelBackColor;
            if (!isRoot)
            {
                if (isSelected)
                    backColor = QueueStatusSelectedBackColor;
                else if (isHovered)
                    backColor = QueueStatusHoverBackColor;
            }

            var textColor = isRoot
                ? QueueHeaderTextColor
                : isSelected
                    ? QueueStatusSelectedTextColor
                    : QueueTextColor;

            using var backBrush = new SolidBrush(backColor);
            e.Graphics.FillRectangle(backBrush, rowRect);

            if (isSelected && !isRoot)
            {
                var markerRect = new Rectangle(rowRect.Left, rowRect.Top, 3, rowRect.Height);
                using var markerBrush = new SolidBrush(QueueActiveMarkerColor);
                e.Graphics.FillRectangle(markerBrush, markerRect);
            }

            var textValue = isRoot ? e.Node.Text : FormatQueueLabel(e.Node.Text);
            var textLeft = e.Bounds.X + 8;
            if (isRoot)
            {
                var indicatorDiameter = 8;
                var indicatorRect = new Rectangle(
                    e.Bounds.X + 10,
                    e.Bounds.Y + Math.Max(0, (e.Bounds.Height - indicatorDiameter) / 2),
                    indicatorDiameter,
                    indicatorDiameter);

                using var indicatorBrush = new SolidBrush(IsQueueServerConnected()
                    ? QueueHeaderOnlineIndicatorColor
                    : QueueHeaderOfflineIndicatorColor);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(indicatorBrush, indicatorRect);
                e.Graphics.SmoothingMode = SmoothingMode.None;

                textLeft = indicatorRect.Right + 8;
            }

            using var textFont = new Font(
                isRoot || isSelected ? "Segoe UI Semibold" : "Segoe UI",
                isRoot ? 16f : 15f,
                FontStyle.Regular,
                GraphicsUnit.Pixel);

            var trailingInset = isRoot ? 14 : 86;
            var textRect = new Rectangle(
                textLeft,
                e.Bounds.Y,
                Math.Max(0, treeView1.ClientSize.Width - textLeft - trailingInset),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                textValue,
                textFont,
                textRect,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (!isRoot)
            {
                var countValue = GetQueueStatusCount(e.Node.Text);
                var countText = $"({countValue})";
                var countColor = ResolveQueueCounterColor(countValue, isSelected);
                using var countFont = new Font(
                    isSelected ? "Segoe UI Semibold" : "Segoe UI",
                    14f,
                    FontStyle.Regular,
                    GraphicsUnit.Pixel);
                var countRect = new Rectangle(0, e.Bounds.Y, treeView1.ClientSize.Width - 14, e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    countText,
                    countFont,
                    countRect,
                    countColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (!isRoot && (e.State & TreeNodeStates.Focused) == TreeNodeStates.Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, rowRect, textColor, backColor);
        }

        private void TreeView1_MouseMove(object? sender, MouseEventArgs e)
        {
            var hoveredNode = treeView1.GetNodeAt(e.Location);
            if (hoveredNode?.Level == 0)
                hoveredNode = null;

            if (ReferenceEquals(hoveredNode, _hoveredQueueNode))
                return;

            _hoveredQueueNode = hoveredNode;
            treeView1.Invalidate();
        }

        private void TreeView1_MouseLeave(object? sender, EventArgs e)
        {
            if (_hoveredQueueNode == null)
                return;

            _hoveredQueueNode = null;
            treeView1.Invalidate();
        }

        private static Color ResolveQueueCounterColor(int count, bool isSelected)
        {
            if (count == 0)
            {
                return isSelected
                    ? QueueCounterSelectedZeroTextColor
                    : QueueCounterZeroTextColor;
            }

            return isSelected
                ? QueueCounterSelectedTextColor
                : QueueCounterTextColor;
        }

        private bool IsQueueServerConnected()
        {
            if (toolConnection.IsDisposed)
                return false;

            var connectionText = toolConnection.Text ?? string.Empty;
            return connectionText.Contains("подключен", StringComparison.OrdinalIgnoreCase);
        }

    }
}
