using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Manina.Windows.Forms;
using PdfiumViewer;
using Svg;

namespace MyManager
{
    public partial class MainForm
    {
        private static readonly FieldInfo? ImageListViewDefaultAdaptorField = typeof(ImageListView).GetField(
            "defaultAdaptor",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private void InitializeOrdersTilesView()
        {
            _lvPrintTiles.Dock = DockStyle.Fill;
            _lvPrintTiles.Margin = dgvJobs.Margin;
            _lvPrintTiles.BackColor = dgvJobs.BackgroundColor;
            _lvPrintTiles.BorderStyle = BorderStyle.None;
            _lvPrintTiles.MultiSelect = true;
            _lvPrintTiles.ScrollBars = true;
            _lvPrintTiles.View = Manina.Windows.Forms.View.Thumbnails;
            _lvPrintTiles.ThumbnailSize = new Size(120, 120);
            _lvPrintTiles.AllowDrag = false;
            _lvPrintTiles.ShowFileIcons = false;
            _lvPrintTiles.UseEmbeddedThumbnails = UseEmbeddedThumbnails.Never;
            _lvPrintTiles.CacheMode = CacheMode.Continuous;
            _lvPrintTiles.PersistentCacheDirectory = _printTilesCacheFolderPath;
            _lvPrintTiles.PersistentCacheSize = 512L * 1024 * 1024;
            Directory.CreateDirectory(_printTilesCacheFolderPath);
            TryEnablePdfThumbnailAdaptor();
            _lvPrintTiles.SetRenderer(new SimpleTilesRenderer(OrdersRowSelectedBackColor));
            _lvPrintTiles.Visible = false;
            _lvPrintTiles.SelectionChanged += LvPrintTiles_SelectedIndexChanged;
            _lvPrintTiles.DoubleClick += LvPrintTiles_ItemActivate;
            _lvPrintTiles.MouseUp += LvPrintTiles_MouseUp;
            _printTileOrderFont = new Font(_lvPrintTiles.Font, FontStyle.Bold);

            tableLayoutPanel1.Controls.Add(_lvPrintTiles, 0, 2);
            _lvPrintTiles.BringToFront();
        }

        private void TryEnablePdfThumbnailAdaptor()
        {
            if (ImageListViewDefaultAdaptorField == null)
            {
                Logger.Warn("TILES | ImageListView default adaptor field not found. PDF preview falls back to shell icons.");
                return;
            }

            try
            {
                ImageListViewDefaultAdaptorField.SetValue(_lvPrintTiles, new PdfAwareFileSystemAdaptor());
            }
            catch (Exception ex)
            {
                Logger.Warn($"TILES | Failed to set PDF thumbnail adaptor: {ex.Message}");
            }
        }

        private void InitializeViewModeSwitches()
        {
            btnViewList.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnViewTiles.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnViewList.Click += (_, _) => SetOrdersViewMode(OrdersViewMode.List);
            btnViewTiles.Click += (_, _) => SetOrdersViewMode(OrdersViewMode.Tiles);
            UpdateViewModeSwitchesVisuals();
        }

        private void SetOrdersViewMode(OrdersViewMode mode)
        {
            _ordersViewMode = mode;

            var isTilesMode = _ordersViewMode == OrdersViewMode.Tiles;
            RefreshPrintTilesFromVisibleRows();
            dgvJobs.Visible = !isTilesMode;
            _lvPrintTiles.Visible = isTilesMode;

            if (isTilesMode)
                SyncGridSelectionWithTiles();
            else
                SyncTilesSelectionWithGrid();

            UpdateViewModeSwitchesVisuals();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void UpdateViewModeSwitchesVisuals()
        {
            var listModeActive = _ordersViewMode == OrdersViewMode.List;
            var activeBackColor = Color.FromArgb(229, 236, 247);
            var inactiveBackColor = SystemColors.Control;

            btnViewList.UseVisualStyleBackColor = false;
            btnViewTiles.UseVisualStyleBackColor = false;
            btnViewList.BackColor = listModeActive ? activeBackColor : inactiveBackColor;
            btnViewTiles.BackColor = listModeActive ? inactiveBackColor : activeBackColor;
            btnViewList.Enabled = !listModeActive;
            btnViewTiles.Enabled = listModeActive;
        }

        private void LvPrintTiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isSyncingTileSelection)
                return;

            SyncGridSelectionWithTiles();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void LvPrintTiles_ItemActivate(object? sender, EventArgs e)
        {
            var selectedTile = GetSelectedPrintTileTag();
            if (selectedTile == null || !HasExistingFile(selectedTile.PrintPath))
                return;

            OpenFileDefault(selectedTile.PrintPath);
        }

        private void LvPrintTiles_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            _lvPrintTiles.HitTest(e.Location, out var hit);
            if (!hit.ItemHit || hit.ItemIndex < 0)
                return;

            var hitItem = _lvPrintTiles.Items[hit.ItemIndex];
            if (hitItem == null || hitItem.Tag is not PrintTileTag tileTag)
                return;

            if (!hitItem.Selected)
            {
                _isSyncingTileSelection = true;
                try
                {
                    _lvPrintTiles.ClearSelection();
                    hitItem.Selected = true;
                    _lvPrintTiles.Items.FocusedItem = hitItem;
                }
                finally
                {
                    _isSyncingTileSelection = false;
                }

                SyncGridSelectionWithTiles();
                UpdateActionButtonsState();
                UpdateTrayStatsIndicator();
            }

            var order = FindOrderByInternalId(tileTag.OrderInternalId);
            if (order == null)
                return;

            ShowPrintTileContextMenu(order, tileTag, e.Location);
        }

