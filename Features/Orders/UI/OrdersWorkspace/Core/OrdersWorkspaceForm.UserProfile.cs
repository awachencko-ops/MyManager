using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Svg;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private static readonly Color UserProfilePanelBackColor = Color.FromArgb(244, 245, 247);
        private static readonly Color UserProfileCardBackColor = Color.White;
        private static readonly Color UserProfileNameColor = Color.FromArgb(35, 35, 35);
        private static readonly Color UserProfileRoleColor = Color.FromArgb(120, 120, 120);
        private static readonly Color UserProfileAuthStateColor = Color.FromArgb(96, 96, 96);
        private static readonly Color UserProfileAuthHealthyColor = Color.FromArgb(46, 125, 50);
        private static readonly Color UserProfileSessionActionColor = Color.FromArgb(33, 98, 165);

        private void InitializeUserProfilePanel()
        {
            pnlUser.BackColor = UserProfilePanelBackColor;
            pnlUser.Padding = new Padding(12, 8, 12, 8);
            pnlPictureUser.BackColor = UserProfileCardBackColor;
            pnlInfoUser.BackColor = UserProfileCardBackColor;
            pnlPictureUser.Padding = new Padding(14, 14, 10, 14);
            pnlInfoUser.Padding = new Padding(2, 10, 14, 10);
            pnlInfoUser.Controls.Clear();

            pictureUser.Dock = DockStyle.Fill;
            pictureUser.SizeMode = PictureBoxSizeMode.Zoom;
            pictureUser.BackColor = Color.Transparent;
            ReplaceUserProfileIcon();

            _userProfileNameLabel = new Label
            {
                Name = "lblUserProfileName",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel),
                ForeColor = UserProfileNameColor,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Text = GetDefaultUserName()
            };

            _userProfileRoleLabel = new Label
            {
                Name = "lblUserProfileRole",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel),
                ForeColor = UserProfileRoleColor,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Text = _currentUserRoleText
            };

            _userProfileAuthStateLabel = new Label
            {
                Name = "lblUserProfileAuthState",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel),
                ForeColor = UserProfileAuthStateColor,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Text = _currentUserAuthStateText
            };

            _userProfileSessionActionLabel = new LinkLabel
            {
                Name = "lnkUserProfileSessionAction",
                Dock = DockStyle.Left,
                AutoSize = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                ActiveLinkColor = UserProfileSessionActionColor,
                LinkColor = UserProfileSessionActionColor,
                VisitedLinkColor = UserProfileSessionActionColor,
                Text = "Войти"
            };
            _userProfileSessionActionLabel.LinkClicked -= UserProfileSessionActionLabel_LinkClicked;
            _userProfileSessionActionLabel.LinkClicked += UserProfileSessionActionLabel_LinkClicked;

            var textLayout = new TableLayoutPanel
            {
                Name = "tblUserProfileText",
                Dock = DockStyle.Fill,
                BackColor = UserProfileCardBackColor,
                ColumnCount = 1,
                RowCount = 4,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            textLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 28f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 22f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 10f));
            textLayout.Controls.Add(_userProfileNameLabel, 0, 0);
            textLayout.Controls.Add(_userProfileRoleLabel, 0, 1);
            textLayout.Controls.Add(_userProfileAuthStateLabel, 0, 2);
            textLayout.Controls.Add(_userProfileSessionActionLabel, 0, 3);

            pnlInfoUser.Controls.Add(textLayout);
            ApplyCurrentUserProfile(GetDefaultUserName(), _currentUserRoleText, _currentUserAuthStateText, usesBearerSession: false);
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
            pictureUser.Image = CreateUserProfileIcon(42);

            if (previousImage != null && !ReferenceEquals(previousImage, pictureUser.Image))
                previousImage.Dispose();
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
                    svg.Width = iconSize;
                    svg.Height = iconSize;
                    var rendered = svg.Draw(iconSize, iconSize);
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
                    return new Bitmap(source, new Size(iconSize, iconSize));
                }
                catch
                {
                    // Fall back to generated avatar.
                }
            }

            return CreateFallbackUserProfileIcon(iconSize);
        }

        private static string[] ResolveUserProfileIconCandidates(string extension)
        {
            var fileName = $"account_circle_24dp_1F1F1F_FILL1_wght400_GRAD0_opsz24.{extension}";
            return
            [
                Path.Combine(AppContext.BaseDirectory, "Icons", "account circle", fileName),
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Icons", "account circle", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName)
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
