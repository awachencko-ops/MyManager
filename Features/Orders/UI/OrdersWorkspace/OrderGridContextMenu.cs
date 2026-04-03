using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Replica
{
    public sealed class OrderGridContextMenu
    {
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        public Action<int>? OpenFolder { get; set; }
        public Action? Delete { get; set; }
        public Action? Run { get; set; }
        public Action? Stop { get; set; }
        public Action<int>? RemoveFile { get; set; }
        public Action<int, string>? PickFile { get; set; }
        public Action<int>? RenameFile { get; set; }
        public Action<int>? CopyPathToClipboard { get; set; }
        public Action<int>? PastePathFromClipboard { get; set; }
        public Action? ApplyWatermark { get; set; }
        public Action? ApplyWatermarkLeft { get; set; }
        public Action? CopyToPrepared { get; set; }
        public Action? CopyToPrint { get; set; }
        public Action? CopyToGrandpa { get; set; }
        public Action? OpenPitStopMan { get; set; }
        public Action? OpenImpMan { get; set; }
        public Action? RemovePitStopAction { get; set; }
        public Action? RemoveImposingAction { get; set; }
        public Action? OpenOrderLog { get; set; }

        public ContextMenuStrip Build(string colName, bool allowCopyToGrandpa = true)
        {
            _menu.Items.Clear();
            _menu.ImageScalingSize = new Size(GetMenuIconSize(), GetMenuIconSize());

            var currentStage = OrderGridColumnNames.ResolveStage(colName);

            AddItem("Запустить обработку", Run, "av", "play_arrow");
            AddItem("Остановить обработку", Stop, "av", "stop");
            AddItem("Удалить заказ из списка", Delete, "action", "delete");
            AddItem("Открыть папку", () => OpenFolder?.Invoke(currentStage), "file", "folder_open");

            _menu.Items.Add(new ToolStripSeparator());

            switch (colName)
            {
                case OrderGridColumnNames.Source:
                    AddItem("Вставить путь из буфера", () => PastePathFromClipboard?.Invoke(OrderStages.Source), "content", "content_paste");
                    AddItem("Переименовать файл", () => RenameFile?.Invoke(OrderStages.Source), "file", "drive_file_rename_outline");
                    AddItem("Указать файл...", () => PickFile?.Invoke(OrderStages.Source, "source"), "file", "attach_file");
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(OrderStages.Source), "action", "delete");
                    break;

                case OrderGridColumnNames.Prepared:
                case OrderGridColumnNames.PreparedLegacy:
                    AddItem("Вставить путь из буфера", () => PastePathFromClipboard?.Invoke(OrderStages.Prepared), "content", "content_paste");
                    AddItem("Переименовать файл", () => RenameFile?.Invoke(OrderStages.Prepared), "file", "drive_file_rename_outline");
                    AddItem("Указать файл...", () => PickFile?.Invoke(OrderStages.Prepared, "prepared"), "file", "attach_file");
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(OrderStages.Prepared), "action", "delete");
                    break;

                case OrderGridColumnNames.Print:
                    AddItem("Водяной знак (сверху)", ApplyWatermark, "toggle", "radio_button_checked");
                    AddItem("Водяной знак (слева)", ApplyWatermarkLeft, "toggle", "radio_button_checked");
                    AddItem("Переименовать файл", () => RenameFile?.Invoke(OrderStages.Print), "file", "drive_file_rename_outline");
                    AddItem("Копировать путь в буфер", () => CopyPathToClipboard?.Invoke(OrderStages.Print), "content", "content_copy");
                    if (allowCopyToGrandpa)
                    {
                        _menu.Items.Add(new ToolStripSeparator());
                        AddItem("Копировать в Дедушку", CopyToGrandpa, "content", "content_copy");
                    }

                    AddItem("Указать файл...", () => PickFile?.Invoke(OrderStages.Print, "print"), "file", "attach_file");
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(OrderStages.Print), "action", "delete");
                    break;

                case OrderGridColumnNames.PitStopLegacy:
                case OrderGridColumnNames.PitStop:
                    AddItem("Открыть диспетчер PitStop", OpenPitStopMan, "action", "build");
                    AddItem("Очистить операцию", RemovePitStopAction, "action", "delete_outline");
                    break;

                case OrderGridColumnNames.ImposingLegacy:
                case OrderGridColumnNames.HotImposing:
                    AddItem("Открыть диспетчер Imposing", OpenImpMan, "action", "build");
                    AddItem("Очистить операцию", RemoveImposingAction, "action", "delete_outline");
                    break;

                case OrderGridColumnNames.StateLegacy:
                case OrderGridColumnNames.Status:
                    AddItem("Открыть лог заказа", OpenOrderLog, "action", "terminal");
                    break;
            }

            return _menu;
        }

        private void AddItem(string text, Action? action, string? iconFolder = null, string? iconHint = null)
        {
            if (action == null)
                return;

            var item = new ToolStripMenuItem(text);
            if (!string.IsNullOrWhiteSpace(iconFolder))
            {
                var icon = GetMenuIcon(iconFolder, iconHint ?? string.Empty);
                if (icon != null)
                {
                    item.Image = icon;
                    item.ImageScaling = ToolStripItemImageScaling.None;
                }
            }

            item.Click += (_, _) => action();
            _menu.Items.Add(item);
        }

        private Image? GetMenuIcon(string iconFolder, string iconHint)
        {
            var iconSize = GetMenuIconSize();
            var cacheKey = $"{iconFolder}|{iconHint}|{iconSize}";
            if (_iconCache.TryGetValue(cacheKey, out var cached))
                return new Bitmap(cached);

            var icon = OrdersWorkspaceIconCatalog.LoadIcon(iconFolder, iconHint, size: iconSize);
            if (icon == null)
                return null;

            _iconCache[cacheKey] = new Bitmap(icon);
            return icon;
        }

        private int GetMenuIconSize()
        {
            var scale = _menu.DeviceDpi > 0 ? _menu.DeviceDpi / 96f : 1f;
            var size = (int)Math.Round(16f * scale);
            return Math.Max(16, Math.Min(32, size));
        }
    }
}
