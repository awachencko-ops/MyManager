using System;
using System.Windows.Forms;

namespace Replica
{
    public sealed class OrderGridContextMenu
    {
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();

        // --- Связи с MainForm (Actions) ---
        public Action<int>? OpenFolder { get; set; } // int - это стадия (0-корень, 1-исходные, 2-подготовка, 3-печать)
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

        public ContextMenuStrip Build(
            string colName,
            bool allowCopyToGrandpa = true)
        {
            _menu.Items.Clear();

            // Определяем стадию в зависимости от колонки, на которую нажали.
            int currentStage = OrderGridColumnNames.ResolveStage(colName);

            // 1. ГЛАВНЫЕ КНОПКИ (Всегда сверху)
            AddItem("🚀 Запустить обработку", Run);
            AddItem("🛑 Остановить обработку", Stop);
            AddItem("❌ Удалить заказ из списка", Delete);

            // Откроет либо корень, либо конкретную подпапку (1. исходные и т.д.)
            AddItem("📁 Открыть папку", () => OpenFolder?.Invoke(currentStage));

            // 2. РАЗДЕЛИТЕЛЬ
            _menu.Items.Add(new ToolStripSeparator());

            // 3. СПЕЦИФИЧЕСКИЕ ПУНКТЫ ДЛЯ КОЛОНОК
            switch (colName)
            {
                case OrderGridColumnNames.Source:
                    AddItem("📋 Вставить путь из буфера", () => PastePathFromClipboard?.Invoke(OrderStages.Source));
                    AddItem("✏️ Переименовать файл", () => RenameFile?.Invoke(OrderStages.Source));
                    AddItem("Указать файл...", () => PickFile?.Invoke(OrderStages.Source, "source"));
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(OrderStages.Source));
                    break;

                case OrderGridColumnNames.Prepared:
                case OrderGridColumnNames.PreparedLegacy:
                    AddItem("📋 Вставить путь из буфера", () => PastePathFromClipboard?.Invoke(OrderStages.Prepared));
                    AddItem("✏️ Переименовать файл", () => RenameFile?.Invoke(OrderStages.Prepared));
                    AddItem("Указать файл...", () => PickFile?.Invoke(OrderStages.Prepared, "prepared"));
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(OrderStages.Prepared));
                    break;

                case OrderGridColumnNames.Print:
                    AddItem("⏺️ Водяной знак (сверху)", ApplyWatermark);
                    AddItem("⏺️ Водяной знак (слева)", ApplyWatermarkLeft);
                    AddItem("✏️ Переименовать файл", () => RenameFile?.Invoke(OrderStages.Print));
                    AddItem("📋 Копировать путь в буфер", () => CopyPathToClipboard?.Invoke(OrderStages.Print));
                    if (allowCopyToGrandpa)
                    {
                        _menu.Items.Add(new ToolStripSeparator());
                        AddItem("Копировать в Дедушку", CopyToGrandpa);
                    }
                    AddItem("Указать файл...", () => PickFile?.Invoke(OrderStages.Print, "print"));
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(OrderStages.Print));
                    break;

                case OrderGridColumnNames.PitStopLegacy:
                case OrderGridColumnNames.PitStop:
                    AddItem("Открыть диспетчер PitStop", OpenPitStopMan);
                    AddItem("Очистить операцию", RemovePitStopAction);
                    break;

                case OrderGridColumnNames.ImposingLegacy:
                case OrderGridColumnNames.HotImposing:
                    AddItem("Открыть диспетчер Imposing", OpenImpMan);
                    AddItem("Очистить операцию", RemoveImposingAction);
                    break;

                case OrderGridColumnNames.StateLegacy:
                case OrderGridColumnNames.Status:
                    AddItem("📜 Открыть лог заказа", OpenOrderLog);
                    break;
            }

            return _menu;
        }

        private void AddItem(string text, Action? action)
        {
            if (action == null) return;
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => action();
            _menu.Items.Add(item);
        }
    }
}
