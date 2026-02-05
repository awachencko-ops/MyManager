using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

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

            // 2. ИСПОЛЬЗУЕМ СЕРВИС: загружаем данные из JSON
            allActions = new BindingList<ImposingConfig>(ConfigService.GetAllImposingConfigs());

            // Привязка списка к таблице
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