        private void ClearTilesSelectionAndSync()
        {
            _isSyncingTileSelection = true;
            try
            {
                _lvPrintTiles.ClearSelection();
                _lvPrintTiles.Items.FocusedItem = null;
            }
            finally
            {
                _isSyncingTileSelection = false;
            }

            ClearGridSelection();
            UpdateActionButtonsState();
            UpdateTrayStatsIndicator();
        }

        private void ShowPrintTileContextMenu(OrderData order, PrintTileTag tileTag, Point location)
        {
            _printTilesContextMenu.Items.Clear();
            AddPrintTileMenuItem("📁 Открыть папку", () => OpenPrintTileFolder(order, tileTag));
            _printTilesContextMenu.Items.Add(new ToolStripSeparator());
            AddPrintTileMenuItem("✏️ Переименовать файл", () => RenamePrintTileFile(order, tileTag));
            AddPrintTileMenuItem("📋 Копировать путь в буфер", () => CopyExistingPathToClipboard(tileTag.PrintPath));
            _printTilesContextMenu.Show(_lvPrintTiles, location);
        }

        private void AddPrintTileMenuItem(string text, Action onClick)
        {
            var menuItem = new ToolStripMenuItem(text);
            menuItem.Click += (_, _) => onClick();
            _printTilesContextMenu.Items.Add(menuItem);
        }

