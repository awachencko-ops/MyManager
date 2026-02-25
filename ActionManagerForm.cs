using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MyManager
{
    public partial class ActionManagerForm : Form
    {
        // 1. УДАЛЕНО: configFile больше не нужен здесь
        private BindingList<ActionConfig> allActions;

        public ActionManagerForm()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterParent;

            // 2. ИСПРАВЛЕНО: добавлена точка с запятой и загрузка через сервис
            allActions = new BindingList<ActionConfig>(ConfigService.GetAllPitStopConfigs());

            gridActions.DataSource = allActions;

            if (gridActions.Columns.Count > 0)
            {
                foreach (DataGridViewColumn col in gridActions.Columns)
                    if (col.Name != "Name") col.Visible = false;

                if (gridActions.Columns["Name"] != null)
                {
                    gridActions.Columns["Name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    gridActions.Columns["Name"].HeaderText = "Зарегистрированные PitStop Actions";
                }
            }

            gridActions.SelectionChanged += GridActions_SelectionChanged;
            buttonCreateAction.Click += ButtonCreateAction_Click;
            buttonDeleteAction.Click += ButtonDeleteAction_Click;

            btnBrowseBase.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                        txtBaseFolder.Text = fbd.SelectedPath;
                }
            };

            btnCreateBasic.Click += (s, e) =>
            {
                string baseF = txtBaseFolder.Text.Trim();
                if (string.IsNullOrEmpty(baseF)) return;

                txtInput.Text = Path.Combine(baseF, "Input Folder");
                txtRepSucc.Text = Path.Combine(baseF, "Reports on Success");
                txtRepErr.Text = Path.Combine(baseF, "Reports on Error");
                txtOrgSucc.Text = Path.Combine(baseF, "Original Docs on Success");
                txtOrgErr.Text = Path.Combine(baseF, "Original Docs on Error");
                txtProcSucc.Text = Path.Combine(baseF, "Processed Docs on Success");
                txtProcErr.Text = Path.Combine(baseF, "Processed Docs on Error");
                txtNonPdfLogs.Text = Path.Combine(baseF, "Non-PDF Error Logs");
                txtNonPdfFiles.Text = Path.Combine(baseF, "Non-PDF Files");

                if (string.IsNullOrWhiteSpace(txtActionName.Text) || txtActionName.Text == "Action Name")
                    txtActionName.Text = Path.GetFileName(baseF);

                try
                {
                    Directory.CreateDirectory(baseF);
                    Directory.CreateDirectory(txtInput.Text);
                    Directory.CreateDirectory(txtRepSucc.Text);
                    Directory.CreateDirectory(txtRepErr.Text);
                    Directory.CreateDirectory(txtOrgSucc.Text);
                    Directory.CreateDirectory(txtOrgErr.Text);
                    Directory.CreateDirectory(txtProcSucc.Text);
                    Directory.CreateDirectory(txtProcErr.Text);
                    Directory.CreateDirectory(txtNonPdfLogs.Text);
                    Directory.CreateDirectory(txtNonPdfFiles.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось создать базовые папки: " + ex.Message);
                }
            };

            btnOk.Click += (s, e) =>
            {
                // 3. Сохраняем при закрытии
                ConfigService.SavePitStopConfigs(allActions.ToList());
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
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

        private void GridActions_SelectionChanged(object sender, EventArgs e)
        {
            if (gridActions.CurrentRow != null &&
                gridActions.CurrentRow.DataBoundItem is ActionConfig selectedAction)
            {
                FillFields(selectedAction);
            }
        }

        private void FillFields(ActionConfig config)
        {
            if (config == null) return;
            txtActionName.Text = config.Name;
            txtBaseFolder.Text = config.BaseFolder;
            txtInput.Text = config.InputFolder;
            txtRepSucc.Text = config.ReportSuccess;
            txtRepErr.Text = config.ReportError;
            txtOrgSucc.Text = config.OriginalSuccess;
            txtOrgErr.Text = config.OriginalError;
            txtProcSucc.Text = config.ProcessedSuccess;
            txtProcErr.Text = config.ProcessedError;
            txtNonPdfLogs.Text = config.NonPdfLogs;
            txtNonPdfFiles.Text = config.NonPdfFiles;
        }

        private void ClearAllFields()
        {
            txtActionName.Text = "Action Name";
            txtBaseFolder.Clear();
            txtInput.Clear();
            txtRepSucc.Clear();
            txtRepErr.Clear();
            txtOrgSucc.Clear();
            txtOrgErr.Clear();
            txtProcSucc.Clear();
            txtProcErr.Clear();
            txtNonPdfLogs.Clear();
            txtNonPdfFiles.Clear();
        }

        private void ButtonDeleteAction_Click(object sender, EventArgs e)
        {
            if (gridActions.CurrentRow == null ||
                !(gridActions.CurrentRow.DataBoundItem is ActionConfig selected))
            {
                MessageBox.Show("Сначала выберите сценарий.");
                return;
            }

            if (MessageBox.Show($"Удалить '{selected.Name}'?", "Удаление", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                allActions.Remove(selected);
                // 4. ИСПРАВЛЕНО: Сохраняем изменения через сервис
                ConfigService.SavePitStopConfigs(allActions.ToList());
                ClearAllFields();
            }
        }

        private void ButtonCreateAction_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtActionName.Text) || txtActionName.Text == "Action Name")
            {
                MessageBox.Show("Введите уникальное имя!");
                return;
            }

            var newAction = new ActionConfig
            {
                Name = txtActionName.Text.Trim(),
                BaseFolder = txtBaseFolder.Text.Trim(),
                InputFolder = txtInput.Text.Trim(),
                ReportSuccess = txtRepSucc.Text.Trim(),
                ReportError = txtRepErr.Text.Trim(),
                OriginalSuccess = txtOrgSucc.Text.Trim(),
                OriginalError = txtOrgErr.Text.Trim(),
                ProcessedSuccess = txtProcSucc.Text.Trim(),
                ProcessedError = txtProcErr.Text.Trim(),
                NonPdfLogs = txtNonPdfLogs.Text.Trim(),
                NonPdfFiles = txtNonPdfFiles.Text.Trim()
            };

            var existing = allActions.FirstOrDefault(a => a.Name == newAction.Name);
            if (existing != null)
            {
                if (MessageBox.Show("Обновить существующий?", "Обновление", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return;

                int index = allActions.IndexOf(existing);
                allActions[index] = newAction;
            }
            else
            {
                allActions.Add(newAction);
            }

            // 5. ИСПРАВЛЕНО: Сохраняем изменения через сервис
            ConfigService.SavePitStopConfigs(allActions.ToList());
            MessageBox.Show("Сценарий сохранён.");
        }

        // 6. УДАЛЕНО: методы LoadFromFile и SaveToFile больше не нужны, так как логика в ConfigService

        private void gridActions_CellContentClick(object sender, DataGridViewCellEventArgs e) { }
    }
}