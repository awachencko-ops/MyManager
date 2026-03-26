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

        private void InitializeUserProfilePanel()
        {
            pnlUser.BackColor = UserProfilePanelBackColor;
            pnlUser.Padding = new Padding(12, 8, 12, 8);

            splitUser.BackColor = UserProfileCardBackColor;
            splitUser.IsSplitterFixed = true;
            splitUser.FixedPanel = FixedPanel.Panel1;
            splitUser.SplitterWidth = 1;
            splitUser.Panel1MinSize = 68;
            splitUser.SplitterDistance = 68;
            splitUser.TabStop = false;

            splitUser.Panel1.BackColor = UserProfileCardBackColor;
            splitUser.Panel2.BackColor = UserProfileCardBackColor;
            splitUser.Panel1.Padding = new Padding(12, 8, 6, 8);
            splitUser.Panel2.Padding = new Padding(0, 10, 14, 10);

            splitUser.Panel2.Paint -= splitServer_Panel2_Paint;
            splitUser.Panel2.Controls.Clear();

            pictureBox5.Dock = DockStyle.None;
            pictureBox5.Size = new Size(42, 42);
            pictureBox5.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox5.BackColor = Color.Transparent;
            ReplaceUserProfileIcon();

            splitUser.Panel1.Resize -= SplitUserPanel1_Resize;
            splitUser.Panel1.Resize += SplitUserPanel1_Resize;
            CenterUserProfileIcon();

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

            var textLayout = new TableLayoutPanel
            {
                Name = "tblUserProfileText",
                Dock = DockStyle.Fill,
                BackColor = UserProfileCardBackColor,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            textLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            textLayout.Controls.Add(_userProfileNameLabel, 0, 0);
            textLayout.Controls.Add(_userProfileRoleLabel, 0, 1);

            splitUser.Panel2.Controls.Add(textLayout);
            ApplyCurrentUserProfile(GetDefaultUserName(), _currentUserRoleText);
        }

        private void RefreshCurrentUserProfile(bool forceRefresh)
        {
            if (Disposing || IsDisposed)
                return;

            if (!ShouldUseLanRunApi() || _ordersStorageBackend != OrdersStorageMode.LanPostgreSql)
            {
                ApplyCurrentUserProfile(GetDefaultUserName(), "Локальный режим");
                return;
            }

            if (_currentUserProfileRefreshInProgress)
                return;

            if (!TryResolveLanApiBaseUri(_lanApiBaseUrl, out _))
            {
                ApplyCurrentUserProfile(GetDefaultUserName(), "API не настроен");
                return;
            }

            var actor = ResolveLanApiActor();
            var baseUrl = _lanApiBaseUrl;
            _currentUserProfileRefreshInProgress = true;
            _ = RefreshCurrentUserProfileCoreAsync(baseUrl, actor);
        }

        private async Task RefreshCurrentUserProfileCoreAsync(string apiBaseUrl, string actor)
        {
            LanApiIdentityResult result;
            try
            {
                result = await _lanApiIdentityService
                    .GetCurrentUserAsync(apiBaseUrl, actor)
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
            ApplyCurrentUserProfile(resolvedName, roleText);
        }

        private void ApplyCurrentUserProfile(string userName, string roleText)
        {
            _currentUserName = ResolveCurrentUserDisplayName(userName, GetDefaultUserName());
            _currentUserRoleText = string.IsNullOrWhiteSpace(roleText)
                ? "Права не определены"
                : roleText.Trim();

            if (_userProfileNameLabel != null)
                _userProfileNameLabel.Text = _currentUserName;

            if (_userProfileRoleLabel != null)
                _userProfileRoleLabel.Text = _currentUserRoleText;
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

        private void SplitUserPanel1_Resize(object? sender, EventArgs e)
        {
            CenterUserProfileIcon();
        }

        private void CenterUserProfileIcon()
        {
            var x = Math.Max(0, (splitUser.Panel1.ClientSize.Width - pictureBox5.Width) / 2);
            var y = Math.Max(0, (splitUser.Panel1.ClientSize.Height - pictureBox5.Height) / 2);
            pictureBox5.Location = new Point(x, y);
        }

        private void ReplaceUserProfileIcon()
        {
            var previousImage = pictureBox5.Image;
            pictureBox5.Image = CreateUserProfileIcon(42);

            if (previousImage != null && !ReferenceEquals(previousImage, pictureBox5.Image))
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
