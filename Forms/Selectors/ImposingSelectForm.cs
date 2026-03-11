using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace MyManager
{
    public partial class ImposingSelectForm : Form
    {
        private readonly BindingList<ImposingConfig> _items = new BindingList<ImposingConfig>();
        private List<ImposingConfig> _allData = new List<ImposingConfig>();
        private readonly string _preselectName;

        public string SelectedName { get; private set; } = "-";

        public ImposingSelectForm(string currentName = "-")
        {
            InitializeComponent();
            _preselectName = string.IsNullOrWhiteSpace(currentName) ? "-" : currentName.Trim();

            Load += (s, e) =>
            {
                BuildGrid();
                LoadItems();
                UpdateCategoryTree();
                ApplyFilter();
                PreselectIfPossible();
            };

            txtSearch.TextChanged += (s, e) => ApplyFilter();
            treeCategories.AfterSelect += (s, e) => ApplyFilter();

            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            btnSelect.Click += (s, e) => SelectCurrent();

            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SelectCurrent(); };

            // Быстрые клавиши
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = true; SelectCurrent(); }
                if (e.KeyCode == Keys.Escape) Close();
            };
        }

        private void LoadItems()
        {
            _allData = ConfigService.GetAllImposingConfigs()
                        .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
        }

        private void UpdateCategoryTree()
        {
            treeCategories.BeginUpdate();
            treeCategories.Nodes.Clear();

            var rootNode = treeCategories.Nodes.Add("ALL", "Все группы");

            var categories = _allData
                .Select(a => string.IsNullOrWhiteSpace(a.Category) ? "Без категории" : a.Category)
                .Distinct()
                .OrderBy(c => c);

            foreach (var cat in categories)
            {
                treeCategories.Nodes.Add(cat, cat);
            }

            treeCategories.ExpandAll();
            treeCategories.SelectedNode = rootNode;
            treeCategories.EndUpdate();
        }

        private void ApplyFilter()
        {
            string q = (txtSearch.Text ?? "").Trim();
            string selectedCat = treeCategories.SelectedNode?.Name ?? "ALL";

            // 1. Фильтр по категории
            var filteredByCategory = (selectedCat == "ALL")
                ? _allData
                : _allData.Where(x => (string.IsNullOrWhiteSpace(x.Category) ? "Без категории" : x.Category) == selectedCat);

            // 2. Фильтр по поиску
            var finalFiltered = string.IsNullOrWhiteSpace(q)
                ? filteredByCategory
                : filteredByCategory.Where(x => x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               x.BaseFolder.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);

            _items.Clear();
            foreach (var a in finalFiltered) _items.Add(a);

            grid.DataSource = _items;
            lblCount.Text = $"Секвенций: {_items.Count}";
        }

        private void PreselectIfPossible()
        {
            if (string.IsNullOrWhiteSpace(_preselectName) || _preselectName == "-") return;

            // Пытаемся найти и выделить строку с текущей секвенцией
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.DataBoundItem is ImposingConfig cfg && cfg.Name == _preselectName)
                {
                    row.Selected = true;
                    grid.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        private void SelectCurrent()
        {
            if (grid.CurrentRow?.DataBoundItem is ImposingConfig cfg)
            {
                SelectedName = cfg.Name;
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (grid.Rows.Count == 0 && string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                // Если список пуст и ничего не выбрано, возвращаем прочерк
                SelectedName = "-";
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
            grid.AllowUserToAddRows = false;
            grid.Columns.Clear();

            // Оставляем только одну колонку с названием
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Name",
                HeaderText = "Сценарий (Sequence)",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
        }
    }
}