        private void OpenPrintTileFolder(OrderData order, PrintTileTag tileTag)
        {
            var printPath = CleanPath(tileTag.PrintPath);
            if (HasExistingFile(printPath))
            {
                var folderPath = Path.GetDirectoryName(printPath);
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true
                    });
                    SetBottomStatus($"Открыта папка этапа: {folderPath}");
                    return;
                }
            }

            OpenOrderStageFolder(order, 3);
        }

        private void RenamePrintTileFile(OrderData order, PrintTileTag tileTag)
        {
            var currentPath = CleanPath(tileTag.PrintPath);
            if (!HasExistingFile(currentPath))
            {
                SetBottomStatus("Путь к файлу не найден");
                MessageBox.Show(this, "Путь к файлу не найден.", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryBuildRenamedPath(currentPath, out var renamedPath))
                return;

            try
            {
                File.Move(currentPath, renamedPath);
            }
            catch (Exception ex)
            {
                SetBottomStatus($"Не удалось переименовать файл: {ex.Message}");
                MessageBox.Show(this, $"Не удалось переименовать файл: {ex.Message}", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdatePrintPathReferencesForOrder(order, currentPath, renamedPath);
            RemovePrintTileImageIndex(currentPath);
            RemovePrintTileImageIndex(renamedPath);
            PersistGridChanges($"order|{order.InternalId}");
            SetBottomStatus("Файл переименован");
        }

        private void UpdatePrintPathReferencesForOrder(OrderData order, string oldPath, string newPath)
        {
            var hasUpdated = false;

            if (PathsEqual(order.PrintPath, oldPath))
            {
                order.PrintPath = newPath;
                hasUpdated = true;
            }

            if (order.Items != null)
            {
                foreach (var item in order.Items)
                {
                    if (item == null || !PathsEqual(item.PrintPath, oldPath))
                        continue;

                    item.PrintPath = newPath;
                    hasUpdated = true;
                }
            }

            if (!hasUpdated)
                order.PrintPath = newPath;
        }

        private void LvPrintTiles_DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            var bounds = e.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var isSelected = e.Item.Selected;
            var itemBackColor = isSelected ? OrdersRowSelectedBackColor : _lvPrintTiles.BackColor;
            var textColor = Color.FromArgb(24, 28, 36);

            using (var backBrush = new SolidBrush(itemBackColor))
                e.Graphics.FillRectangle(backBrush, bounds);

            var tileTag = e.Item.Tag as PrintTileTag;
            var orderNumber = string.IsNullOrWhiteSpace(tileTag?.OrderNumber) ? "—" : tileTag.OrderNumber.Trim();
            var fileName = string.IsNullOrWhiteSpace(tileTag?.PrintFileName) ? e.Item.Text : tileTag.PrintFileName.Trim();

            const int outerPadding = 8;
            const int textSpacing = 4;
            var contentRect = Rectangle.Inflate(bounds, -outerPadding, -outerPadding);
            if (contentRect.Width <= 0 || contentRect.Height <= 0)
                return;

            var orderFont = _printTileOrderFont ?? _lvPrintTiles.Font;
            var fileFont = _lvPrintTiles.Font;
            var orderTextHeight = TextRenderer.MeasureText(e.Graphics, "Hg", orderFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
            var fileTextHeight = TextRenderer.MeasureText(e.Graphics, "Hg", fileFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
            var textTotalHeight = orderTextHeight + textSpacing + fileTextHeight;

            var availablePreviewHeight = contentRect.Height - textTotalHeight - textSpacing;
            var frameSize = Math.Min(contentRect.Width, availablePreviewHeight);
            if (frameSize < 42)
                frameSize = Math.Max(42, Math.Min(contentRect.Width, contentRect.Height));

            var frameRect = new Rectangle(
                x: contentRect.Left + (contentRect.Width - frameSize) / 2,
                y: contentRect.Top,
                width: frameSize,
                height: frameSize);

            using (var frameBackBrush = new SolidBrush(Color.White))
                e.Graphics.FillRectangle(frameBackBrush, frameRect);
            using (var frameBorderPen = new Pen(Color.FromArgb(195, 203, 216)))
                e.Graphics.DrawRectangle(frameBorderPen, frameRect);

            var image = GetPrintTileImage(e.Item);
            if (image != null)
            {
                var imageBounds = Rectangle.Inflate(frameRect, -4, -4);
                var drawRect = FitImageToBounds(image.Size, imageBounds);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(image, drawRect);
            }

            var textRectTop = frameRect.Bottom + textSpacing;
            var textRectWidth = contentRect.Width;
            var orderRect = new Rectangle(contentRect.Left, textRectTop, textRectWidth, orderTextHeight);
            var fileRect = new Rectangle(contentRect.Left, orderRect.Bottom + textSpacing, textRectWidth, fileTextHeight);
            var textFlags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

            TextRenderer.DrawText(e.Graphics, orderNumber, orderFont, orderRect, textColor, textFlags);
            TextRenderer.DrawText(e.Graphics, fileName, fileFont, fileRect, textColor, textFlags);
        }

        private void RefreshPrintTilesFromVisibleRows()
        {
            var selectedOrderInternalIds = _ordersViewMode == OrdersViewMode.Tiles
                ? GetSelectedOrderInternalIdsFromTiles()
                : GetSelectedOrderInternalIdsFromGrid();

            if (selectedOrderInternalIds.Count == 0)
                selectedOrderInternalIds = GetSelectedOrderInternalIdsFromGrid();

            var preferredOrderInternalId = GetFocusedPrintTileOrderInternalId();
            if (string.IsNullOrWhiteSpace(preferredOrderInternalId))
                preferredOrderInternalId = ExtractOrderInternalIdFromTag(dgvJobs.CurrentRow?.Tag?.ToString());

            _lvPrintTiles.SuspendLayout();
            _isSyncingTileSelection = true;

            try
            {
                _lvPrintTiles.Items.Clear();

                foreach (DataGridViewRow row in dgvJobs.Rows)
                {
                    if (row.IsNewRow || !row.Visible)
                        continue;

                    var rowTag = row.Tag?.ToString();
                    if (!IsOrderTag(rowTag))
                        continue;

                    var orderInternalId = ExtractOrderInternalIdFromTag(rowTag);
                    if (string.IsNullOrWhiteSpace(orderInternalId))
                        continue;

                    var order = FindOrderByInternalId(orderInternalId);
                    if (order == null)
                        continue;

                    var printPath = ResolveSingleOrderDisplayPath(order, 3);
                    if (!HasExistingFile(printPath))
                        continue;

                    var printFileName = row.Cells[colPrint.Index].Value?.ToString();
                    if (string.IsNullOrWhiteSpace(printFileName) || string.Equals(printFileName, "...", StringComparison.Ordinal))
                        printFileName = Path.GetFileName(printPath);

                    if (string.IsNullOrWhiteSpace(printFileName))
                        continue;

                    var orderNumber = row.Cells[colOrderNumber.Index].Value?.ToString();
                    if (string.IsNullOrWhiteSpace(orderNumber))
                        orderNumber = GetOrderDisplayId(order);

                    var cleanOrderNumber = orderNumber?.Trim();
                    if (string.IsNullOrWhiteSpace(cleanOrderNumber))
                        cleanOrderNumber = "—";

                    var cleanPrintFileName = printFileName.Trim();
                    var item = new ImageListViewItem(printPath)
                    {
                        Text = cleanPrintFileName,
                        Tag = new PrintTileTag(orderInternalId, cleanOrderNumber, printPath, cleanPrintFileName)
                    };

                    _lvPrintTiles.Items.Add(item);
                }

            }
            finally
            {
                _isSyncingTileSelection = false;
                _lvPrintTiles.ResumeLayout(false);
            }

            ApplyTileSelectionByOrderInternalIds(
                selectedOrderInternalIds,
                preferredOrderInternalId,
                ensureVisible: _ordersViewMode == OrdersViewMode.Tiles);

            if (_ordersViewMode == OrdersViewMode.Tiles)
                SyncGridSelectionWithTiles();
        }

        private bool TrySelectTileByOrderInternalId(string? orderInternalId)
        {
            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(orderInternalId))
                selectedOrderIds.Add(orderInternalId);

            ApplyTileSelectionByOrderInternalIds(selectedOrderIds, orderInternalId, ensureVisible: true);
            return _lvPrintTiles.SelectedItems.Count > 0;
        }

        private PrintTileTag? GetSelectedPrintTileTag()
        {
            return _lvPrintTiles.SelectedItems.Count > 0
                ? _lvPrintTiles.SelectedItems[0].Tag as PrintTileTag
                : null;
        }

        private string? GetFocusedPrintTileOrderInternalId()
        {
            return _lvPrintTiles.Items.FocusedItem?.Tag is PrintTileTag focusedTileTag
                ? focusedTileTag.OrderInternalId
                : null;
        }

        private HashSet<string> GetSelectedOrderInternalIdsFromGrid()
        {
            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataGridViewRow row in dgvJobs.SelectedRows)
            {
                if (row.IsNewRow)
                    continue;

                var orderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                if (!string.IsNullOrWhiteSpace(orderInternalId))
                    selectedOrderIds.Add(orderInternalId);
            }

            return selectedOrderIds;
        }

        private HashSet<string> GetSelectedOrderInternalIdsFromTiles()
        {
            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ImageListViewItem item in _lvPrintTiles.SelectedItems)
            {
                if (item.Tag is not PrintTileTag tileTag)
                    continue;

                if (!string.IsNullOrWhiteSpace(tileTag.OrderInternalId))
                    selectedOrderIds.Add(tileTag.OrderInternalId);
            }

            return selectedOrderIds;
        }

        private void SyncTilesSelectionWithGrid()
        {
            if (_isSyncingGridSelection)
                return;

            var selectedOrderIds = GetSelectedOrderInternalIdsFromGrid();
            var preferredOrderInternalId = ExtractOrderInternalIdFromTag(dgvJobs.CurrentRow?.Tag?.ToString());
            ApplyTileSelectionByOrderInternalIds(selectedOrderIds, preferredOrderInternalId, ensureVisible: false);
        }

        private void SyncGridSelectionWithTiles()
        {
            if (_isSyncingTileSelection)
                return;

            var selectedOrderIds = GetSelectedOrderInternalIdsFromTiles();
            var preferredOrderInternalId = GetFocusedPrintTileOrderInternalId();
            if (string.IsNullOrWhiteSpace(preferredOrderInternalId))
                preferredOrderInternalId = selectedOrderIds.FirstOrDefault();

            ApplyGridSelectionByOrderInternalIds(selectedOrderIds, preferredOrderInternalId);
        }

        private void ApplyTileSelectionByOrderInternalIds(
            ISet<string> selectedOrderInternalIds,
            string? preferredOrderInternalId,
            bool ensureVisible)
        {
            _isSyncingTileSelection = true;
            _lvPrintTiles.SuspendLayout();

            try
            {
                _lvPrintTiles.ClearSelection();
                if (selectedOrderInternalIds.Count == 0)
                    return;

                ImageListViewItem? firstSelectedItem = null;
                ImageListViewItem? preferredSelectedItem = null;

                foreach (ImageListViewItem item in _lvPrintTiles.Items)
                {
                    if (item.Tag is not PrintTileTag tileTag)
                        continue;

                    if (!selectedOrderInternalIds.Contains(tileTag.OrderInternalId))
                        continue;

                    item.Selected = true;
                    firstSelectedItem ??= item;

                    if (!string.IsNullOrWhiteSpace(preferredOrderInternalId)
                        && string.Equals(tileTag.OrderInternalId, preferredOrderInternalId, StringComparison.Ordinal))
                    {
                        preferredSelectedItem = item;
                    }
                }

                var itemToFocus = preferredSelectedItem ?? firstSelectedItem;
                if (itemToFocus != null)
                {
                    _lvPrintTiles.Items.FocusedItem = itemToFocus;
                    if (ensureVisible)
                        _lvPrintTiles.EnsureVisible(itemToFocus.Index);
                }
            }
            finally
            {
                _lvPrintTiles.ResumeLayout(false);
                _isSyncingTileSelection = false;
            }
        }

        private bool ApplyGridSelectionByOrderInternalIds(
            ISet<string> selectedOrderInternalIds,
            string? preferredOrderInternalId)
        {
            _isSyncingGridSelection = true;

            try
            {
                dgvJobs.ClearSelection();

                if (selectedOrderInternalIds.Count == 0)
                {
                    dgvJobs.CurrentCell = null;
                    return false;
                }

                var targetColumnIndex = colPrint.Index >= 0 ? colPrint.Index : colStatus.Index;
                DataGridViewRow? firstSelectedRow = null;
                DataGridViewRow? preferredSelectedRow = null;

                foreach (DataGridViewRow row in dgvJobs.Rows)
                {
                    if (row.IsNewRow || !row.Visible)
                        continue;

                    var rowOrderInternalId = ExtractOrderInternalIdFromTag(row.Tag?.ToString());
                    if (string.IsNullOrWhiteSpace(rowOrderInternalId))
                        continue;

                    if (!selectedOrderInternalIds.Contains(rowOrderInternalId))
                        continue;

                    row.Selected = true;
                    firstSelectedRow ??= row;

                    if (!string.IsNullOrWhiteSpace(preferredOrderInternalId)
                        && string.Equals(rowOrderInternalId, preferredOrderInternalId, StringComparison.Ordinal))
                    {
                        preferredSelectedRow = row;
                    }
                }

                var rowToFocus = preferredSelectedRow ?? firstSelectedRow;
                if (rowToFocus == null)
                {
                    dgvJobs.CurrentCell = null;
                    return false;
                }

                dgvJobs.CurrentCell = rowToFocus.Cells[targetColumnIndex];
                return true;
            }
            finally
            {
                _isSyncingGridSelection = false;
            }
        }

        private bool TrySelectGridRowByOrderInternalId(string? orderInternalId)
        {
            if (string.IsNullOrWhiteSpace(orderInternalId))
                return false;

            var selectedOrderIds = new HashSet<string>(StringComparer.Ordinal) { orderInternalId };
            return ApplyGridSelectionByOrderInternalIds(selectedOrderIds, orderInternalId);
        }

        private void ClearGridSelection()
        {
            _isSyncingGridSelection = true;
            try
            {
                dgvJobs.ClearSelection();
                dgvJobs.CurrentCell = null;
            }
            finally
            {
                _isSyncingGridSelection = false;
            }
        }

        private int GetOrCreatePrintTileImageIndex(string printPath)
        {
            var extensionKey = Path.GetExtension(printPath);
            if (string.IsNullOrWhiteSpace(extensionKey))
                extensionKey = "__default";

            if (_printTileImageIndexesByExtension.TryGetValue(extensionKey, out var existingIndex))
                return existingIndex;

            var tileImage = CreatePrintTilePlaceholderImage(_printTilesImageList.ImageSize, extensionKey);

            if (HasExistingFile(printPath))
            {
                try
                {
                    using var icon = Icon.ExtractAssociatedIcon(printPath);
                    if (icon != null)
                    {
                        using var iconBitmap = icon.ToBitmap();
                        using var graphics = Graphics.FromImage(tileImage);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        var iconBounds = new Rectangle(24, 14, 48, 48);
                        graphics.DrawImage(iconBitmap, iconBounds);
                    }
                }
                catch
                {
                    // Фоллбеком остается placeholder.
                }
            }

            _printTilesImageList.Images.Add(tileImage);

            var createdIndex = _printTilesImageList.Images.Count - 1;
            _printTileImageIndexesByExtension[extensionKey] = createdIndex;
            return createdIndex;
        }

        private int ResolvePrintTileImageIndex(string printPath)
        {
            if (TryGetPrintTileImageIndex(printPath, out var imageIndexByPath))
                return imageIndexByPath;

            return GetOrCreatePrintTileImageIndex(printPath);
        }

        private static string GetPrintTileImageIndexKey(string printPath)
        {
            return string.IsNullOrWhiteSpace(printPath) ? string.Empty : NormalizePath(printPath);
        }

        private bool HasPrintTileImageIndex(string printPath)
        {
            var cacheKey = GetPrintTileImageIndexKey(printPath);
            return !string.IsNullOrWhiteSpace(cacheKey) && _printTileImageIndexesByPath.ContainsKey(cacheKey);
        }

        private bool TryGetPrintTileImageIndex(string printPath, out int imageIndex)
        {
            var cacheKey = GetPrintTileImageIndexKey(printPath);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                imageIndex = -1;
                return false;
            }

            return _printTileImageIndexesByPath.TryGetValue(cacheKey, out imageIndex);
        }

        private void SetPrintTileImageIndex(string printPath, int imageIndex)
        {
            var cacheKey = GetPrintTileImageIndexKey(printPath);
            if (string.IsNullOrWhiteSpace(cacheKey))
                return;

            _printTileImageIndexesByPath[cacheKey] = imageIndex;
        }

        private void RemovePrintTileImageIndex(string printPath)
        {
            var cacheKey = GetPrintTileImageIndexKey(printPath);
            if (string.IsNullOrWhiteSpace(cacheKey))
                return;

            _printTileImageIndexesByPath.TryRemove(cacheKey, out _);
        }

        private bool TryLoadPdfThumbnailFromDiskCache(string pdfPath, out int imageIndex)
        {
            imageIndex = -1;

            if (TryGetPrintTileImageIndex(pdfPath, out var existingIndex))
            {
                imageIndex = existingIndex;
                return true;
            }

            if (!TryGetPdfThumbnailCachePath(pdfPath, out var cachePath))
                return false;

            if (!File.Exists(cachePath))
                return false;

            try
            {
                using var cacheStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var cacheImage = Image.FromStream(cacheStream);
                var thumbnailBitmap = new Bitmap(cacheImage);
                _printTilesImageList.Images.Add(thumbnailBitmap);
                imageIndex = _printTilesImageList.Images.Count - 1;
                SetPrintTileImageIndex(pdfPath, imageIndex);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"TILES | PDF thumbnail cache read failed | {cachePath} | {ex.Message}");
                return false;
            }
        }

        private void SavePdfThumbnailToDiskCache(string pdfPath, Bitmap thumbnail)
        {
            if (!TryGetPdfThumbnailCachePath(pdfPath, out var cachePath))
                return;

            try
            {
                var cacheFolder = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(cacheFolder))
                    Directory.CreateDirectory(cacheFolder);

                thumbnail.Save(cachePath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Logger.Warn($"TILES | PDF thumbnail cache write failed | {cachePath} | {ex.Message}");
            }
        }

        private bool TryGetPdfThumbnailCachePath(string pdfPath, out string cachePath)
        {
            cachePath = string.Empty;
            if (!HasExistingFile(pdfPath))
                return false;

            try
            {
                var fileInfo = new FileInfo(pdfPath);
                var normalizedPath = NormalizePath(pdfPath);
                var cacheKeySource = string.Join(
                    "|",
                    normalizedPath,
                    fileInfo.Length.ToString(CultureInfo.InvariantCulture),
                    fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                    _printTilesImageList.ImageSize.Width.ToString(CultureInfo.InvariantCulture),
                    _printTilesImageList.ImageSize.Height.ToString(CultureInfo.InvariantCulture),
                    "v1");

                var cacheKeyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKeySource));
                var cacheKey = Convert.ToHexString(cacheKeyBytes);
                cachePath = Path.Combine(_printTilesCacheFolderPath, $"{cacheKey}.png");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"TILES | PDF thumbnail cache key failed | {pdfPath} | {ex.Message}");
                return false;
            }
        }

        private void StartPdfThumbnailGeneration(IEnumerable<string> pdfPaths)
        {
            _printTilesThumbnailsCts?.Cancel();
            _printTilesThumbnailsCts?.Dispose();
            _printTilesThumbnailsCts = null;

            var pending = pdfPaths
                .Select(CleanPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && HasExistingFile(path) && IsPdfPath(path))
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pending.Count == 0)
                return;

            var cts = new CancellationTokenSource();
            _printTilesThumbnailsCts = cts;
            var token = cts.Token;

            _ = Task.Run(() =>
            {
                foreach (var pdfPath in pending)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (HasPrintTileImageIndex(pdfPath))
                        continue;

                    var thumbnail = TryRenderPdfThumbnail(pdfPath, _printTilesImageList.ImageSize);
                    if (thumbnail == null)
                        continue;

                    if (HasPrintTileImageIndex(pdfPath))
                    {
                        thumbnail.Dispose();
                        continue;
                    }

                    SavePdfThumbnailToDiskCache(pdfPath, thumbnail);
                    AttachPdfThumbnailOnUiThread(pdfPath, thumbnail, token);
                }
            }, token);
        }

        private void AttachPdfThumbnailOnUiThread(string pdfPath, Bitmap thumbnail, CancellationToken token)
        {
            if (IsDisposed || !IsHandleCreated || token.IsCancellationRequested)
            {
                thumbnail.Dispose();
                return;
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || token.IsCancellationRequested)
                    {
                        thumbnail.Dispose();
                        return;
                    }

                    if (TryGetPrintTileImageIndex(pdfPath, out var existingIndex))
                    {
                        ApplyImageIndexToTileItems(pdfPath, existingIndex);
                        thumbnail.Dispose();
                        return;
                    }

                    _printTilesImageList.Images.Add(thumbnail);
                    var createdIndex = _printTilesImageList.Images.Count - 1;
                    SetPrintTileImageIndex(pdfPath, createdIndex);
                    ApplyImageIndexToTileItems(pdfPath, createdIndex);
                }));
            }
            catch
            {
                thumbnail.Dispose();
            }
        }

        private void ApplyImageIndexToTileItems(string printPath, int imageIndex)
        {
            // ImageListViewCore manages thumbnails internally.
            // Keep method as no-op to avoid breaking existing call sites.
        }

        private Image? GetPrintTileImage(ListViewItem item)
        {
            if (item == null)
                return null;

            var imageIndex = item.ImageIndex;
            if (imageIndex < 0 || imageIndex >= _printTilesImageList.Images.Count)
                return null;

            return _printTilesImageList.Images[imageIndex];
        }

        private static Rectangle FitImageToBounds(Size sourceSize, Rectangle targetBounds)
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0 || targetBounds.Width <= 0 || targetBounds.Height <= 0)
                return targetBounds;

            var widthRatio = targetBounds.Width / (double)sourceSize.Width;
            var heightRatio = targetBounds.Height / (double)sourceSize.Height;
            var scale = Math.Min(widthRatio, heightRatio);
            if (scale <= 0)
                scale = 1d;

            var drawWidth = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
            var drawX = targetBounds.Left + (targetBounds.Width - drawWidth) / 2;
            var drawY = targetBounds.Top + (targetBounds.Height - drawHeight) / 2;

            return new Rectangle(drawX, drawY, drawWidth, drawHeight);
        }

        private Bitmap? TryRenderPdfThumbnail(string pdfPath, Size targetSize)
        {
            if (!HasExistingFile(pdfPath))
                return null;

            try
            {
                using var document = PdfDocument.Load(pdfPath);
                if (document.PageCount <= 0)
                    return null;

                var pageSize = document.PageSizes.Count > 0 ? document.PageSizes[0] : new SizeF(1f, 1f);
                var pageWidth = Math.Max(1f, pageSize.Width);
                var pageHeight = Math.Max(1f, pageSize.Height);
                const int renderBasePixels = 1200;
                int renderWidth;
                int renderHeight;
                if (pageWidth >= pageHeight)
                {
                    renderWidth = renderBasePixels;
                    renderHeight = Math.Max(200, (int)Math.Round(renderBasePixels * (pageHeight / pageWidth)));
                }
                else
                {
                    renderHeight = renderBasePixels;
                    renderWidth = Math.Max(200, (int)Math.Round(renderBasePixels * (pageWidth / pageHeight)));
                }

                const int renderDpi = 150;
                using var rendered = document.Render(
                    page: 0,
                    width: renderWidth,
                    height: renderHeight,
                    dpiX: renderDpi,
                    dpiY: renderDpi,
                    flags: PdfRenderFlags.Annotations);

                if (rendered == null)
                    return null;

                return ComposeThumbnailCanvas(rendered, targetSize);
            }
            catch (Exception ex)
            {
                Logger.Warn($"TILES | PDF thumbnail failed | {pdfPath} | {ex.Message}");
                return null;
            }
        }

        private static Bitmap ComposeThumbnailCanvas(Image source, Size targetSize)
        {
            var canvas = new Bitmap(targetSize.Width, targetSize.Height);

            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.Clear(Color.FromArgb(248, 250, 253));

                var frameRect = new Rectangle(2, 2, targetSize.Width - 4, targetSize.Height - 4);
                using (var frameBackBrush = new SolidBrush(Color.White))
                    graphics.FillRectangle(frameBackBrush, frameRect);
                using (var frameBorderPen = new Pen(Color.FromArgb(205, 212, 225)))
                    graphics.DrawRectangle(frameBorderPen, frameRect);

                var imageRect = Rectangle.Inflate(frameRect, -4, -4);
                var drawRect = FitImageToBounds(source.Size, imageRect);
                graphics.DrawImage(source, drawRect);
            }

            return canvas;
        }

        private static bool IsPdfPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static Bitmap CreatePrintTilePlaceholderImage(Size size, string extension)
        {
            var bitmap = new Bitmap(size.Width, size.Height);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.FromArgb(247, 248, 250));

                var pageRect = new Rectangle(18, 10, size.Width - 36, size.Height - 24);
                using var borderPen = new Pen(Color.FromArgb(190, 197, 210), 2f);
                using var pageBrush = new SolidBrush(Color.White);
                graphics.FillRectangle(pageBrush, pageRect);
                graphics.DrawRectangle(borderPen, pageRect);

                var fold = new Point[]
                {
                    new(pageRect.Right - 18, pageRect.Top),
                    new(pageRect.Right, pageRect.Top),
                    new(pageRect.Right, pageRect.Top + 18)
                };
                using var foldBrush = new SolidBrush(Color.FromArgb(235, 238, 244));
                graphics.FillPolygon(foldBrush, fold);

                var cleanExt = string.IsNullOrWhiteSpace(extension)
                    ? "FILE"
                    : extension.Trim().TrimStart('.').ToUpperInvariant();
                if (cleanExt.Length > 5)
                    cleanExt = cleanExt[..5];

                var textRect = new Rectangle(pageRect.Left + 6, pageRect.Bottom - 26, pageRect.Width - 12, 20);
                using var extFont = new Font("Segoe UI", 8f, FontStyle.Bold);
                TextRenderer.DrawText(
                    graphics,
                    cleanExt,
                    extFont,
                    textRect,
                    Color.FromArgb(110, 117, 129),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            return bitmap;
        }

    }
}
