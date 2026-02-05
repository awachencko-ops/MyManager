namespace MyManager
{
    partial class PitStopSelectForm
    {
        private System.ComponentModel.IContainer components = null;
        private DataGridView grid;
        private TextBox txtSearch;
        private Button btnSelect;
        private Button btnCancel;
        private Label lblCount;
        private Label lblSearch;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            grid = new DataGridView();
            txtSearch = new TextBox();
            btnSelect = new Button();
            btnCancel = new Button();
            lblCount = new Label();
            lblSearch = new Label();
            ((System.ComponentModel.ISupportInitialize)grid).BeginInit();
            SuspendLayout();
            // 
            // grid
            // 
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.ColumnHeadersHeight = 34;
            grid.Location = new Point(34, 69);
            grid.Name = "grid";
            grid.RowHeadersWidth = 62;
            grid.Size = new Size(724, 512);
            grid.TabIndex = 2;
            // 
            // txtSearch
            // 
            txtSearch.Location = new Point(107, 28);
            txtSearch.Name = "txtSearch";
            txtSearch.Size = new Size(651, 31);
            txtSearch.TabIndex = 1;
            // 
            // btnSelect
            // 
            btnSelect.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSelect.Location = new Point(479, 606);
            btnSelect.Name = "btnSelect";
            btnSelect.Size = new Size(131, 36);
            btnSelect.TabIndex = 4;
            btnSelect.Text = "Выбрать";
            btnSelect.UseVisualStyleBackColor = true;
            // 
            // btnCancel
            // 
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Location = new Point(627, 606);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(131, 36);
            btnCancel.TabIndex = 5;
            btnCancel.Text = "Отмена";
            btnCancel.UseVisualStyleBackColor = true;
            // 
            // lblCount
            // 
            lblCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblCount.AutoSize = true;
            lblCount.Location = new Point(34, 612);
            lblCount.Name = "lblCount";
            lblCount.Size = new Size(121, 25);
            lblCount.TabIndex = 3;
            lblCount.Text = "Сценариев: 0";
            // 
            // lblSearch
            // 
            lblSearch.AutoSize = true;
            lblSearch.Location = new Point(34, 34);
            lblSearch.Name = "lblSearch";
            lblSearch.Size = new Size(67, 25);
            lblSearch.TabIndex = 0;
            lblSearch.Text = "Поиск:";
            // 
            // PitStopSelectForm
            // 
            ClientSize = new Size(784, 669);
            Controls.Add(btnCancel);
            Controls.Add(btnSelect);
            Controls.Add(lblCount);
            Controls.Add(grid);
            Controls.Add(txtSearch);
            Controls.Add(lblSearch);
            MinimumSize = new Size(700, 420);
            Name = "PitStopSelectForm";
            ((System.ComponentModel.ISupportInitialize)grid).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
