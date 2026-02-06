using System;
using System.Windows.Forms;

namespace MyManager
{
    public sealed class OrderGridContextMenu
    {
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();

        // --- Связи с Form1 (Actions) ---
        public Action<int> OpenFolder { get; set; } // int - это стадия (0-корень, 1-исходные, 2-подготовка, 3-печать)
        public Action Delete { get; set; }
        public Action Run { get; set; }
        public Action<int> RemoveFile { get; set; }
        public Action<int, string> PickFile { get; set; }
        public Action<int> RenameFile { get; set; }
        public Action<int> PastePathFromClipboard { get; set; }
        public Action<int> CopyPathToClipboard { get; set; }

        public Action ApplyWatermark { get; set; }
        public Action ApplyWatermarkLeft { get; set; }

        public Action CopyToPrepared { get; set; }
        public Action CopyToPrint { get; set; }
        public Action CopyToGrandpa { get; set; }

        public Action OpenPitStopMan { get; set; }
        public Action OpenImpMan { get; set; }

        public ContextMenuStrip Build(string colName, bool allowCopyToGrandpa = true)
        {
            _menu.Items.Clear();

            // Определяем стадию в зависимости от колонки, на которую нажали
            int currentStage = colName switch
            {
                "colSource" => 1,
                "colReady" => 2,
                "colPrint" => 3,
                _ => 0 // 0 означает корень заказа
            };

            // 1. ГЛАВНЫЕ КНОПКИ (Всегда сверху)
            AddItem("🚀 Запустить обработку", Run);
            AddItem("❌ Удалить заказ из списка", Delete);

            // Откроет либо корень, либо конкретную подпапку (1. исходные и т.д.)
            AddItem("📁 Открыть папку", () => OpenFolder?.Invoke(currentStage));

            // 2. РАЗДЕЛИТЕЛЬ
            _menu.Items.Add(new ToolStripSeparator());

            // 3. СПЕЦИФИЧЕСКИЕ ПУНКТЫ ДЛЯ КОЛОНОК
            switch (colName)
            {
                case "colSource":
                    AddItem("✏️ Переименовать файл", () => RenameFile?.Invoke(1));
                    AddItem("Указать файл...", () => PickFile?.Invoke(1, "source"));
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(1));
                    break;

                case "colReady":
                    AddItem("📋 Копировать путь в буфер", () => CopyPathToClipboard?.Invoke(2));
                    AddItem("✏️ Переименовать файл", () => RenameFile?.Invoke(2));
                    AddItem("Указать файл...", () => PickFile?.Invoke(2, "prepared"));
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(2));
                    break;

                case "colPrint":
                    AddItem("⏺️ Водяной знак (сверху)", ApplyWatermark);
                    AddItem("⏺️ Водяной знак (слева)", ApplyWatermarkLeft);
                    AddItem("✏️ Переименовать файл", () => RenameFile?.Invoke(3));
                    AddItem("📋 Копировать путь в буфер", () => CopyPathToClipboard?.Invoke(3));
                    if (allowCopyToGrandpa)
                    {
                        _menu.Items.Add(new ToolStripSeparator());
                        AddItem("Копировать в Дедушку", CopyToGrandpa);
                    }
                    AddItem("Указать файл...", () => PickFile?.Invoke(3, "print"));
                    AddItem("Удалить файл", () => RemoveFile?.Invoke(3));
                    break;

                case "colPitStop":
                    AddItem("Открыть диспетчер PitStop", OpenPitStopMan);
                    break;

                case "colImposing":
                    AddItem("Открыть диспетчер Imposing", OpenImpMan);
                    break;
            }

            return _menu;
        }

        private void AddItem(string text, Action action)
        {
            if (action == null) return;
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => action();
            _menu.Items.Add(item);
        }
    }
}
