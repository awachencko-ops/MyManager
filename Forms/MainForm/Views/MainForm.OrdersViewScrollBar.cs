using System;
using System.Reflection;
using System.Windows.Forms;
using Manina.Windows.Forms;

namespace Replica
{
    public partial class MainForm
    {
        private const int OrdersViewScrollBarWidth = 16;
        private const int OrdersViewScrollBarGap = 1;
        private const int OrdersViewScrollBarRightMargin = 3;

        private static readonly FieldInfo? ImageListViewVScrollBarField = typeof(ImageListView).GetField(
            "vScrollBar",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private HoverStateVScrollBar? _ordersViewScrollBar;
        private VScrollBar? _tilesInnerVScrollBar;
        private bool _isSyncingOrdersViewScrollBar;

        private void InitializeOrdersViewScrollBar()
        {
            if (_ordersViewScrollBar != null)
                return;

            ReserveRightSideForOrdersViewScrollBar(dgvJobs);
            ReserveRightSideForOrdersViewScrollBar(_lvPrintTiles);

            dgvJobs.ScrollBars = ScrollBars.Horizontal;
            _lvPrintTiles.ScrollBars = false;

            _ordersViewScrollBar = new HoverStateVScrollBar
            {
                Name = "ordersViewScrollBar",
                Width = OrdersViewScrollBarWidth,
                Margin = new Padding(0, dgvJobs.Margin.Top, OrdersViewScrollBarRightMargin, dgvJobs.Margin.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
                TabStop = false
            };

            _ordersViewScrollBar.ValueChanged += OrdersViewScrollBar_ValueChanged;
            tableLayoutPanel1.Controls.Add(_ordersViewScrollBar, 0, 2);

            dgvJobs.Scroll += DgvJobs_ScrollForCustomBar;
            dgvJobs.RowsAdded += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            dgvJobs.RowsRemoved += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            dgvJobs.DataBindingComplete += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            dgvJobs.SizeChanged += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            dgvJobs.VisibleChanged += (_, _) => UpdateOrdersViewScrollBarFromActiveView();

            _lvPrintTiles.SizeChanged += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            _lvPrintTiles.Layout += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            _lvPrintTiles.MouseWheel += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            _lvPrintTiles.VisibleChanged += (_, _) => UpdateOrdersViewScrollBarFromActiveView();
            _lvPrintTiles.HandleCreated += (_, _) =>
            {
                AttachTilesInternalVScrollBar();
                UpdateOrdersViewScrollBarFromActiveView();
            };

            AttachTilesInternalVScrollBar();
            UpdateOrdersViewScrollBarFromActiveView();
        }

        private static void ReserveRightSideForOrdersViewScrollBar(Control control)
        {
            var margin = control.Margin;
            var requiredRightMargin = OrdersViewScrollBarWidth + OrdersViewScrollBarGap + OrdersViewScrollBarRightMargin;
            if (margin.Right >= requiredRightMargin)
                return;

            control.Margin = new Padding(margin.Left, margin.Top, requiredRightMargin, margin.Bottom);
        }

        private void OrdersViewScrollBar_ValueChanged(object? sender, EventArgs e)
        {
            if (_ordersViewScrollBar == null || _isSyncingOrdersViewScrollBar)
                return;

            if (dgvJobs.Visible)
            {
                ApplyOrdersGridScrollFromCustomBar(_ordersViewScrollBar.Value);
                return;
            }

            if (_lvPrintTiles.Visible)
                ApplyOrdersTilesScrollFromCustomBar(_ordersViewScrollBar.Value);
        }

        private void DgvJobs_ScrollForCustomBar(object? sender, ScrollEventArgs e)
        {
            if (e.ScrollOrientation != ScrollOrientation.VerticalScroll)
                return;

            UpdateOrdersViewScrollBarFromActiveView();
        }

        private void AttachTilesInternalVScrollBar()
        {
            var currentVScrollBar = ImageListViewVScrollBarField?.GetValue(_lvPrintTiles) as VScrollBar;
            if (ReferenceEquals(_tilesInnerVScrollBar, currentVScrollBar))
                return;

            if (_tilesInnerVScrollBar != null)
            {
                _tilesInnerVScrollBar.Scroll -= TilesInnerVScrollBar_Scroll;
                _tilesInnerVScrollBar.ValueChanged -= TilesInnerVScrollBar_ValueChanged;
                _tilesInnerVScrollBar.VisibleChanged -= TilesInnerVScrollBar_VisibleChanged;
            }

            _tilesInnerVScrollBar = currentVScrollBar;
            if (_tilesInnerVScrollBar != null)
            {
                _tilesInnerVScrollBar.Scroll += TilesInnerVScrollBar_Scroll;
                _tilesInnerVScrollBar.ValueChanged += TilesInnerVScrollBar_ValueChanged;
                _tilesInnerVScrollBar.VisibleChanged += TilesInnerVScrollBar_VisibleChanged;
            }
        }

        private void TilesInnerVScrollBar_Scroll(object? sender, ScrollEventArgs e)
        {
            UpdateOrdersViewScrollBarFromActiveView();
        }

        private void TilesInnerVScrollBar_ValueChanged(object? sender, EventArgs e)
        {
            UpdateOrdersViewScrollBarFromActiveView();
        }

        private void TilesInnerVScrollBar_VisibleChanged(object? sender, EventArgs e)
        {
            UpdateOrdersViewScrollBarFromActiveView();
        }

        private void ApplyOrdersGridScrollFromCustomBar(int targetFirstRowIndex)
        {
            if (dgvJobs.Rows.Count == 0)
                return;

            var maxFirstRowIndex = GetOrdersGridMaxFirstRowIndex();
            var nextFirstRowIndex = Math.Clamp(targetFirstRowIndex, 0, maxFirstRowIndex);

            try
            {
                if (dgvJobs.FirstDisplayedScrollingRowIndex != nextFirstRowIndex)
                    dgvJobs.FirstDisplayedScrollingRowIndex = nextFirstRowIndex;
            }
            catch
            {
                // DataGridView can throw while rows are being rebuilt.
            }

            UpdateOrdersViewScrollBarFromActiveView();
        }

        private void ApplyOrdersTilesScrollFromCustomBar(int targetValue)
        {
            AttachTilesInternalVScrollBar();
            if (_tilesInnerVScrollBar == null)
                return;

            var minimum = _tilesInnerVScrollBar.Minimum;
            var maximum = GetVScrollBarMaxScrollableValue(_tilesInnerVScrollBar);
            var nextValue = Math.Clamp(targetValue, minimum, maximum);

            if (_tilesInnerVScrollBar.Value != nextValue)
                _tilesInnerVScrollBar.Value = nextValue;

            UpdateOrdersViewScrollBarFromActiveView();
        }

        private int GetOrdersGridMaxFirstRowIndex()
        {
            if (dgvJobs.Rows.Count == 0)
                return 0;

            var visibleRows = GetOrdersGridVisibleRowCapacity();
            return Math.Max(0, dgvJobs.Rows.Count - visibleRows);
        }

        private int GetOrdersGridVisibleRowCapacity()
        {
            var displayHeight = dgvJobs.DisplayRectangle.Height;
            var fallback = Math.Max(1, dgvJobs.DisplayedRowCount(false));
            if (displayHeight <= 0)
                return fallback;

            var rowHeight = Math.Max(1, dgvJobs.RowTemplate.Height);
            var estimatedCapacity = Math.Max(1, displayHeight / rowHeight);
            return Math.Max(fallback, estimatedCapacity);
        }

        private static int GetVScrollBarMaxScrollableValue(VScrollBar vScrollBar)
        {
            var largeChange = Math.Max(1, vScrollBar.LargeChange);
            return Math.Max(vScrollBar.Minimum, vScrollBar.Maximum - largeChange + 1);
        }

        private void UpdateOrdersViewScrollBarFromActiveView()
        {
            if (_ordersViewScrollBar == null || IsDisposed)
                return;

            var hasVisibleOrdersView = dgvJobs.Visible || _lvPrintTiles.Visible;
            _ordersViewScrollBar.Visible = hasVisibleOrdersView;
            if (!hasVisibleOrdersView)
                return;

            _isSyncingOrdersViewScrollBar = true;
            try
            {
                if (dgvJobs.Visible)
                    SyncOrdersViewScrollBarFromGrid();
                else
                    SyncOrdersViewScrollBarFromTiles();
            }
            finally
            {
                _isSyncingOrdersViewScrollBar = false;
            }

            _ordersViewScrollBar.BringToFront();
        }

        private void SyncOrdersViewScrollBarFromGrid()
        {
            if (_ordersViewScrollBar == null)
                return;

            var rowCount = dgvJobs.Rows.Count;
            var largeChange = GetOrdersGridVisibleRowCapacity();
            var maximum = Math.Max(0, rowCount - largeChange);
            var value = 0;

            if (rowCount > 0)
            {
                try
                {
                    if (maximum == 0 && dgvJobs.FirstDisplayedScrollingRowIndex != 0)
                        dgvJobs.FirstDisplayedScrollingRowIndex = 0;

                    value = Math.Clamp(dgvJobs.FirstDisplayedScrollingRowIndex, 0, maximum);
                }
                catch
                {
                    value = 0;
                }
            }

            _ordersViewScrollBar.SetState(0, maximum, largeChange, 1, value);
        }

        private void SyncOrdersViewScrollBarFromTiles()
        {
            if (_ordersViewScrollBar == null)
                return;

            AttachTilesInternalVScrollBar();
            if (_tilesInnerVScrollBar == null)
            {
                _ordersViewScrollBar.SetState(0, 0, 1, 1, 0);
                return;
            }

            var minimum = _tilesInnerVScrollBar.Minimum;
            var largeChange = Math.Max(1, _tilesInnerVScrollBar.LargeChange);
            var smallChange = Math.Max(1, _tilesInnerVScrollBar.SmallChange);
            var maximum = GetVScrollBarMaxScrollableValue(_tilesInnerVScrollBar);
            var value = Math.Clamp(_tilesInnerVScrollBar.Value, minimum, maximum);

            _ordersViewScrollBar.SetState(minimum, maximum, largeChange, smallChange, value);
        }
    }
}
