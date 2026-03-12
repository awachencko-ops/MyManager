using System;
using System.Windows.Forms;

namespace MyManager
{
    public partial class MainForm
    {
        private const int ClassicGridScrollBarGap = 1;
        private const int ClassicGridScrollBarRightMargin = 3;

        private VScrollBar? _classicGridScrollBar;
        private bool _isSyncingClassicGridScrollBar;

        private void InitializeClassicGridScrollBar()
        {
            if (_classicGridScrollBar != null)
                return;

            ReserveRightSideForClassicGridScrollBar();

            // Use dedicated standard WinForms scrollbar, always visible beside the grid.
            dgvJobs.ScrollBars = ScrollBars.None;

            _classicGridScrollBar = new VScrollBar
            {
                Name = "dgvJobsClassicVScrollBar",
                Width = SystemInformation.VerticalScrollBarWidth,
                Margin = new Padding(0, dgvJobs.Margin.Top, ClassicGridScrollBarRightMargin, dgvJobs.Margin.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
                TabStop = false
            };

            _classicGridScrollBar.ValueChanged += ClassicGridScrollBar_ValueChanged;
            tableLayoutPanel1.Controls.Add(_classicGridScrollBar, 0, 2);

            dgvJobs.Scroll += DgvJobs_ScrollForClassicBar;
            dgvJobs.RowsAdded += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.RowsRemoved += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.DataBindingComplete += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.SizeChanged += (_, _) => UpdateClassicGridScrollBar();
            dgvJobs.VisibleChanged += (_, _) => UpdateClassicGridScrollBar();

            UpdateClassicGridScrollBar();
        }

        private void ReserveRightSideForClassicGridScrollBar()
        {
            var requiredRightMargin = SystemInformation.VerticalScrollBarWidth + ClassicGridScrollBarGap + ClassicGridScrollBarRightMargin;
            var currentMargin = dgvJobs.Margin;
            if (currentMargin.Right >= requiredRightMargin)
                return;

            dgvJobs.Margin = new Padding(currentMargin.Left, currentMargin.Top, requiredRightMargin, currentMargin.Bottom);
        }

        private void DgvJobs_ScrollForClassicBar(object? sender, ScrollEventArgs e)
        {
            if (e.ScrollOrientation != ScrollOrientation.VerticalScroll)
                return;

            UpdateClassicGridScrollBar();
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
                _classicGridScrollBar.Minimum = 0;
                _classicGridScrollBar.SmallChange = 1;
                _classicGridScrollBar.LargeChange = largeChange;
                _classicGridScrollBar.Maximum = maximum;
                _classicGridScrollBar.Value = Math.Clamp(value, 0, Math.Max(0, maximum - largeChange + 1));
                _classicGridScrollBar.Enabled = maxFirstRowIndex > 0;
            }
            finally
            {
                _isSyncingClassicGridScrollBar = false;
            }

            _classicGridScrollBar.BringToFront();
        }
    }
}
