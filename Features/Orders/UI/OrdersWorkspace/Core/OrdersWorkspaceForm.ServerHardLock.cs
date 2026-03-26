using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private static readonly Color ServerHardLockOverlayBackColor = Color.FromArgb(228, 236, 242);
        private const string ServerHardLockTitle = "\u041d\u0435\u0442 \u0441\u043e\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u044f \u0441 \u0441\u0435\u0440\u0432\u0435\u0440\u043e\u043c";

        private void InitializeServerHardLockUi()
        {
            EnsureServerHardLockOverlays();
            ApplyServerHardLockState(
                shouldLock: ShouldUseLanRunApi(),
                details: "\u041f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u043f\u043e\u0434\u043a\u043b\u044e\u0447\u0435\u043d\u0438\u044f \u043a \u0441\u0435\u0440\u0432\u0435\u0440\u0443 \u0432\u044b\u043f\u043e\u043b\u043d\u044f\u0435\u0442\u0441\u044f...");
        }

        private bool EnsureServerWriteAllowed(string operationCaption)
        {
            if (!ShouldUseLanRunApi())
                return true;

            if (!CanQuickReachLanApiHost())
            {
                RequestLanServerProbe("write-guard", force: true);
                var quickCheckDetails = "\u0411\u044b\u0441\u0442\u0440\u0430\u044f TCP-\u043f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u0434\u043e API \u043d\u0435 \u043f\u0440\u043e\u0448\u043b\u0430.";
                ApplyServerHardLockState(shouldLock: true, quickCheckDetails);
                SetBottomStatus($"{operationCaption}: \u043d\u0435\u0442 \u0441\u043e\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u044f \u0441 \u0441\u0435\u0440\u0432\u0435\u0440\u043e\u043c");

                var nowUtcQuick = DateTime.UtcNow;
                if ((nowUtcQuick - _serverHardLockLastDialogUtc).TotalMilliseconds >= 1200)
                {
                    _serverHardLockLastDialogUtc = nowUtcQuick;
                    MessageBox.Show(
                        this,
                        $"{operationCaption} \u043d\u0435\u0434\u043e\u0441\u0442\u0443\u043f\u043d\u043e.{Environment.NewLine}{ServerHardLockTitle}.{Environment.NewLine}{quickCheckDetails}",
                        operationCaption,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }

            var snapshot = GetLanServerProbeSnapshot(out var probeInProgress, out _);
            if (snapshot.ApiReachable && snapshot.IsReady)
                return true;

            RequestLanServerProbe("write-guard", force: true);
            var details = BuildServerUnavailableDetails(snapshot, probeInProgress);
            ApplyServerHardLockState(shouldLock: true, details);
            SetBottomStatus($"{operationCaption}: \u043d\u0435\u0442 \u0441\u043e\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u044f \u0441 \u0441\u0435\u0440\u0432\u0435\u0440\u043e\u043c");

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _serverHardLockLastDialogUtc).TotalMilliseconds >= 1200)
            {
                _serverHardLockLastDialogUtc = nowUtc;
                MessageBox.Show(
                    this,
                    $"{operationCaption} \u043d\u0435\u0434\u043e\u0441\u0442\u0443\u043f\u043d\u043e.{Environment.NewLine}{ServerHardLockTitle}.{Environment.NewLine}{details}",
                    operationCaption,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        private bool CanQuickReachLanApiHost()
        {
            if (!TryResolveLanApiBaseUri(_lanApiBaseUrl, out var baseUri))
                return false;

            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(baseUri.Host, baseUri.Port);
                var connectedInTime = connectTask.Wait(TimeSpan.FromMilliseconds(450));
                return connectedInTime && tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyServerHardLockFromLanSnapshot(LanServerProbeSnapshot snapshot, bool probeInProgress)
        {
            if (!ShouldUseLanRunApi())
            {
                ApplyServerHardLockState(shouldLock: false, details: string.Empty);
                return;
            }

            var shouldLock = !snapshot.ApiReachable || !snapshot.IsReady;
            var details = shouldLock
                ? BuildServerUnavailableDetails(snapshot, probeInProgress)
                : string.Empty;
            ApplyServerHardLockState(shouldLock, details);
        }

        private void ApplyServerHardLockState(bool shouldLock, string details)
        {
            EnsureServerHardLockOverlays();

            if (!ShouldUseLanRunApi())
                shouldLock = false;

            var normalizedDetails = string.IsNullOrWhiteSpace(details)
                ? "\u041e\u043f\u0435\u0440\u0430\u0446\u0438\u0438 \u0432\u0440\u0435\u043c\u0435\u043d\u043d\u043e \u043d\u0435\u0434\u043e\u0441\u0442\u0443\u043f\u043d\u044b."
                : details.Trim();

            if (_serverHardLockOverlayTitleMain != null)
                _serverHardLockOverlayTitleMain.Text = ServerHardLockTitle;
            if (_serverHardLockOverlayDetailsMain != null)
                _serverHardLockOverlayDetailsMain.Text = normalizedDetails;

            if (_serverHardLockOverlayPanelMain != null)
            {
                _serverHardLockOverlayPanelMain.Visible = shouldLock;
                if (shouldLock)
                    _serverHardLockOverlayPanelMain.BringToFront();
            }

            if (_serverHardLockOverlayPanelQueue != null)
            {
                _serverHardLockOverlayPanelQueue.Visible = shouldLock;
                if (shouldLock)
                    _serverHardLockOverlayPanelQueue.BringToFront();
            }

            if (_serverHardLockActive == shouldLock)
                return;

            _serverHardLockActive = shouldLock;
            UpdateActionButtonsState();
        }

        private void EnsureServerHardLockOverlays()
        {
            if (_serverHardLockOverlayPanelMain == null && !tableLayoutPanel1.IsDisposed)
            {
                var panel = new Panel
                {
                    Name = "pnlServerHardLockMain",
                    BackColor = ServerHardLockOverlayBackColor,
                    Margin = Padding.Empty,
                    Dock = DockStyle.Fill,
                    Visible = false
                };

                var titleLabel = new Label
                {
                    Name = "lblServerHardLockTitleMain",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Top,
                    Height = 48,
                    Font = new Font("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Pixel),
                    ForeColor = Color.Firebrick,
                    Text = ServerHardLockTitle
                };

                var detailsLabel = new Label
                {
                    Name = "lblServerHardLockDetailsMain",
                    AutoSize = false,
                    TextAlign = ContentAlignment.TopCenter,
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel),
                    ForeColor = Color.FromArgb(70, 70, 70),
                    Padding = new Padding(20, 12, 20, 20),
                    Text = "\u041e\u043f\u0435\u0440\u0430\u0446\u0438\u0438 \u0432\u0440\u0435\u043c\u0435\u043d\u043d\u043e \u043d\u0435\u0434\u043e\u0441\u0442\u0443\u043f\u043d\u044b."
                };

                panel.Controls.Add(detailsLabel);
                panel.Controls.Add(titleLabel);

                tableLayoutPanel1.Controls.Add(panel, 0, 0);
                tableLayoutPanel1.SetRowSpan(panel, 3);
                panel.BringToFront();

                _serverHardLockOverlayPanelMain = panel;
                _serverHardLockOverlayTitleMain = titleLabel;
                _serverHardLockOverlayDetailsMain = detailsLabel;
            }

            if (_serverHardLockOverlayPanelQueue == null && !scMain.Panel1.IsDisposed)
            {
                var queueOverlay = new Panel
                {
                    Name = "pnlServerHardLockQueue",
                    BackColor = ServerHardLockOverlayBackColor,
                    Dock = DockStyle.Fill,
                    Visible = false
                };

                var queueLabel = new Label
                {
                    Name = "lblServerHardLockQueue",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 12f, FontStyle.Regular, GraphicsUnit.Pixel),
                    ForeColor = Color.Firebrick,
                    Text = ServerHardLockTitle
                };

                queueOverlay.Controls.Add(queueLabel);
                scMain.Panel1.Controls.Add(queueOverlay);
                queueOverlay.BringToFront();
                _serverHardLockOverlayPanelQueue = queueOverlay;
            }
        }

        private string BuildServerUnavailableDetails(LanServerProbeSnapshot snapshot, bool probeInProgress)
        {
            var readyState = $"{snapshot.LiveStatus}/{snapshot.ReadyStatus}/{snapshot.SloStatus}";
            if (probeInProgress && snapshot.CompletedAtUtc <= DateTime.MinValue)
                return $"\u0418\u0434\u0451\u0442 \u043f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u0441\u0435\u0440\u0432\u0435\u0440\u0430 ({readyState}).";

            var lastReply = snapshot.CompletedAtUtc > DateTime.MinValue
                ? FormatLanProbeStamp(snapshot.CompletedAtUtc)
                : "\u043d/\u0434";

            var errorText = string.IsNullOrWhiteSpace(snapshot.Error)
                ? "\u043f\u0440\u0438\u0447\u0438\u043d\u0430 \u043d\u0435 \u043f\u043e\u043b\u0443\u0447\u0435\u043d\u0430"
                : snapshot.Error.Trim();

            return $"\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435 live/ready/slo: {readyState}. \u041f\u043e\u0441\u043b\u0435\u0434\u043d\u0438\u0439 \u043e\u0442\u0432\u0435\u0442: {lastReply}. \u041e\u0448\u0438\u0431\u043a\u0430: {errorText}";
        }
    }
}
