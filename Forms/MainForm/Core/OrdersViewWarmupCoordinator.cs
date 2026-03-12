using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyManager
{
    internal sealed class OrdersViewWarmupCoordinator : IDisposable
    {
        private readonly int _gridWarmupIntervalMs;
        private readonly Func<bool> _shouldWarmupGrid;
        private readonly Func<string> _buildGridSignature;
        private readonly Action _rebuildGrid;

        private readonly object _sync = new();
        private System.Windows.Forms.Timer? _gridWarmupTimer;
        private CancellationTokenSource? _pdfWarmupCts;
        private string _gridSignature = string.Empty;
        private bool _gridTickBusy;

        public OrdersViewWarmupCoordinator(
            int gridWarmupIntervalMs,
            Func<bool> shouldWarmupGrid,
            Func<string> buildGridSignature,
            Action rebuildGrid)
        {
            _gridWarmupIntervalMs = Math.Max(1000, gridWarmupIntervalMs);
            _shouldWarmupGrid = shouldWarmupGrid ?? throw new ArgumentNullException(nameof(shouldWarmupGrid));
            _buildGridSignature = buildGridSignature ?? throw new ArgumentNullException(nameof(buildGridSignature));
            _rebuildGrid = rebuildGrid ?? throw new ArgumentNullException(nameof(rebuildGrid));
        }

        public void Start()
        {
            _gridSignature = SafeBuildGridSignature();

            _gridWarmupTimer ??= new System.Windows.Forms.Timer
            {
                Interval = _gridWarmupIntervalMs
            };
            _gridWarmupTimer.Tick -= GridWarmupTimer_Tick;
            _gridWarmupTimer.Tick += GridWarmupTimer_Tick;
            _gridWarmupTimer.Start();
        }

        public void SyncGridSignature()
        {
            _gridSignature = SafeBuildGridSignature();
        }

        public void WarmupPdfThumbnails(
            IReadOnlyList<string> pdfPaths,
            Action<string, CancellationToken> warmupAction)
        {
            CancelPdfWarmup();

            if (pdfPaths == null || pdfPaths.Count == 0)
                return;

            if (warmupAction == null)
                return;

            var cts = new CancellationTokenSource();
            lock (_sync)
                _pdfWarmupCts = cts;

            var token = cts.Token;
            _ = Task.Run(() =>
            {
                foreach (var pdfPath in pdfPaths)
                {
                    if (token.IsCancellationRequested)
                        return;

                    try
                    {
                        warmupAction(pdfPath, token);
                    }
                    catch
                    {
                        // Warmup failures must not break UI flow.
                    }
                }
            }, token);
        }

        public void Dispose()
        {
            CancelPdfWarmup();

            if (_gridWarmupTimer == null)
                return;

            _gridWarmupTimer.Stop();
            _gridWarmupTimer.Tick -= GridWarmupTimer_Tick;
            _gridWarmupTimer.Dispose();
            _gridWarmupTimer = null;
        }

        private void GridWarmupTimer_Tick(object? sender, EventArgs e)
        {
            if (_gridTickBusy)
                return;

            if (!_shouldWarmupGrid())
                return;

            _gridTickBusy = true;
            try
            {
                var nextSignature = SafeBuildGridSignature();
                if (string.Equals(nextSignature, _gridSignature, StringComparison.Ordinal))
                    return;

                _rebuildGrid();
            }
            finally
            {
                _gridTickBusy = false;
            }
        }

        private string SafeBuildGridSignature()
        {
            try
            {
                return _buildGridSignature() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CancelPdfWarmup()
        {
            CancellationTokenSource? previousCts;
            lock (_sync)
            {
                previousCts = _pdfWarmupCts;
                _pdfWarmupCts = null;
            }

            if (previousCts == null)
                return;

            try
            {
                previousCts.Cancel();
            }
            catch
            {
                // Ignore cancellation races.
            }
            finally
            {
                previousCts.Dispose();
            }
        }
    }
}
