using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyManager
{
    /// <summary>
    /// Тестовая форма для отработки Fiery-like layout по FIERY_WIREFRAME_PLAN.
    /// Не заменяет Form1 и не содержит бизнес-логики.
    /// </summary>
    public sealed class FieryPrototypeForm : Form
    {
        public FieryPrototypeForm()
        {
            Text = "Fiery Prototype (Test Form)";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(244, 245, 247);

            var topBar = BuildTopBar();
            var rootSplit = BuildRootLayout();
            var statusLabel = BuildStatusBar();

            Controls.Add(rootSplit);
            Controls.Add(statusLabel);
            Controls.Add(topBar);
        }

        private Control BuildTopBar()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.IndianRed,
                Padding = new Padding(12, 10, 12, 10)
            };

            var title = new Label
            {
                Text = "MyManager • Fiery Prototype",
                ForeColor = Color.White,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(12, 16)
            };

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            actions.Controls.AddRange(new Control[]
            {
                CreateTopButton("Создать"),
                CreateTopButton("Режим"),
                CreateTopButton("Сортировка"),
                CreateTopButton("Лог"),
                CreateTopButton("Настройки")
            });

            panel.Controls.Add(actions);
            panel.Controls.Add(title);
            return panel;
        }

        private static Button CreateTopButton(string text)
        {
            return new Button
            {
                Text = text,
                Height = 32,
                Width = 110,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.IndianRed,
                Margin = new Padding(6, 0, 0, 0)
            };
        }

        private Control BuildRootLayout()
        {
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 230,
                FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 200,
                Panel2MinSize = 600,
                Location = new Point(0, 56)
            };

            splitMain.Panel1.Controls.Add(BuildLeftNav());
            splitMain.Panel2.Controls.Add(BuildCenterAndInspector());
            return splitMain;
        }

        private Control BuildLeftNav()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
            var title = new Label { Text = "Очереди", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                Top = 30,
                Font = new Font("Segoe UI", 9),
                BorderStyle = BorderStyle.None
            };

            list.Items.AddRange(new object[]
            {
                "Все задания",
                "Ожидание",
                "В работе",
                "Ошибка",
                "Готово",
                "Частично"
            });
            list.SelectedIndex = 0;

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 28, 0, 0) };
            host.Controls.Add(list);

            panel.Controls.Add(host);
            panel.Controls.Add(title);
            return panel;
        }

        private Control BuildCenterAndInspector()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 980,
                Panel1MinSize = 700,
                Panel2MinSize = 280,
                FixedPanel = FixedPanel.Panel2
            };

            split.Panel1.Controls.Add(BuildCenterPanel());
            split.Panel2.Controls.Add(BuildInspectorPanel());
            return split;
        }

        private Control BuildCenterPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

            var actionsPanel = new Panel { Dock = DockStyle.Top, Height = 44 };
            var btnCreate = new Button
            {
                Text = "Создать заказ",
                Width = 150,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(0, 6)
            };
            var lblSearch = new Label { Text = "Поиск:", AutoSize = true, Location = new Point(170, 12) };
            var txtSearch = new TextBox
            {
                PlaceholderText = "Поиск по № заказа и имени файла...",
                Width = 420,
                Location = new Point(225, 8)
            };
            actionsPanel.Controls.Add(btnCreate);
            actionsPanel.Controls.Add(lblSearch);
            actionsPanel.Controls.Add(txtSearch);

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White
            };

            grid.Columns.Add("colState", "СОСТОЯНИЕ");
            grid.Columns.Add("colId", "№ ЗАКАЗА");
            grid.Columns.Add("colSource", "ИСХОДНЫЕ");
            grid.Columns.Add("colReady", "ПОДГОТОВКА");
            grid.Columns.Add("colPitStop", "PITSTOP");
            grid.Columns.Add("colImposing", "HOTIMPOSING");
            grid.Columns.Add("colPrint", "ПЕЧАТЬ");

            grid.Rows.Add("Ожидание", "12345", "file.pdf", "-", "PS_Action_A", "-", "-");
            grid.Rows.Add("В работе", "12346", "brochure.pdf", "brochure_ready.pdf", "PS_Action_B", "Seq_01", "-");

            panel.Controls.Add(grid);
            panel.Controls.Add(actionsPanel);
            return panel;
        }

        private Control BuildInspectorPanel()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };

            var tabSummary = new TabPage("Сводка");
            var summary = new Label
            {
                Dock = DockStyle.Fill,
                Text = "№ заказа: —\nСтатус: —\nPitStop: —\nImposing: —",
                Padding = new Padding(10)
            };
            tabSummary.Controls.Add(summary);

            var tabPreview = new TabPage("Предпросмотр");
            var previewPlaceholder = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Предпросмотр недоступен\n(placeholder)",
                TextAlign = ContentAlignment.MiddleCenter
            };
            tabPreview.Controls.Add(previewPlaceholder);

            var tabLog = new TabPage("Журнал");
            var logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                Text = "Лог выбранного заказа будет показан здесь..."
            };
            tabLog.Controls.Add(logBox);

            tabs.TabPages.Add(tabSummary);
            tabs.TabPages.Add(tabPreview);
            tabs.TabPages.Add(tabLog);

            return tabs;
        }

        private static Control BuildStatusBar()
        {
            return new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Text = "Статус: тестовая Fiery-like форма"
            };
        }
    }
}
