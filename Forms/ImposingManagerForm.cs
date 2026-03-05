using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace MyManager
{
    public partial class ImposingManagerForm : Form
    {
        // 1. УДАЛЕНО: configFile больше не нужен здесь
        private BindingList<ImposingConfig> allActions;

        public ImposingManagerForm()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterParent;

            allActions = new BindingList<ImposingConfig>(ConfigService.GetAllImposingConfigs());
            dataGridView1.DataSource = allActions;

            // Настройка колонок таблицы
            if (dataGridView1.Columns.Count > 0)
            {
                foreach (DataGridViewColumn col in dataGridView1.Columns)
                    if (col.Name != "Name") col.Visible = false;

                if (dataGridView1.Columns["Name"] != null)
                {
                    dataGridView1.Columns["Name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    dataGridView1.Columns["Name"].HeaderText = "Зарегистрированные Imposing сценарии";
                }
                dataGridView1.RowHeadersVisible = false;
            }

            dataGridView1.SelectionChanged += DataGridView1_SelectionChanged;

            // Выбор базовой папки
            btnBrowseBase.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                        txtBaseFolder.Text = fbd.SelectedPath;
                }
            };

            treeCategories.AfterSelect += TreeCategories_AfterSelect;
            btnSyncMemory.Click += (s, e) => SyncWithAcrobatMemory();
            btnExportQHI.Click += (s, e) => ExportToQuiteHotImposing();

            // Автозаполнение и физическое создание папок
            btnCreateBasic.Click += (s, e) =>
            {
                string baseF = txtBaseFolder.Text.Trim();
                if (string.IsNullOrEmpty(baseF)) return;

                txtIn.Text = Path.Combine(baseF, "in");
                txtOut.Text = Path.Combine(baseF, "out");
                txtDone.Text = Path.Combine(baseF, "done");
                txtError.Text = Path.Combine(baseF, "error");

                if (string.IsNullOrWhiteSpace(txtName.Text))
                    txtName.Text = Path.GetFileName(baseF);

                try
                {
                    Directory.CreateDirectory(baseF);
                    Directory.CreateDirectory(txtIn.Text);
                    Directory.CreateDirectory(txtOut.Text);
                    Directory.CreateDirectory(txtDone.Text);
                    Directory.CreateDirectory(txtError.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось создать папки: " + ex.Message);
                }
            };

            buttonCreateSequance.Click += ButtonCreateSequance_Click;
            buttonDeleteSequance.Click += ButtonDeleteSequance_Click;

            btnOk.Text = "Закрыть";
            btnOk.Click += (s, e) =>
            {
                // 3. СОХРАНЯЕМ: при закрытии записываем всё в JSON
                ConfigService.SaveImposingConfigs(allActions.ToList());
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            UpdateCategoryTree();
        }

        public void SetEmbeddedMode(bool embedded)
        {
            if (embedded)
            {
                btnOk.Visible = false;
                StartPosition = FormStartPosition.Manual;
                MaximizeBox = false;
                MinimizeBox = false;
            }
            else
            {
                btnOk.Visible = true;
                StartPosition = FormStartPosition.CenterParent;
            }
        }

        private void DataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow != null &&
                dataGridView1.CurrentRow.DataBoundItem is ImposingConfig selected)
            {
                FillFields(selected);
            }
        }

        private void FillFields(ImposingConfig cfg)
        {
            if (cfg == null) return;
            txtName.Text = cfg.Name;
            txtBaseFolder.Text = cfg.BaseFolder;
            txtIn.Text = cfg.In;
            txtOut.Text = cfg.Out;
            txtDone.Text = cfg.Done;
            txtError.Text = cfg.Error;
        }

        private void ClearAllFields()
        {
            txtName.Clear();
            txtBaseFolder.Clear();
            txtIn.Clear();
            txtOut.Clear();
            txtDone.Clear();
            txtError.Clear();
        }

        private void ButtonCreateSequance_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Введите имя сценария!");
                return;
            }

            var newCfg = new ImposingConfig
            {
                Name = txtName.Text.Trim(),
                BaseFolder = txtBaseFolder.Text.Trim(),
                In = txtIn.Text.Trim(),
                Out = txtOut.Text.Trim(),
                Done = txtDone.Text.Trim(),
                Error = txtError.Text.Trim()
            };

            if (string.IsNullOrWhiteSpace(newCfg.BaseFolder) || string.IsNullOrWhiteSpace(newCfg.In))
            {
                MessageBox.Show("Заполните пути!");
                return;
            }

            var existing = allActions.FirstOrDefault(a => a.Name == newCfg.Name);
            if (existing != null)
            {
                if (MessageBox.Show($"Обновить '{newCfg.Name}'?", "Обновление", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return;

                int idx = allActions.IndexOf(existing);
                allActions[idx] = newCfg;
            }
            else
            {
                allActions.Add(newCfg);
            }

            // 4. СОХРАНЯЕМ ИЗМЕНЕНИЯ
            ConfigService.SaveImposingConfigs(allActions.ToList());
            MessageBox.Show("Сценарий сохранён.");
        }

        private void SyncWithAcrobatMemory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string memoryPath = Path.Combine(appData, @"Quite\Preferences\qiplusmemory6.xml");

            if (!File.Exists(memoryPath))
            {
                memoryPath = Path.Combine(appData, @"Quite\Quite Imposing Plus 5\qiplusmemory6.xml");
            }

            if (!File.Exists(memoryPath))
            {
                using (OpenFileDialog ofd = new OpenFileDialog { Filter = "XML|*.xml", Title = "Выберите qiplusmemory6.xml" })
                {
                    if (ofd.ShowDialog() == DialogResult.OK) memoryPath = ofd.FileName;
                    else return;
                }
            }

            try
            {
                XDocument xDoc = XDocument.Load(memoryPath);
                // ВАЖНО: Указываем пространство имен Quite
                XNamespace ns = "http://www.quite.com/general/ns/quitexml/";

                // Ищем словарь Memory с учетом пространства имен
                var memoryDict = xDoc.Descendants(ns + "DICT")
                                     .FirstOrDefault(d => d.Attribute("N")?.Value == "Memory");

                if (memoryDict == null)
                {
                    MessageBox.Show("В файле не найдена секция Memory. Убедитесь, что вы выбрали правильный файл.");
                    return;
                }

                var categories = memoryDict.Element(ns + "ITEMS")?.Elements(ns + "DICT");
                if (categories == null) return;

                string rootPath = @"C:\HotImposing";
                int added = 0;

                foreach (var catDict in categories)
                {
                    var catItems = catDict.Element(ns + "ITEMS");
                    if (catItems == null) continue;

                    string catName = catItems.Elements(ns + "S").FirstOrDefault(s => s.Attribute("N")?.Value == "Name")?.Value;

                    // Пропускаем системные категории
                    if (string.IsNullOrEmpty(catName) || catName == "Automation sequences" || catName == "Memory") continue;

                    // Ищем секвенции внутри категории (тег DICT с атрибутом N='Item' или просто вложенные DICT)
                    var sequenceWrapper = catItems.Elements(ns + "DICT").FirstOrDefault(d => d.Attribute("N")?.Value == "Item");
                    var sequences = sequenceWrapper?.Element(ns + "ITEMS")?.Elements(ns + "DICT");

                    if (sequences == null) continue;

                    foreach (var seqDict in sequences)
                    {
                        var seqItems = seqDict.Element(ns + "ITEMS");
                        if (seqItems == null) continue;

                        string seqName = seqItems.Elements(ns + "S").FirstOrDefault(s => s.Attribute("N")?.Value == "Name")?.Value;

                        if (string.IsNullOrEmpty(seqName)) continue;
                        if (allActions.Any(a => a.Name == seqName)) continue;

                        // Очистка имен для папок (удаляем слэши, двоеточия и т.д.)
                        string safeCat = Regex.Replace(catName, @"[<>:""/\\|?*]", "_").Trim();
                        string safeSeq = Regex.Replace(seqName, @"[<>:""/\\|?*]", "_").Trim();
                        string baseF = Path.Combine(rootPath, safeCat, safeSeq);

                        var cfg = new ImposingConfig
                        {
                            Name = seqName,
                            Category = catName,
                            BaseFolder = baseF,
                            In = Path.Combine(baseF, "in"),
                            Out = Path.Combine(baseF, "out"),
                            Done = Path.Combine(baseF, "done"),
                            Error = Path.Combine(baseF, "error")
                        };

                        // Создаем папки
                        Directory.CreateDirectory(cfg.In);
                        Directory.CreateDirectory(cfg.Out);
                        Directory.CreateDirectory(cfg.Done);
                        Directory.CreateDirectory(cfg.Error);

                        allActions.Add(cfg);
                        UpdateCategoryTree();
                        added++;
                    }
                }

                ConfigService.SaveImposingConfigs(allActions.ToList());
                MessageBox.Show($"Готово! Импортировано секвенций: {added}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при чтении файла: " + ex.Message);
                UpdateCategoryTree();
            }
        }

        private void ExportToQuiteHotImposing()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 1. Путь к твоему файлу
            string qhiPath = Path.Combine(appData, @"Quite\Preferences\qihot4.xml");

            // 2. Если вдруг файла там нет, проверяем альтернативный путь (5-я версия)
            if (!File.Exists(qhiPath))
            {
                qhiPath = Path.Combine(appData, @"Quite\Quite Hot Imposing 5\HotFolders.xml");
            }

            // 3. Если файл всё еще не найден — даем выбрать вручную
            if (!File.Exists(qhiPath))
            {
                MessageBox.Show("Файл настроек Hot Imposing не найден автоматически.\nПожалуйста, укажите его вручную.");
                using (OpenFileDialog ofd = new OpenFileDialog { Filter = "XML|*.xml", Title = "Выберите qihot4.xml или HotFolders.xml" })
                {
                    if (ofd.ShowDialog() == DialogResult.OK) qhiPath = ofd.FileName;
                    else return;
                }
            }

            try
            {
                // ОПРЕДЕЛЯЕМ ПРОСТРАНСТВО ИМЕН
                XNamespace ns = "http://www.quite.com/general/ns/quitexml/";

                // Генерируем список папок
                XElement foldersItems = new XElement(ns + "ITEMS");
                for (int i = 0; i < allActions.Count; i++)
                {
                    var cfg = allActions[i];
                    XElement dict = new XElement(ns + "DICT", new XAttribute("N", i.ToString()),
                        new XElement(ns + "ITEMS",
                            new XElement(ns + "A", new XAttribute("N", "ControlMode"), "Sequence"),
                            new XElement(ns + "S", new XAttribute("N", "Description"), cfg.Name),
                            new XElement(ns + "S", new XAttribute("N", "HFInput"), cfg.In),
                            new XElement(ns + "S", new XAttribute("N", "HFOutput"), cfg.Out),
                            new XElement(ns + "S", new XAttribute("N", "HFDone"), cfg.Done),
                            new XElement(ns + "S", new XAttribute("N", "HFError"), cfg.Error),
                            new XElement(ns + "S", new XAttribute("N", "QISequenceName"), cfg.Name),
                            new XElement(ns + "S", new XAttribute("N", "QISequenceCategory"), cfg.Category),
                            new XElement(ns + "B", new XAttribute("N", "HFEnabled"), "1"),
                            new XElement(ns + "I", new XAttribute("N", "HFClientID"), (i + 1).ToString())
                        )
                    );
                    foldersItems.Add(dict);
                }

                // Собираем весь документ
                XDocument doc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement(ns + "QUITEXML",
                        new XElement(ns + "ITEMS",
                            new XElement(ns + "DICT", new XAttribute("N", "Folders"), foldersItems)
                        )
                    )
                );

                // Делаем бэкап
                if (File.Exists(qhiPath))
                    File.Copy(qhiPath, qhiPath + ".bak", true);

                // Сохраняем
                doc.Save(qhiPath);
                MessageBox.Show($"Настройки успешно записаны в:\n{qhiPath}\n\nПерезапустите Quite Hot Imposing.", "Успех");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при записи файла: " + ex.Message);
            }
        }

        private void UpdateCategoryTree()
        {
            if (treeCategories.InvokeRequired)
            {
                treeCategories.Invoke(new Action(UpdateCategoryTree));
                return;
            }

            treeCategories.BeginUpdate();
            treeCategories.Nodes.Clear();

            // Добавляем корень
            TreeNode rootNode = treeCategories.Nodes.Add("ALL", "Все группы");

            // Берем все уникальные категории
            var categories = allActions
                .Select(a => string.IsNullOrWhiteSpace(a.Category) ? "Без категории" : a.Category)
                .Distinct()
                .OrderBy(c => c);

            foreach (var cat in categories)
            {
                // Ключ и Текст узла делаем одинаковыми (название категории)
                treeCategories.Nodes.Add(cat, cat);
            }

            treeCategories.ExpandAll();
            treeCategories.SelectedNode = rootNode; // Выделяем "Все группы" при старте
            treeCategories.EndUpdate();
        }

        private void TreeCategories_AfterSelect(object sender, TreeViewEventArgs e)
        {
            string selectedCategory = e.Node.Name;

            if (selectedCategory == "ALL")
            {
                // Показываем всё
                dataGridView1.DataSource = allActions;
            }
            else
            {
                // Фильтруем список
                var filtered = allActions
                    .Where(a => (string.IsNullOrWhiteSpace(a.Category) ? "Без категории" : a.Category) == selectedCategory)
                    .ToList();

                dataGridView1.DataSource = new BindingList<ImposingConfig>(filtered);
            }
        }

        private void ButtonDeleteSequance_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow == null || !(dataGridView1.CurrentRow.DataBoundItem is ImposingConfig selected))
            {
                MessageBox.Show("Выберите сценарий.");
                return;
            }

            if (MessageBox.Show($"Удалить '{selected.Name}'?", "Удаление", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                allActions.Remove(selected);
                // 5. СОХРАНЯЕМ ИЗМЕНЕНИЯ
                ConfigService.SaveImposingConfigs(allActions.ToList());
                ClearAllFields();
            }
        }

        // 6. УДАЛЕНО: Методы LoadFromFile, SaveToFile и FromString больше не нужны.
    }
}