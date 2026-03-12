using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyManager
{
    internal sealed class ExplorerMarqueeListView : ListView
    {
        private const int WmPaint = 0x000F;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmMouseMove = 0x0200;
        private const int WmCaptureChanged = 0x0215;
        private const int LvmFirst = 0x1000;
        private const int LvmSetExtendedListViewStyle = LvmFirst + 54;
        private const int LvsExDoubleBuffer = 0x00010000;
        private const int SelectionUpdateIntervalMs = 20;

        private bool _isMarqueeSelecting;
        private bool _isSelectionUpdatePending;
        private Point _marqueeStartPoint;
        private Rectangle _marqueeClientRect = Rectangle.Empty;
        private Rectangle _pendingSelectionRect = Rectangle.Empty;
        private readonly System.Windows.Forms.Timer _selectionUpdateTimer;
        private readonly HashSet<ListViewItem> _marqueeSelectedItems = [];

        public event EventHandler? MarqueeSelectionCompleted;
        public Color MarqueeColor { get; set; } = Color.FromArgb(235, 240, 250);
        public bool IsMarqueeSelecting => _isMarqueeSelecting;

        public ExplorerMarqueeListView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;

            _selectionUpdateTimer = new System.Windows.Forms.Timer
            {
                Interval = SelectionUpdateIntervalMs
            };
            _selectionUpdateTimer.Tick += SelectionUpdateTimer_Tick;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnableNativeDoubleBuffering();
        }

        protected override void Dispose(bool disposing)
        {
            EndMarqueeSelection();

            if (disposing)
            {
                _selectionUpdateTimer.Tick -= SelectionUpdateTimer_Tick;
                _selectionUpdateTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmLButtonDown)
            {
                var point = GetPointFromLParam(m.LParam);
                if (ShouldStartMarqueeSelection(point))
                {
                    BeginMarqueeSelection(point);
                    return;
                }
            }

            if (m.Msg == WmMouseMove && _isMarqueeSelecting)
            {
                if ((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
                {
                    var point = GetPointFromLParam(m.LParam);
                    UpdateMarqueeSelection(point);
                }
                else
                {
                    EndMarqueeSelection();
                }

                return;
            }

            if (m.Msg == WmLButtonUp && _isMarqueeSelecting)
            {
                EndMarqueeSelection();
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WmCaptureChanged)
                EndMarqueeSelection();

            if (m.Msg == WmPaint && _isMarqueeSelecting)
                DrawMarqueeOverlay();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            EndMarqueeSelection();
            base.OnLostFocus(e);
        }

        private void BeginMarqueeSelection(Point clientPoint)
        {
            EndMarqueeSelection();

            _isMarqueeSelecting = true;
            _marqueeStartPoint = ClampToClient(clientPoint);
            _marqueeClientRect = Rectangle.Empty;
            _pendingSelectionRect = Rectangle.Empty;
            _isSelectionUpdatePending = false;
            _marqueeSelectedItems.Clear();
            Capture = true;
            Focus();
            _selectionUpdateTimer.Start();

            BeginUpdate();
            try
            {
                SelectedItems.Clear();
                FocusedItem = null;
            }
            finally
            {
                EndUpdate();
            }
        }

        private void UpdateMarqueeSelection(Point clientPoint)
        {
            if (!_isMarqueeSelecting)
                return;

            var clampedPoint = ClampToClient(clientPoint);
            var clientRect = GetNormalizedRect(_marqueeStartPoint, clampedPoint);
            if (clientRect == _marqueeClientRect)
                return;

            InvalidateMarqueeRect(_marqueeClientRect);
            _marqueeClientRect = clientRect;
            InvalidateMarqueeRect(_marqueeClientRect);
            _pendingSelectionRect = clientRect;
            _isSelectionUpdatePending = true;
        }

        private void EndMarqueeSelection()
        {
            if (!_isMarqueeSelecting)
                return;

            var rectToInvalidate = _marqueeClientRect;
            _selectionUpdateTimer.Stop();
            if (_isSelectionUpdatePending)
                ApplyMarqueeSelection(_pendingSelectionRect);

            _marqueeClientRect = Rectangle.Empty;
            _pendingSelectionRect = Rectangle.Empty;
            _isSelectionUpdatePending = false;
            _isMarqueeSelecting = false;
            Capture = false;
            InvalidateMarqueeRect(rectToInvalidate);
            _marqueeSelectedItems.Clear();
            MarqueeSelectionCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyMarqueeSelection(Rectangle clientRect)
        {
            if (!_isMarqueeSelecting && (clientRect.Width <= 0 || clientRect.Height <= 0))
                return;

            ListViewItem? firstSelected = null;

            foreach (ListViewItem item in Items)
            {
                var shouldBeSelected = item.Bounds.IntersectsWith(clientRect);
                var isTrackedAsSelected = _marqueeSelectedItems.Contains(item);

                if (shouldBeSelected)
                {
                    firstSelected ??= item;

                    if (!isTrackedAsSelected)
                    {
                        item.Selected = true;
                        _marqueeSelectedItems.Add(item);
                    }
                }
                else if (isTrackedAsSelected)
                {
                    item.Selected = false;
                    _marqueeSelectedItems.Remove(item);
                }
                else if (item.Selected)
                {
                    item.Selected = false;
                }
            }

            if (firstSelected != null && !ReferenceEquals(FocusedItem, firstSelected))
                firstSelected.Focused = true;
        }

        private void SelectionUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMarqueeSelecting)
            {
                _selectionUpdateTimer.Stop();
                return;
            }

            if (!_isSelectionUpdatePending)
                return;

            _isSelectionUpdatePending = false;
            ApplyMarqueeSelection(_pendingSelectionRect);
        }

        private void DrawMarqueeOverlay()
        {
            if (_marqueeClientRect.Width <= 0 || _marqueeClientRect.Height <= 0 || !IsHandleCreated)
                return;

            using var graphics = CreateGraphics();
            var fillColor = Color.FromArgb(96, MarqueeColor);
            var borderColor = Color.FromArgb(210, ControlPaint.Dark(MarqueeColor, 0.18f));
            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor, 1f);

            graphics.FillRectangle(fillBrush, _marqueeClientRect);
            graphics.DrawRectangle(borderPen, GetBorderRect(_marqueeClientRect));
        }

        private void InvalidateMarqueeRect(Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var invalidateRect = Rectangle.Inflate(rect, 2, 2);
            invalidateRect.Intersect(ClientRectangle);
            if (invalidateRect.Width <= 0 || invalidateRect.Height <= 0)
                return;

            Invalidate(invalidateRect);
        }

        private bool ShouldStartMarqueeSelection(Point point)
        {
            if (HasSelectionModifiers())
                return false;

            return HitTest(point).Item == null;
        }

        private Point ClampToClient(Point point)
        {
            var maxX = Math.Max(0, ClientSize.Width - 1);
            var maxY = Math.Max(0, ClientSize.Height - 1);
            var clampedX = Math.Max(0, Math.Min(maxX, point.X));
            var clampedY = Math.Max(0, Math.Min(maxY, point.Y));
            return new Point(clampedX, clampedY);
        }

        private static bool HasSelectionModifiers()
        {
            return (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
        }

        private static Point GetPointFromLParam(IntPtr lParam)
        {
            var value = unchecked((int)(long)lParam);
            var x = (short)(value & 0xFFFF);
            var y = (short)((value >> 16) & 0xFFFF);
            return new Point(x, y);
        }

        private void EnableNativeDoubleBuffering()
        {
            if (!IsHandleCreated)
                return;

            SendMessage(
                Handle,
                LvmSetExtendedListViewStyle,
                (IntPtr)LvsExDoubleBuffer,
                (IntPtr)LvsExDoubleBuffer);
        }

        private static Rectangle GetBorderRect(Rectangle rect)
        {
            if (rect.Width <= 1 || rect.Height <= 1)
                return rect;

            return new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        private static Rectangle GetNormalizedRect(Point start, Point end)
        {
            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            var width = Math.Abs(end.X - start.X);
            var height = Math.Abs(end.Y - start.Y);
            return new Rectangle(left, top, width, height);
        }
    }
}
