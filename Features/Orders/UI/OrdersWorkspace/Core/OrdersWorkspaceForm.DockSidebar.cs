using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private enum DockWorkspaceGroup
        {
            Orders,
            Literature,
            Utilities
        }

        private readonly ToolTip _dockToolTip = new();
        private readonly Dictionary<DockWorkspaceGroup, Panel> _dockPanelsByWorkspace = [];
        private readonly Dictionary<DockWorkspaceGroup, PictureBox> _dockIconHostsByWorkspace = [];
        private readonly Dictionary<DockWorkspaceGroup, Image> _dockInactiveIconsByWorkspace = [];
        private readonly Dictionary<DockWorkspaceGroup, Image> _dockHoverIconsByWorkspace = [];
        private readonly Dictionary<DockWorkspaceGroup, Image> _dockActiveIconsByWorkspace = [];
        private readonly Dictionary<DockWorkspaceGroup, string> _dockWorkspaceTitles = new()
        {
            [DockWorkspaceGroup.Orders] = "\u0417\u0430\u043A\u0430\u0437\u044B",
            [DockWorkspaceGroup.Literature] = "\u041B\u0438\u0442\u0435\u0440\u0430\u0442\u0443\u0440\u0430",
            [DockWorkspaceGroup.Utilities] = "\u0423\u0442\u0438\u043B\u0438\u0442\u044B"
        };

        private DockWorkspaceGroup _activeDockWorkspace = DockWorkspaceGroup.Orders;
        private DockWorkspaceGroup? _hoveredDockWorkspace;
        private Panel? _workspaceStubPanel;
        private Label? _workspaceStubLabel;

        private static readonly Color DockSidebarBackColor = Color.FromArgb(238, 238, 242); // #EEEEF2
        private static readonly Color DockSidebarDividerColor = Color.FromArgb(224, 227, 230); // #E0E3E6
        private static readonly Color DockButtonBackColor = Color.FromArgb(238, 238, 242); // #EEEEF2
        private static readonly Color DockButtonHoverBackColor = Color.FromArgb(230, 231, 233); // #E6E7E9
        private static readonly Color DockButtonActiveBackColor = Color.FromArgb(224, 227, 230); // #E0E3E6
        private static readonly Color DockButtonActiveMarkerColor = Color.FromArgb(101, 101, 101); // #656565
        private static readonly Color DockLockedButtonBackColor = Color.FromArgb(238, 238, 242); // #EEEEF2
        private static readonly Color DockButtonIconColor = Color.FromArgb(113, 113, 113); // #717171
        private static readonly Color DockButtonHoverIconColor = Color.FromArgb(90, 90, 90); // #5A5A5A
        private static readonly Color DockButtonActiveIconColor = Color.FromArgb(30, 30, 30); // #1E1E1E

        private void InitializeDockSidebar()
        {
            pnlSidebar.BackColor = DockSidebarBackColor;
            pnlSidebar.Paint -= PnlSidebar_Paint;

            ConfigureLockedAppDockButton();
            ConfigureWorkspaceDockButton(
                DockWorkspaceGroup.Orders,
                pnl_Orders,
                pictureBox1,
                "action",
                "view_cozy");

            ConfigureWorkspaceDockButton(
                DockWorkspaceGroup.Literature,
                pnlDockLiterature,
                pictureBox4,
                "action",
                "description");
            ConfigureWorkspaceDockButton(
                DockWorkspaceGroup.Utilities,
                pnlDockUtilities,
                pictureBox3,
                "action",
                "settings");

            SetDockWorkspace(DockWorkspaceGroup.Orders);
        }

        private void ConfigureLockedAppDockButton()
        {
            pnl_Icon.BackColor = DockLockedButtonBackColor;
            pnl_Icon.Cursor = Cursors.Default;
            pnl_Icon.Tag = null;

            pictureBox2.BackColor = Color.Transparent;
            pictureBox2.Dock = DockStyle.Fill;
            pictureBox2.SizeMode = PictureBoxSizeMode.CenterImage;
            pictureBox2.TabStop = false;
            pictureBox2.Cursor = Cursors.Default;
            pictureBox2.Image?.Dispose();
            pictureBox2.Image = CreateApplicationDockIcon(30);

            var appTitle = string.IsNullOrWhiteSpace(Application.ProductName) ? "Replica" : Application.ProductName;
            _dockToolTip.SetToolTip(pnl_Icon, appTitle);
            _dockToolTip.SetToolTip(pictureBox2, appTitle);
        }

        private void ConfigureWorkspaceDockButton(
            DockWorkspaceGroup workspace,
            Panel buttonPanel,
            PictureBox iconHost,
            string iconFolder,
            string iconHint)
        {
            buttonPanel.BackColor = DockButtonBackColor;
            buttonPanel.Tag = workspace;
            buttonPanel.Cursor = Cursors.Hand;

            iconHost.BackColor = Color.Transparent;
            iconHost.Dock = DockStyle.Fill;
            iconHost.SizeMode = PictureBoxSizeMode.CenterImage;
            iconHost.TabStop = false;
            iconHost.Cursor = Cursors.Hand;
            iconHost.Tag = workspace;
            RegisterDockWorkspaceIcons(workspace, iconHost, iconFolder, iconHint);

            WireWorkspaceDockControl(buttonPanel);
            WireWorkspaceDockControl(iconHost);
            buttonPanel.Paint -= DockWorkspacePanel_Paint;
            buttonPanel.Paint += DockWorkspacePanel_Paint;

            _dockPanelsByWorkspace[workspace] = buttonPanel;
            var title = _dockWorkspaceTitles[workspace];
            _dockToolTip.SetToolTip(buttonPanel, title);
            _dockToolTip.SetToolTip(iconHost, title);
        }

        private void WireWorkspaceDockControl(Control control)
        {
            control.Click += DockWorkspaceControl_Click;
            control.MouseEnter += DockWorkspaceControl_MouseEnter;
            control.MouseLeave += DockWorkspaceControl_MouseLeave;
        }

        private void DockWorkspaceControl_Click(object? sender, EventArgs e)
        {
            var workspace = TryResolveDockWorkspace(sender);
            if (workspace == null)
                return;

            SetDockWorkspace(workspace.Value);
        }

        private void DockWorkspaceControl_MouseEnter(object? sender, EventArgs e)
        {
            var panel = ResolveDockWorkspacePanel(sender);
            if (panel == null)
                return;

            if (panel.Tag is not DockWorkspaceGroup workspace)
                return;

            if (workspace == _activeDockWorkspace)
                return;

            _hoveredDockWorkspace = workspace;
            UpdateDockWorkspaceSelectionVisuals();
        }

        private void DockWorkspaceControl_MouseLeave(object? sender, EventArgs e)
        {
            var panel = ResolveDockWorkspacePanel(sender);
            if (panel == null)
                return;

            if (panel.Tag is not DockWorkspaceGroup workspace)
                return;

            if (panel.ClientRectangle.Contains(panel.PointToClient(Cursor.Position)))
                return;

            if (_hoveredDockWorkspace == workspace)
            {
                _hoveredDockWorkspace = null;
                UpdateDockWorkspaceSelectionVisuals();
            }
        }

        private DockWorkspaceGroup? TryResolveDockWorkspace(object? sender)
        {
            if (sender is not Control control)
                return null;

            if (control.Tag is DockWorkspaceGroup workspaceFromControl)
                return workspaceFromControl;

            if (control.Parent?.Tag is DockWorkspaceGroup workspaceFromParent)
                return workspaceFromParent;

            return null;
        }

        private static Panel? ResolveDockWorkspacePanel(object? sender)
        {
            if (sender is Panel panel)
                return panel;

            return (sender as Control)?.Parent as Panel;
        }

        private void SetDockWorkspace(DockWorkspaceGroup workspace)
        {
            _activeDockWorkspace = workspace;
            _hoveredDockWorkspace = null;
            UpdateDockWorkspaceSelectionVisuals();

            var showOrdersWorkspace = workspace == DockWorkspaceGroup.Orders;
            scMain.Visible = showOrdersWorkspace;
            if (showOrdersWorkspace)
            {
                if (_workspaceStubPanel != null)
                    _workspaceStubPanel.Visible = false;
                return;
            }

            EnsureWorkspaceStubPanel();
            if (_workspaceStubLabel != null)
            {
                _workspaceStubLabel.Text =
                    $"\u041E\u043A\u043D\u043E \"{_dockWorkspaceTitles[workspace]}\" " +
                    "\u043F\u043E\u043A\u0430 \u043D\u0435 \u043F\u043E\u0434\u043A\u043B\u044E\u0447\u0435\u043D\u043E.";
            }

            if (_workspaceStubPanel != null)
                _workspaceStubPanel.Visible = true;
        }

        private void UpdateDockWorkspaceSelectionVisuals()
        {
            foreach (var (workspace, panel) in _dockPanelsByWorkspace)
            {
                var isActive = workspace == _activeDockWorkspace;
                var isHovered = workspace == _hoveredDockWorkspace;
                panel.BackColor = isActive
                    ? DockButtonActiveBackColor
                    : isHovered
                        ? DockButtonHoverBackColor
                        : DockButtonBackColor;
                panel.Invalidate();

                if (_dockIconHostsByWorkspace.TryGetValue(workspace, out var iconHost))
                {
                    iconHost.Image = isActive
                        ? ResolveDockIconVariant(_dockActiveIconsByWorkspace, workspace)
                        : isHovered
                            ? ResolveDockIconVariant(_dockHoverIconsByWorkspace, workspace)
                            : ResolveDockIconVariant(_dockInactiveIconsByWorkspace, workspace);
                }
            }
        }

        private void DockWorkspacePanel_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not Panel panel || panel.Tag is not DockWorkspaceGroup workspace)
                return;

            if (workspace != _activeDockWorkspace)
                return;

            using var markerBrush = new SolidBrush(DockButtonActiveMarkerColor);
            e.Graphics.FillRectangle(markerBrush, 0, 0, 2, panel.Height);
        }

        private void PnlSidebar_Paint(object? sender, PaintEventArgs e)
        {
            var x = pnlSidebar.ClientSize.Width - 1;
            using var pen = new Pen(DockSidebarDividerColor, 1f);
            e.Graphics.DrawLine(pen, x, 0, x, pnlSidebar.ClientSize.Height);
        }

        private void RegisterDockWorkspaceIcons(
            DockWorkspaceGroup workspace,
            PictureBox iconHost,
            string iconFolder,
            string iconHint)
        {
            DisposeDockWorkspaceIcons(workspace);

            var baseIcon = LoadDockIcon(iconFolder, iconHint, 30);
            _dockInactiveIconsByWorkspace[workspace] = RecolorDockIcon(baseIcon, DockButtonIconColor);
            _dockHoverIconsByWorkspace[workspace] = RecolorDockIcon(baseIcon, DockButtonHoverIconColor);
            _dockActiveIconsByWorkspace[workspace] = RecolorDockIcon(baseIcon, DockButtonActiveIconColor);
            _dockIconHostsByWorkspace[workspace] = iconHost;
            iconHost.Image = _dockInactiveIconsByWorkspace[workspace];

            baseIcon.Dispose();
        }

        private void DisposeDockWorkspaceIcons(DockWorkspaceGroup workspace)
        {
            if (_dockInactiveIconsByWorkspace.Remove(workspace, out var inactiveIcon))
                inactiveIcon.Dispose();
            if (_dockHoverIconsByWorkspace.Remove(workspace, out var hoverIcon))
                hoverIcon.Dispose();
            if (_dockActiveIconsByWorkspace.Remove(workspace, out var activeIcon))
                activeIcon.Dispose();
        }

        private static Image? ResolveDockIconVariant(
            IReadOnlyDictionary<DockWorkspaceGroup, Image> iconsByWorkspace,
            DockWorkspaceGroup workspace)
        {
            if (iconsByWorkspace.TryGetValue(workspace, out var icon))
                return icon;

            return null;
        }

        private static Bitmap RecolorDockIcon(Image source, Color color)
        {
            var srcBitmap = source as Bitmap ?? new Bitmap(source);
            var result = new Bitmap(srcBitmap.Width, srcBitmap.Height);

            for (var y = 0; y < srcBitmap.Height; y++)
            {
                for (var x = 0; x < srcBitmap.Width; x++)
                {
                    var sourcePixel = srcBitmap.GetPixel(x, y);
                    if (sourcePixel.A == 0)
                    {
                        result.SetPixel(x, y, Color.Transparent);
                        continue;
                    }

                    result.SetPixel(x, y, Color.FromArgb(sourcePixel.A, color.R, color.G, color.B));
                }
            }

            if (!ReferenceEquals(srcBitmap, source))
                srcBitmap.Dispose();

            return result;
        }

        private void EnsureWorkspaceStubPanel()
        {
            if (_workspaceStubPanel != null)
                return;

            _workspaceStubLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Pixel),
                ForeColor = Color.FromArgb(90, 96, 116),
                BackColor = Color.Transparent
            };

            _workspaceStubPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 248, 252),
                Visible = false
            };
            _workspaceStubPanel.Controls.Add(_workspaceStubLabel);

            Controls.Add(_workspaceStubPanel);
            // Keep the sidebar docking layout stable: left dock reserves width, fill panels use the rest.
            var mainAreaIndex = Controls.GetChildIndex(scMain);
            Controls.SetChildIndex(_workspaceStubPanel, mainAreaIndex);
        }

        private Image CreateApplicationDockIcon(int targetSize)
        {
            if (Icon != null)
                return new Bitmap(Icon.ToBitmap(), new Size(targetSize, targetSize));

            var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                try
                {
                    using var appIcon = new Icon(iconPath, new Size(targetSize, targetSize));
                    return appIcon.ToBitmap();
                }
                catch
                {
                    // fallback below
                }
            }

            return new Bitmap(SystemIcons.Application.ToBitmap(), new Size(targetSize, targetSize));
        }

        private static Image LoadDockIcon(string iconFolder, string iconHint, int targetSize)
        {
            var icon = OrdersWorkspaceIconCatalog.LoadIcon(iconFolder, iconHint, targetSize);
            if (icon != null)
                return new Bitmap(icon, new Size(targetSize, targetSize));

            return new Bitmap(SystemIcons.Application.ToBitmap(), new Size(targetSize, targetSize));
        }
    }
}

