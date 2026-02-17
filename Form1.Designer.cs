namespace MyManager
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            DataGridViewCellStyle dataGridViewCellStyle1 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle2 = new DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            panel1 = new Panel();
            ButtonSettings = new Button();
            label1 = new Label();
            btnCreateOrder = new Button();
            gridOrders = new DataGridView();
            colState = new DataGridViewTextBoxColumn();
            colId = new DataGridViewTextBoxColumn();
            colSource = new DataGridViewTextBoxColumn();
            colReady = new DataGridViewTextBoxColumn();
            colPitStop = new DataGridViewTextBoxColumn();
            colImposing = new DataGridViewTextBoxColumn();
            colPrint = new DataGridViewTextBoxColumn();
            lblBottomStatus = new Label();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)gridOrders).BeginInit();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.BackColor = Color.IndianRed;
            panel1.Controls.Add(ButtonSettings);
            panel1.Controls.Add(label1);
            panel1.Dock = DockStyle.Top;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(2564, 60);
            panel1.TabIndex = 6;
            // 
            // ButtonSettings
            // 
            ButtonSettings.FlatAppearance.BorderSize = 0;
            ButtonSettings.FlatStyle = FlatStyle.Flat;
            ButtonSettings.ForeColor = Color.White;
            ButtonSettings.Location = new Point(213, 17);
            ButtonSettings.Name = "ButtonSettings";
            ButtonSettings.Size = new Size(111, 33);
            ButtonSettings.TabIndex = 2;
            ButtonSettings.Text = "Settings";
            ButtonSettings.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI Black", 9F, FontStyle.Bold, GraphicsUnit.Point);
            label1.ForeColor = Color.White;
            label1.Location = new Point(47, 22);
            label1.Name = "label1";
            label1.Size = new Size(119, 25);
            label1.TabIndex = 0;
            label1.Text = "MyManager";
            // 
            // btnCreateOrder
            // 
            btnCreateOrder.BackColor = Color.White;
            btnCreateOrder.FlatStyle = FlatStyle.Flat;
            btnCreateOrder.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
            btnCreateOrder.ForeColor = Color.Black;
            btnCreateOrder.Location = new Point(47, 100);
            btnCreateOrder.Name = "btnCreateOrder";
            btnCreateOrder.RightToLeft = RightToLeft.No;
            btnCreateOrder.Size = new Size(234, 52);
            btnCreateOrder.TabIndex = 3;
            btnCreateOrder.Text = "Создать заказ";
            btnCreateOrder.UseVisualStyleBackColor = false;
            // 
            // gridOrders
            // 
            gridOrders.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            gridOrders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridOrders.BackgroundColor = Color.White;
            gridOrders.BorderStyle = BorderStyle.None;
            gridOrders.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = Color.White;
            dataGridViewCellStyle1.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
            dataGridViewCellStyle1.ForeColor = Color.FromArgb(140, 140, 140);
            dataGridViewCellStyle1.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            gridOrders.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            gridOrders.ColumnHeadersHeight = 50;
            gridOrders.Columns.AddRange(new DataGridViewColumn[] { colState, colId, colSource, colReady, colPitStop, colImposing, colPrint });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = SystemColors.Window;
            dataGridViewCellStyle2.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            dataGridViewCellStyle2.ForeColor = Color.FromArgb(60, 60, 60);
            dataGridViewCellStyle2.Padding = new Padding(10, 0, 0, 0);
            dataGridViewCellStyle2.SelectionBackColor = Color.FromArgb(245, 247, 255);
            dataGridViewCellStyle2.SelectionForeColor = Color.Black;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.False;
            gridOrders.DefaultCellStyle = dataGridViewCellStyle2;
            gridOrders.EnableHeadersVisualStyles = false;
            gridOrders.GridColor = Color.FromArgb(230, 230, 230);
            gridOrders.Location = new Point(47, 178);
            gridOrders.Name = "gridOrders";
            gridOrders.RowHeadersVisible = false;
            gridOrders.RowHeadersWidth = 62;
            gridOrders.RowTemplate.Height = 50;
            gridOrders.Size = new Size(2463, 1083);
            gridOrders.TabIndex = 4;
            // 
            // colState
            // 
            colState.FillWeight = 50F;
            colState.HeaderText = "СОСТОЯНИЕ";
            colState.MinimumWidth = 8;
            colState.Name = "colState";
            colState.ReadOnly = true;
            // 
            // colId
            // 
            colId.HeaderText = "№ ЗАКАЗА";
            colId.MinimumWidth = 8;
            colId.Name = "colId";
            colId.ReadOnly = true;
            // 
            // colSource
            // 
            colSource.HeaderText = "ИСХОДНЫЕ";
            colSource.MinimumWidth = 8;
            colSource.Name = "colSource";
            colSource.ReadOnly = true;
            colSource.Resizable = DataGridViewTriState.True;
            colSource.SortMode = DataGridViewColumnSortMode.NotSortable;
            // 
            // colReady
            // 
            colReady.HeaderText = "ПОДГОТОВКА";
            colReady.MinimumWidth = 8;
            colReady.Name = "colReady";
            colReady.ReadOnly = true;
            colReady.Resizable = DataGridViewTriState.True;
            colReady.SortMode = DataGridViewColumnSortMode.NotSortable;
            // 
            // colPitStop
            // 
            colPitStop.HeaderText = "PITSTOP";
            colPitStop.MinimumWidth = 8;
            colPitStop.Name = "colPitStop";
            colPitStop.ReadOnly = true;
            colPitStop.Resizable = DataGridViewTriState.True;
            colPitStop.SortMode = DataGridViewColumnSortMode.NotSortable;
            // 
            // colImposing
            // 
            colImposing.HeaderText = "HOTIMPOSING";
            colImposing.MinimumWidth = 8;
            colImposing.Name = "colImposing";
            colImposing.ReadOnly = true;
            colImposing.Resizable = DataGridViewTriState.True;
            colImposing.SortMode = DataGridViewColumnSortMode.NotSortable;
            // 
            // colPrint
            // 
            colPrint.HeaderText = "ПЕЧАТЬ";
            colPrint.MinimumWidth = 8;
            colPrint.Name = "colPrint";
            colPrint.ReadOnly = true;
            colPrint.Resizable = DataGridViewTriState.True;
            colPrint.SortMode = DataGridViewColumnSortMode.NotSortable;
            // 
            // lblBottomStatus
            // 
            lblBottomStatus.AutoSize = true;
            lblBottomStatus.Location = new Point(47, 1294);
            lblBottomStatus.Name = "lblBottomStatus";
            lblBottomStatus.Size = new Size(158, 25);
            lblBottomStatus.TabIndex = 9;
            lblBottomStatus.Text = "Строка состояния";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(244, 245, 247);
            ClientSize = new Size(2564, 1368);
            Controls.Add(lblBottomStatus);
            Controls.Add(btnCreateOrder);
            Controls.Add(gridOrders);
            Controls.Add(panel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "MyManager";
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)gridOrders).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Panel panel1;
        private Button ButtonSettings;
        private Label label1;
        private Button btnCreateOrder;
        private DataGridView gridOrders;
        private Label lblBottomStatus;
        private DataGridViewTextBoxColumn colState;
        private DataGridViewTextBoxColumn colId;
        private DataGridViewTextBoxColumn colSource;
        private DataGridViewTextBoxColumn colReady;
        private DataGridViewTextBoxColumn colPitStop;
        private DataGridViewTextBoxColumn colImposing;
        private DataGridViewTextBoxColumn colPrint;
    }
}