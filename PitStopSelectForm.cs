using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace MyManager
{
    public partial class PitStopSelectForm : Form
    {
        private readonly BindingList<ActionConfig> _items = new BindingList<ActionConfig>();
        private List<ActionConfig> _allData = new List<ActionConfig>();

        public string SelectedName { get; private set; } = "-";
        public string SelectedActionName => SelectedName;

        private readonly string _preselectName;

        public PitStopSelectForm(string currentName = "-")
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterParent;
            _preselectName = string.IsNullOrWhiteSpace(currentName) ? "-" : currentName.Trim();

            Text = "Выбор PitStop Action";
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            Load += (s, e) =>
            {
                BuildGrid();
                LoadItems();
                ApplyFilter();
                PreselectIfPossible();
            };

            txtSearch.TextChanged += (s, e) => ApplyFilter();
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            btnSelect.Click += (s, e) => SelectCurrent();
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SelectCurrent(); };
        }

        private void LoadItems()
        {
            // БЕРЕМ ДАННЫЕ ИЗ JSON ЧЕРЕЗ СЕРВИС
            _allData = ConfigService.GetAllPitStopConfigs()
                        .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
        }

        private void ApplyFilter()
        {
            string q = (txtSearch.Text ?? "").Trim();
            var filtered = string.IsNullOrWhiteSpace(q)
                ? _allData
                : _allData.Where(x => x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     x.BaseFolder.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);

            _items.Clear();
            foreach (var a in filtered) _items.Add(a);
            grid.DataSource = _items;
            lblCount.Text = $"Actions: {_items.Count}";
        }

        private void PreselectIfPossible()
        {
            if (string.IsNullOrWhiteSpace(_preselectName) || _preselectName == "-") return;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.DataBoundItem is ActionConfig cfg && cfg.Name == _preselectName)
                {
                    row.Selected = true;
                    grid.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        private void SelectCurrent()
        {
            if (grid.CurrentRow?.DataBoundItem is ActionConfig cfg)
            {
                SelectedName = cfg.Name;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void BuildGrid()
        {
            grid.AutoGenerateColumns = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Action", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "BaseFolder", HeaderText = "Base Folder", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        }
    }
}