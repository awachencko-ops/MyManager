using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MyManager
{
    public partial class MainForm
    {
        private enum DockWorkspaceGroup
        {
            Orders,
            Literature,
            Utilities
        }

        private readonly ToolTip _dockToolTip = new();
        private readonly Dictionary<DockWorkspaceGroup, Panel> _dockPanelsByWorkspace = [];
        private readonly Dictionary<DockWorkspaceGroup, string> _dockWorkspaceTitles = new()
        {
            [DockWorkspaceGroup.Orders] = "\u0417\u0430\u043A\u0430\u0437\u044B",
            [DockWorkspaceGroup.Literature] = "\u041B\u0438\u0442\u0435\u0440\u0430\u0442\u0443\u0440\u0430",
            [DockWorkspaceGroup.Utilities] = "\u0423\u0442\u0438\u043B\u0438\u0442\u044B"
        };

        private DockWorkspaceGroup _activeDockWorkspace = DockWorkspaceGroup.Orders;
        private Panel? _workspaceStubPanel;
        private Label? _workspaceStubLabel;

        private static readonly Color DockSidebarBackColor = Color.FromArgb(61, 68, 88);
        private static readonly Color DockButtonBackColor = Color.FromArgb(61, 68, 88);
        private static readonly Color DockButtonHoverBackColor = Color.FromArgb(78, 86, 108);
        private static readonly Color DockButtonActiveBackColor = Color.FromArgb(104, 163, 216);
        private static readonly Color DockLockedButtonBackColor = Color.FromArgb(50, 56, 74);

        private void InitializeDockSidebar()
        {
            pnlSidebar.BackColor = DockSidebarBackColor;

            ConfigureLockedAppDockButton();
            ConfigureWorkspaceDockButton(
                DockWorkspaceGroup.Orders,
                pnl_Orders,
                pictureBox1,
                Path.Combine("Icons", "cards", "cards_24dp_1F1F1F_FILL0_wght400_GRAD0_opsz24.png"));

            ConfigureWorkspaceDockButton(
                DockWorkspaceGroup.Literature,
                pnlDockLiterature,
                pictureBox4,
                Path.Combine("Icons", "file export", "file_export_24dp_1F1F1F_FILL0_wght400_GRAD0_opsz24.png"));
            ConfigureWorkspaceDockButton(
                DockWorkspaceGroup.Utilities,
                pnlDockUtilities,
                pictureBox3,
                Path.Combine("Icons", "settings", "settings_24dp_1F1F1F_FILL1_wght400_GRAD0_opsz24.png"));

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

            var appTitle = string.IsNullOrWhiteSpace(Application.ProductName) ? "MyManager" : Application.ProductName;
            _dockToolTip.SetToolTip(pnl_Icon, appTitle);
            _dockToolTip.SetToolTip(pictureBox2, appTitle);
        }

        private void ConfigureWorkspaceDockButton(
            DockWorkspaceGroup workspace,
            Panel buttonPanel,
            PictureBox iconHost,
            string iconRelativePath)
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
            iconHost.Image?.Dispose();
            iconHost.Image = LoadDockIcon(iconRelativePath, 30);

            WireWorkspaceDockControl(buttonPanel);
            WireWorkspaceDockControl(iconHost);

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

            panel.BackColor = DockButtonHoverBackColor;
        }

        private void DockWorkspaceControl_MouseLeave(object? sender, EventArgs e)
        {
            var panel = ResolveDockWorkspacePanel(sender);
            if (panel == null)
                return;

            if (panel.Tag is not DockWorkspaceGroup workspace)
                return;

            panel.BackColor = workspace == _activeDockWorkspace
                ? DockButtonActiveBackColor
                : DockButtonBackColor;
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
            UpdateDockWorkspaceSelectionVisuals();

            var showOrdersWorkspace = workspace == DockWorkspaceGroup.Orders;
            scMain.Visible = showOrdersWorkspace;
            if (showOrdersWorkspace)
            {
                if (_workspaceStubPanel != null)
                    _workspaceStubPanel.Visible = false;

                scMain.BringToFront();
                pnlSidebar.BringToFront();
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

            _workspaceStubPanel?.BringToFront();
            pnlSidebar.BringToFront();
        }

        private void UpdateDockWorkspaceSelectionVisuals()
        {
            foreach (var (workspace, panel) in _dockPanelsByWorkspace)
            {
                panel.BackColor = workspace == _activeDockWorkspace
                    ? DockButtonActiveBackColor
                    : DockButtonBackColor;
            }
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

        private static Image LoadDockIcon(string relativePath, int targetSize)
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, relativePath);
                if (File.Exists(iconPath))
                {
                    using var stream = File.OpenRead(iconPath);
                    using var image = Image.FromStream(stream);
                    return new Bitmap(image, new Size(targetSize, targetSize));
                }
            }
            catch
            {
                // fallback below
            }

            return new Bitmap(SystemIcons.Application.ToBitmap(), new Size(targetSize, targetSize));
        }
    }
}
