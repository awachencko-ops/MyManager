using System;
using System.Reflection;
using System.Windows.Forms;
using Manina.Windows.Forms;

namespace MyManager
{
    public partial class MainForm
    {
        private static readonly FieldInfo? ClassicTilesInnerVScrollBarField = typeof(ImageListView).GetField(
            "vScrollBar",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private HoverStateVScrollBar? _classicGridScrollBar;
        private bool _isSyncingClassicGridScrollBar;
        private HoverStateVScrollBar? _classicTilesScrollBar;
        private VScrollBar? _classicTilesInnerVScrollBar;
        private bool _isSyncingClassicTilesScrollBar;

        private void InitializeClassicGridScrollBar()
        {
            if (_classicGridScrollBar != null)
                return;

            // Use dedicated standard WinForms scrollbar as a child of the grid itself.
            // This avoids putting multiple controls into one TableLayoutPanel cell.
            dgvJobs.ScrollBars = ScrollBars.None;

            _classicGridScrollBar = new HoverStateVScrollBar
            {
                Name = "dgvJobsClassicVScrollBar",
                Width = SystemInformation.VerticalScrollBarWidth,
                Dock = DockStyle.Right,
                TabStop = false
            };

            dgvJobs.Controls.Add(_classicGridScrollBar);
            _classicGridScrollBar.ValueChanged += ClassicGridScrollBar_ValueChanged;

            dgvJobs.Scroll += DgvJobs_ScrollForClassicBar;
            dgvJobs.MouseWheel += DgvJobs_MouseWheelForClassicBar;
            dgvJobs.RowsAdded += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.RowsRemoved += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.DataBindingComplete += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.SizeChanged += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.VisibleChanged += (_, _) => UpdateClassicGridScrollBar();

            UpdateClassicGridScrollBar();
        }

        private void DgvJobs_ScrollForClassicBar(object? sender, ScrollEventArgs e)
        {
            if (e.ScrollOrientation != ScrollOrientation.VerticalScroll)
                return;

            UpdateClassicGridScrollBar();
        }

        private void DgvJobs_MouseWheelForClassicBar(object? sender, MouseEventArgs e)
        {
            if (!IsGridInputOverClassicScrollBar() || _classicGridScrollBar == null)
                return;

            if (e is HandledMouseEventArgs handledMouseEventArgs)
                handledMouseEventArgs.Handled = true;

            StopGridHoverActivation();
            ClearGridHoverVisual();
            _classicGridScrollBar.ApplyMouseWheelDelta(e.Delta);
        }

        private bool IsGridInputOverClassicScrollBar()
        {
            if (_classicGridScrollBar == null || !_classicGridScrollBar.Visible || !dgvJobs.Visible)
                return false;

            if (_classicGridScrollBar.Capture)
                return true;

            var mouseClient = dgvJobs.PointToClient(Cursor.Position);
            return _classicGridScrollBar.Bounds.Contains(mouseClient);
        }

        private void ClassicGridScrollBar_ValueChanged(object? sender, EventArgs e)
        {
            if (_classicGridScrollBar == null || _isSyncingClassicGridScrollBar)
                return;

            if (!dgvJobs.Visible || dgvJobs.Rows.Count == 0)
                return;

            var maxFirstRowIndex = GetClassicGridMaxFirstRowIndex();
            var targetFirstRowIndex = Math.Clamp(_classicGridScrollBar.Value, 0, maxFirstRowIndex);

            try
            {
                if (dgvJobs.FirstDisplayedScrollingRowIndex != targetFirstRowIndex)
                    dgvJobs.FirstDisplayedScrollingRowIndex = targetFirstRowIndex;
            }
            catch
            {
                // DataGridView can throw while its rows are being rebuilt.
            }

            UpdateClassicGridScrollBar();
        }

        private int GetClassicGridVisibleRowCapacity()
        {
            if (dgvJobs.Rows.Count == 0)
                return 1;

            var displayHeight = dgvJobs.DisplayRectangle.Height;
            if (displayHeight <= 0)
                return Math.Max(1, dgvJobs.DisplayedRowCount(false));

            var rowHeight = Math.Max(1, dgvJobs.RowTemplate.Height);
            return Math.Max(1, displayHeight / rowHeight);
        }

        private int GetClassicGridMaxFirstRowIndex()
        {
            var visibleCapacity = GetClassicGridVisibleRowCapacity();
            return Math.Max(0, dgvJobs.Rows.Count - visibleCapacity);
        }

        private void UpdateClassicGridScrollBar()
        {
            if (_classicGridScrollBar == null || IsDisposed)
                return;

            _classicGridScrollBar.Visible = dgvJobs.Visible;
            if (!dgvJobs.Visible)
                return;

            var rowCount = dgvJobs.Rows.Count;
            var visibleCapacity = GetClassicGridVisibleRowCapacity();
            var maxFirstRowIndex = Math.Max(0, rowCount - visibleCapacity);
            var largeChange = Math.Max(1, Math.Min(visibleCapacity, Math.Max(1, rowCount)));
            var maximum = Math.Max(0, maxFirstRowIndex + largeChange - 1);

            var value = 0;
            if (rowCount > 0)
            {
                try
                {
                    value = Math.Clamp(dgvJobs.FirstDisplayedScrollingRowIndex, 0, maxFirstRowIndex);
                }
                catch
                {
                    value = 0;
                }
            }

            _isSyncingClassicGridScrollBar = true;
            try
            {
                _classicGridScrollBar.SetState(
                    minimum: 0,
                    maximum: Math.Max(0, maximum - largeChange + 1),
                    largeChange: largeChange,
                    smallChange: 1,
                    value: Math.Clamp(value, 0, Math.Max(0, maximum - largeChange + 1)));
                // Keep the overlay control enabled so it always consumes mouse hit-testing,
                // even when there is no scrollable range.
                _classicGridScrollBar.Enabled = true;
            }
            finally
            {
                _isSyncingClassicGridScrollBar = false;
            }

            _classicGridScrollBar.BringToFront();
        }

        private void InitializeClassicTilesScrollBar()
        {
            if (_classicTilesScrollBar != null)
                return;

            // Hide native scrollbar and render a custom one with the same style as the grid.
            _lvPrintTiles.ScrollBars = false;

            _classicTilesScrollBar = new HoverStateVScrollBar
            {
                Name = "lvPrintTilesClassicVScrollBar",
                Width = SystemInformation.VerticalScrollBarWidth,
                Dock = DockStyle.Right,
                TabStop = false
            };

            _lvPrintTiles.Controls.Add(_classicTilesScrollBar);
            _classicTilesScrollBar.ValueChanged += ClassicTilesScrollBar_ValueChanged;

            _lvPrintTiles.SizeChanged += (_, _) => UpdateClassicTilesScrollBar();
            _lvPrintTiles.Layout += (_, _) => UpdateClassicTilesScrollBar();
            _lvPrintTiles.VisibleChanged += (_, _) => UpdateClassicTilesScrollBar();
            _lvPrintTiles.MouseWheel += (_, _) => UpdateClassicTilesScrollBar();
            _lvPrintTiles.HandleCreated += (_, _) =>
            {
                AttachClassicTilesInnerVScrollBar();
                UpdateClassicTilesScrollBar();
            };

            AttachClassicTilesInnerVScrollBar();
            UpdateClassicTilesScrollBar();
        }

        private void AttachClassicTilesInnerVScrollBar()
        {
            var currentVScrollBar = ClassicTilesInnerVScrollBarField?.GetValue(_lvPrintTiles) as VScrollBar;
            if (ReferenceEquals(_classicTilesInnerVScrollBar, currentVScrollBar))
                return;

            if (_classicTilesInnerVScrollBar != null)
            {
                _classicTilesInnerVScrollBar.Scroll -= ClassicTilesInnerVScrollBar_Scroll;
                _classicTilesInnerVScrollBar.ValueChanged -= ClassicTilesInnerVScrollBar_ValueChanged;
                _classicTilesInnerVScrollBar.VisibleChanged -= ClassicTilesInnerVScrollBar_VisibleChanged;
            }

            _classicTilesInnerVScrollBar = currentVScrollBar;
            if (_classicTilesInnerVScrollBar != null)
            {
                _classicTilesInnerVScrollBar.Scroll += ClassicTilesInnerVScrollBar_Scroll;
                _classicTilesInnerVScrollBar.ValueChanged += ClassicTilesInnerVScrollBar_ValueChanged;
                _classicTilesInnerVScrollBar.VisibleChanged += ClassicTilesInnerVScrollBar_VisibleChanged;
            }
        }

        private void ClassicTilesInnerVScrollBar_Scroll(object? sender, ScrollEventArgs e)
        {
            UpdateClassicTilesScrollBar();
        }

        private void ClassicTilesInnerVScrollBar_ValueChanged(object? sender, EventArgs e)
        {
            UpdateClassicTilesScrollBar();
        }

        private void ClassicTilesInnerVScrollBar_VisibleChanged(object? sender, EventArgs e)
        {
            UpdateClassicTilesScrollBar();
        }

        private void ClassicTilesScrollBar_ValueChanged(object? sender, EventArgs e)
        {
            if (_classicTilesScrollBar == null || _isSyncingClassicTilesScrollBar)
                return;

            if (!_lvPrintTiles.Visible)
                return;

            AttachClassicTilesInnerVScrollBar();
            if (_classicTilesInnerVScrollBar == null)
                return;

            var minimum = _classicTilesInnerVScrollBar.Minimum;
            var maximum = GetClassicVScrollBarMaxScrollableValue(_classicTilesInnerVScrollBar);
            var nextValue = Math.Clamp(_classicTilesScrollBar.Value, minimum, maximum);

            if (_classicTilesInnerVScrollBar.Value != nextValue)
                _classicTilesInnerVScrollBar.Value = nextValue;

            UpdateClassicTilesScrollBar();
        }

        private static int GetClassicVScrollBarMaxScrollableValue(VScrollBar vScrollBar)
        {
            var largeChange = Math.Max(1, vScrollBar.LargeChange);
            return Math.Max(vScrollBar.Minimum, vScrollBar.Maximum - largeChange + 1);
        }

        private void UpdateClassicTilesScrollBar()
        {
            if (_classicTilesScrollBar == null || IsDisposed)
                return;

            _classicTilesScrollBar.Visible = _lvPrintTiles.Visible;
            if (!_lvPrintTiles.Visible)
                return;

            AttachClassicTilesInnerVScrollBar();
            if (_classicTilesInnerVScrollBar == null)
            {
                _isSyncingClassicTilesScrollBar = true;
                try
                {
                    _classicTilesScrollBar.SetState(0, 0, 1, 1, 0);
                    _classicTilesScrollBar.Enabled = false;
                }
                finally
                {
                    _isSyncingClassicTilesScrollBar = false;
                }

                _classicTilesScrollBar.BringToFront();
                return;
            }

            var minimum = _classicTilesInnerVScrollBar.Minimum;
            var largeChange = Math.Max(1, _classicTilesInnerVScrollBar.LargeChange);
            var smallChange = Math.Max(1, _classicTilesInnerVScrollBar.SmallChange);
            var maximum = GetClassicVScrollBarMaxScrollableValue(_classicTilesInnerVScrollBar);
            var value = Math.Clamp(_classicTilesInnerVScrollBar.Value, minimum, maximum);

            _isSyncingClassicTilesScrollBar = true;
            try
            {
                _classicTilesScrollBar.SetState(minimum, maximum, largeChange, smallChange, value);
                _classicTilesScrollBar.Enabled = maximum > minimum;
            }
            finally
            {
                _isSyncingClassicTilesScrollBar = false;
            }

            _classicTilesScrollBar.BringToFront();
        }
    }
}
