using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private const int UserProfileIconTargetSize = 60;
        private static readonly Color UserProfilePanelBackColor = QueuePanelBackColor;
        private static readonly Color UserProfileNameColor = QueueTextColor;
        private static readonly Color UserProfileRoleColor = Color.FromArgb(90, 96, 116);
        private static readonly Color UserProfileAuthStateColor = Color.FromArgb(96, 96, 96);
        private static readonly Color UserProfileAuthHealthyColor = Color.FromArgb(46, 125, 50);

        private void InitializeUserProfilePanel()
        {
            pnlUser.SuspendLayout();
            pnlInfoUser.SuspendLayout();

            pnlUser.BackColor = UserProfilePanelBackColor;
            pnlUser.Padding = Padding.Empty;
            pnlUser.Height = 90;
            pnlUser.MinimumSize = new Size(0, 90);

            pnlInfoUser.BackColor = Color.Transparent;
            pnlInfoUser.Dock = DockStyle.Fill;
            pnlInfoUser.Padding = Padding.Empty;
            pnlInfoUser.Margin = Padding.Empty;
            pictureUser.BackColor = Color.Transparent;
            pictureUser.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureUser.Resize -= PictureUser_Resize;
            pictureUser.Resize += PictureUser_Resize;

            _userProfileNameLabel = userNameLabel;
            _userProfileRoleLabel = statusUserLabel;
            _userProfileAuthStateLabel = null;
            _userProfileSessionActionLabel = null;

            userNameLabel.Name = "lblUserProfileName";
            userNameLabel.AutoSize = true;
            userNameLabel.ForeColor = UserProfileNameColor;
            userNameLabel.BackColor = Color.Transparent;
            userNameLabel.Text = GetDefaultUserName();

            statusUserLabel.Name = "lblUserProfileRole";
            statusUserLabel.AutoSize = true;
            statusUserLabel.ForeColor = UserProfileRoleColor;
            statusUserLabel.BackColor = Color.Transparent;
            statusUserLabel.Text = _currentUserRoleText;

            ReplaceUserProfileIcon();

            ApplyCurrentUserProfile(
                GetDefaultUserName(),
                _currentUserRoleText,
                _currentUserAuthStateText,
                usesBearerSession: false);

            pnlInfoUser.ResumeLayout(performLayout: true);
            pnlUser.ResumeLayout(performLayout: true);
        }

        private void PictureUser_Resize(object? sender, EventArgs e)
        {
            if (Disposing || IsDisposed)
                return;

            ReplaceUserProfileIcon();
        }

        private void RefreshCurrentUserProfile(bool forceRefresh)
        {
            if (Disposing || IsDisposed)
                return;

            if (!ShouldUseLanRunApi() || _ordersStorageBackend != OrdersStorageMode.LanPostgreSql)
            {
                ApplyCurrentUserProfile(GetDefaultUserName(), "Локальный режим", "Сессия: не используется", usesBearerSession: false);
                return;
            }

            if (_currentUserProfileRefreshInProgress)
                return;

            if (!TryResolveLanApiBaseUri(_lanApiBaseUrl, out _))
            {
                ApplyCurrentUserProfile(GetDefaultUserName(), "API не настроен", "Сессия: API не настроен", usesBearerSession: false);
                return;
            }

            var actor = ResolveLanApiActor();
            var baseUrl = _lanApiBaseUrl;
            var allowSessionBootstrap = !_suppressAutoSessionBootstrap;
            _currentUserProfileRefreshInProgress = true;
            _ = RefreshCurrentUserProfileCoreAsync(baseUrl, actor, allowSessionBootstrap);
        }

        private async Task RefreshCurrentUserProfileCoreAsync(string apiBaseUrl, string actor, bool allowSessionBootstrap)
        {
            LanApiIdentityResult result;
            try
            {
                result = await _lanApiIdentityService
                    .GetCurrentUserAsync(apiBaseUrl, actor, allowSessionBootstrap: allowSessionBootstrap)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = LanApiIdentityResult.Failed(ex.Message);
            }

            RunOnUiThread(() =>
            {
                _currentUserProfileRefreshInProgress = false;
                ApplyCurrentUserProfileResult(actor, result);
            });
        }

        private void ApplyCurrentUserProfileResult(string fallbackActor, LanApiIdentityResult result)
        {
            var resolvedName = ResolveCurrentUserDisplayName(result.User?.Name, fallbackActor);
            var roleText = ResolveCurrentUserRoleText(result);
            var authStateText = ResolveCurrentUserAuthStateText(result);
            var usesBearerSession = ResolveBearerSessionState(result);
            if (usesBearerSession)
                _suppressAutoSessionBootstrap = false;

            ApplyCurrentUserProfile(resolvedName, roleText, authStateText, usesBearerSession);
        }

        private void ApplyCurrentUserProfile(string userName, string roleText, string authStateText, bool usesBearerSession)
        {
            _currentUserName = ResolveCurrentUserDisplayName(userName, GetDefaultUserName());
            _currentUserRoleText = string.IsNullOrWhiteSpace(roleText)
                ? "Права не определены"
                : roleText.Trim();
            _currentUserAuthStateText = string.IsNullOrWhiteSpace(authStateText)
                ? "Сессия не определена"
                : authStateText.Trim();
            _currentUserUsesBearerSession = usesBearerSession;

            if (_userProfileNameLabel != null)
                _userProfileNameLabel.Text = _currentUserName;

            if (_userProfileRoleLabel != null)
                _userProfileRoleLabel.Text = _currentUserRoleText;

            if (_userProfileAuthStateLabel != null)
            {
                _userProfileAuthStateLabel.Text = _currentUserAuthStateText;
                _userProfileAuthStateLabel.ForeColor = usesBearerSession
                    ? UserProfileAuthHealthyColor
                    : UserProfileAuthStateColor;
            }

            UpdateUserProfileSessionActionControl();
            UpdateUserProfileToolTip();
        }

        private string ResolveCurrentUserDisplayName(string? preferredName, string? fallbackName)
        {
            var candidate = string.IsNullOrWhiteSpace(preferredName)
                ? fallbackName
                : preferredName;

            if (string.IsNullOrWhiteSpace(candidate))
                return GetDefaultUserName();

            var normalized = candidate.Trim();
            foreach (var configuredUser in _filterUsers)
            {
                if (string.Equals(configuredUser, normalized, StringComparison.OrdinalIgnoreCase))
                    return configuredUser;
            }

            return normalized;
        }

        private static string ResolveCurrentUserRoleText(LanApiIdentityResult result)
        {
            if (result.IsSuccess && result.User != null)
            {
                if (result.User.CanManageUsers || string.Equals(result.User.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                    return "Администратор";

                if (string.Equals(result.User.Role, "Operator", StringComparison.OrdinalIgnoreCase))
                    return "Оператор";

                return string.IsNullOrWhiteSpace(result.User.Role)
                    ? "Права не определены"
                    : result.User.Role.Trim();
            }

            if (result.IsUnauthorized)
                return "Не авторизован";

            if (result.IsForbidden)
                return "Доступ запрещен";

            if (result.IsUnavailable)
                return "API недоступен";

            return "Права не определены";
        }

        private static string ResolveCurrentUserAuthStateText(LanApiIdentityResult result)
        {
            if (result.IsSuccess && result.User != null)
            {
                var scheme = string.IsNullOrWhiteSpace(result.User.AuthScheme)
                    ? "Unknown"
                    : result.User.AuthScheme.Trim();
                return $"Сессия: {scheme}";
            }

            if (result.IsUnauthorized)
                return "Сессия: не авторизован";

            if (result.IsForbidden)
                return "Сессия: доступ ограничен";

            if (result.IsUnavailable)
                return "Сессия: API недоступен";

            return "Сессия: не определена";
        }

        private static bool ResolveBearerSessionState(LanApiIdentityResult result)
        {
            return result.IsSuccess
                   && result.User != null
                   && string.Equals(result.User.AuthScheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                   && !string.IsNullOrWhiteSpace(result.User.SessionId);
        }

        private async void UserProfileSessionActionLabel_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
        {
            if (Disposing || IsDisposed || _currentUserSessionActionInProgress)
                return;

            if (!ShouldUseLanRunApi() || _ordersStorageBackend != OrdersStorageMode.LanPostgreSql)
                return;

            _currentUserSessionActionInProgress = true;
            UpdateUserProfileSessionActionControl();

            try
            {
                if (_currentUserUsesBearerSession)
                {
                    SetBottomStatus("Завершаем сессию...");
                    var logoutResult = await _lanApiIdentityService
                        .LogoutAsync(_lanApiBaseUrl, ResolveLanApiActor())
                        .ConfigureAwait(true);

                    _suppressAutoSessionBootstrap = true;
                    if (logoutResult.IsSuccess)
                        SetBottomStatus("Сессия завершена. Нажмите \"Войти\", чтобы получить новый токен.");
                    else if (logoutResult.IsUnavailable)
                        SetBottomStatus("Сессия локально завершена, API временно недоступен.");
                    else
                        SetBottomStatus($"Завершение сессии выполнено с предупреждением: {logoutResult.Error}");
                }
                else
                {
                    _suppressAutoSessionBootstrap = false;
                    SetBottomStatus("Выполняем вход...");
                }

                RefreshCurrentUserProfile(forceRefresh: true);
            }
            finally
            {
                _currentUserSessionActionInProgress = false;
                UpdateUserProfileSessionActionControl();
            }
        }

        private void UpdateUserProfileSessionActionControl()
        {
            if (_userProfileSessionActionLabel == null)
                return;

            var isLanMode = ShouldUseLanRunApi() && _ordersStorageBackend == OrdersStorageMode.LanPostgreSql;
            _userProfileSessionActionLabel.Visible = isLanMode;
            _userProfileSessionActionLabel.Enabled = isLanMode && !_currentUserSessionActionInProgress;
            _userProfileSessionActionLabel.Text = _currentUserUsesBearerSession ? "Выйти" : "Войти";
        }

        private void ReplaceUserProfileIcon()
        {
            var previousImage = pictureUser.Image;
            pictureUser.Image = CreateUserProfileIcon(GetUserProfileIconSize());

            if (previousImage != null && !ReferenceEquals(previousImage, pictureUser.Image))
                previousImage.Dispose();
        }

        private int GetUserProfileIconSize()
        {
            var width = pictureUser.ClientSize.Width > 0
                ? pictureUser.ClientSize.Width
                : pictureUser.Width;
            var height = pictureUser.ClientSize.Height > 0
                ? pictureUser.ClientSize.Height
                : pictureUser.Height;

            var availableWidth = Math.Max(32, width);
            var availableHeight = Math.Max(32, height);
            return Math.Max(40, Math.Min(UserProfileIconTargetSize, Math.Min(availableWidth, availableHeight)));
        }

        private void UpdateUserProfileToolTip()
        {
            var toolTipText = string.Join(
                Environment.NewLine,
                [
                    _currentUserName,
                    _currentUserRoleText,
                    _currentUserAuthStateText
                ]);

            _dockToolTip.SetToolTip(pnlUser, toolTipText);
            _dockToolTip.SetToolTip(pnlInfoUser, toolTipText);
            _dockToolTip.SetToolTip(pictureUser, toolTipText);

            if (_userProfileNameLabel != null)
                _dockToolTip.SetToolTip(_userProfileNameLabel, toolTipText);

            if (_userProfileRoleLabel != null)
                _dockToolTip.SetToolTip(_userProfileRoleLabel, toolTipText);

            if (_userProfileAuthStateLabel != null)
                _dockToolTip.SetToolTip(_userProfileAuthStateLabel, toolTipText);
        }

        private static Image CreateUserProfileIcon(int iconSize)
        {
            foreach (var candidate in ResolveUserProfileIconCandidates("svg"))
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    var svg = SvgDocument.Open<SvgDocument>(candidate);
                    var rendered = RenderSvgAvatar(svg, iconSize);
                    if (rendered != null)
                        return rendered;
                }
                catch
                {
                    // Fall back to PNG or a generated avatar.
                }
            }

            foreach (var candidate in ResolveUserProfileIconCandidates("png"))
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    using var source = Image.FromFile(candidate);
                    return DownsampleImage(source, iconSize, iconSize);
                }
                catch
                {
                    // Fall back to generated avatar.
                }
            }

            return CreateFallbackUserProfileIcon(iconSize);
        }

        private static Bitmap? RenderSvgAvatar(SvgDocument svg, int targetSize)
        {
            var renderSize = Math.Max(targetSize * 4, 128);
            svg.Width = renderSize;
            svg.Height = renderSize;
            using var rendered = svg.Draw(renderSize, renderSize);
            if (rendered == null)
                return null;

            return DownsampleImage(rendered, targetSize, targetSize);
        }

        private static Bitmap DownsampleImage(Image source, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);
            return bitmap;
        }

        private static string[] ResolveUserProfileIconCandidates(string extension)
        {
            var primaryFileName = $"account_circle_24dp_1F1F1F_FILL1_wght400_GRAD0_opsz24.{extension}";
            var fallbackFileName = $"account_circle_60dp_1F1F1F_FILL1_wght400_GRAD0_opsz48.{extension}";
            return
            [
                Path.Combine(AppContext.BaseDirectory, "Icons", "account circle", primaryFileName),
                Path.Combine(AppContext.BaseDirectory, primaryFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Icons", "account circle", primaryFileName),
                Path.Combine(Directory.GetCurrentDirectory(), primaryFileName),
                Path.Combine(AppContext.BaseDirectory, "Icons", "account circle", fallbackFileName),
                Path.Combine(AppContext.BaseDirectory, fallbackFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Icons", "account circle", fallbackFileName),
                Path.Combine(Directory.GetCurrentDirectory(), fallbackFileName)
            ];
        }

        private static Bitmap CreateFallbackUserProfileIcon(int iconSize)
        {
            var bitmap = new Bitmap(iconSize, iconSize);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var outerBrush = new SolidBrush(Color.FromArgb(232, 234, 237));
            using var innerBrush = new SolidBrush(Color.FromArgb(110, 117, 125));

            graphics.FillEllipse(outerBrush, 0, 0, iconSize - 1, iconSize - 1);

            var headSize = (int)Math.Round(iconSize * 0.32);
            var headX = (iconSize - headSize) / 2f;
            var headY = (float)Math.Round(iconSize * 0.18);
            graphics.FillEllipse(innerBrush, headX, headY, headSize, headSize);

            var bodyWidth = (float)Math.Round(iconSize * 0.56);
            var bodyHeight = (float)Math.Round(iconSize * 0.28);
            var bodyX = (iconSize - bodyWidth) / 2f;
            var bodyY = (float)Math.Round(iconSize * 0.56);
            using var bodyPath = new GraphicsPath();
            bodyPath.AddArc(bodyX, bodyY, bodyWidth, bodyHeight, 180, 180);
            bodyPath.AddLine(bodyX + bodyWidth, bodyY + bodyHeight / 2f, bodyX + bodyWidth, iconSize - 6);
            bodyPath.AddLine(bodyX + bodyWidth, iconSize - 6, bodyX, iconSize - 6);
            bodyPath.AddLine(bodyX, iconSize - 6, bodyX, bodyY + bodyHeight / 2f);
            bodyPath.CloseFigure();
            graphics.FillPath(innerBrush, bodyPath);

            return bitmap;
        }
    }
}
