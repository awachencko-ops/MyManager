namespace MyManager
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            pnlSidebar = new Panel();
            scMain = new SplitContainer();
            treeView1 = new TreeView();
            tableLayoutPanel1 = new TableLayoutPanel();
            pnlHeader = new Panel();
            btnViewTiles = new Button();
            btnViewList = new Button();
            tbSearch = new TextBox();
            cbQueue = new ComboBox();
            pnlFilters = new Panel();
            flpFilters = new FlowLayoutPanel();
            picFStatusGlyph = new PictureBox();
            lblFStatus = new Label();
            cbFOrderNo = new ComboBox();
            cbUser = new ComboBox();
            cbFCreated = new ComboBox();
            cbFReceived = new ComboBox();
            dgvJobs = new DataGridView();
            colStatus = new DataGridViewTextBoxColumn();
            colOrderNumber = new DataGridViewTextBoxColumn();
            colSource = new DataGridViewTextBoxColumn();
            colPrep = new DataGridViewTextBoxColumn();
            colPitstop = new DataGridViewTextBoxColumn();
            colHotimposing = new DataGridViewTextBoxColumn();
            colPrint = new DataGridViewTextBoxColumn();
            colReceived = new DataGridViewTextBoxColumn();
            colCreated = new DataGridViewTextBoxColumn();
            tsMainActions = new ToolStrip();
            tsbNewJob = new ToolStripButton();
            tsbRun = new ToolStripButton();
            tsbStop = new ToolStripButton();
            tsbRemove = new ToolStripButton();
            tsbBrowse = new ToolStripButton();
            tsbConsole = new ToolStripButton();
            tsbParameters = new ToolStripButton();
            ((System.ComponentModel.ISupportInitialize)scMain).BeginInit();
            scMain.Panel1.SuspendLayout();
            scMain.Panel2.SuspendLayout();
            scMain.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            pnlHeader.SuspendLayout();
            pnlFilters.SuspendLayout();
            flpFilters.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picFStatusGlyph).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dgvJobs).BeginInit();
            tsMainActions.SuspendLayout();
            SuspendLayout();
            // 
            // pnlSidebar
            // 
            pnlSidebar.Dock = DockStyle.Left;
            pnlSidebar.Location = new Point(0, 0);
            pnlSidebar.Name = "pnlSidebar";
            pnlSidebar.Size = new Size(65, 1244);
            pnlSidebar.TabIndex = 0;
            // 
            // scMain
            // 
            scMain.Dock = DockStyle.Fill;
            scMain.Location = new Point(65, 0);
            scMain.Name = "scMain";
            // 
            // scMain.Panel1
            // 
            scMain.Panel1.Controls.Add(treeView1);
            // 
            // scMain.Panel2
            // 
            scMain.Panel2.Controls.Add(tableLayoutPanel1);
            scMain.Panel2.Controls.Add(tsMainActions);
            scMain.Size = new Size(2213, 1244);
            scMain.SplitterDistance = 460;
            scMain.TabIndex = 1;
            // 
            // treeView1
            // 
            treeView1.Dock = DockStyle.Fill;
            treeView1.HideSelection = false;
            treeView1.Location = new Point(0, 0);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(460, 1244);
            treeView1.TabIndex = 1;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(pnlHeader, 0, 0);
            tableLayoutPanel1.Controls.Add(pnlFilters, 0, 1);
            tableLayoutPanel1.Controls.Add(dgvJobs, 0, 2);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 46);
            tableLayoutPanel1.Margin = new Padding(0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.Size = new Size(1749, 1198);
            tableLayoutPanel1.TabIndex = 1;
            // 
            // pnlHeader
            // 
            pnlHeader.Controls.Add(btnViewTiles);
            pnlHeader.Controls.Add(btnViewList);
            pnlHeader.Controls.Add(tbSearch);
            pnlHeader.Controls.Add(cbQueue);
            pnlHeader.Dock = DockStyle.Fill;
            pnlHeader.Location = new Point(3, 3);
            pnlHeader.Name = "pnlHeader";
            pnlHeader.Size = new Size(1743, 36);
            pnlHeader.TabIndex = 0;
            // 
            // btnViewTiles
            // 
            btnViewTiles.Location = new Point(1700, 3);
            btnViewTiles.Name = "btnViewTiles";
            btnViewTiles.Size = new Size(34, 33);
            btnViewTiles.TabIndex = 3;
            btnViewTiles.Text = "▦";
            btnViewTiles.UseVisualStyleBackColor = true;
            // 
            // btnViewList
            // 
            btnViewList.Location = new Point(1665, 3);
            btnViewList.Name = "btnViewList";
            btnViewList.Size = new Size(34, 33);
            btnViewList.TabIndex = 2;
            btnViewList.Text = "≡";
            btnViewList.UseVisualStyleBackColor = true;
            // 
            // tbSearch
            // 
            tbSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            tbSearch.Location = new Point(1357, 2);
            tbSearch.Name = "tbSearch";
            tbSearch.Size = new Size(240, 31);
            tbSearch.TabIndex = 1;
            // 
            // cbQueue
            // 
            cbQueue.DropDownStyle = ComboBoxStyle.DropDownList;
            cbQueue.FormattingEnabled = true;
            cbQueue.IntegralHeight = false;
            cbQueue.Location = new Point(3, 3);
            cbQueue.Name = "cbQueue";
            cbQueue.Size = new Size(170, 33);
            cbQueue.TabIndex = 0;
            // 
            // pnlFilters
            // 
            pnlFilters.Controls.Add(flpFilters);
            pnlFilters.Dock = DockStyle.Fill;
            pnlFilters.Location = new Point(3, 45);
            pnlFilters.Name = "pnlFilters";
            pnlFilters.Size = new Size(1743, 36);
            pnlFilters.TabIndex = 1;
            // 
            // flpFilters
            // 
            flpFilters.AutoScroll = true;
            flpFilters.Controls.Add(picFStatusGlyph);
            flpFilters.Controls.Add(lblFStatus);
            flpFilters.Controls.Add(cbFOrderNo);
            flpFilters.Controls.Add(cbUser);
            flpFilters.Controls.Add(cbFCreated);
            flpFilters.Controls.Add(cbFReceived);
            flpFilters.Dock = DockStyle.Fill;
            flpFilters.Location = new Point(0, 0);
            flpFilters.Name = "flpFilters";
            flpFilters.Size = new Size(1743, 36);
            flpFilters.TabIndex = 0;
            flpFilters.WrapContents = false;
            // 
            // picFStatusGlyph
            // 
            picFStatusGlyph.Cursor = Cursors.Hand;
            picFStatusGlyph.Location = new Point(3, 0);
            picFStatusGlyph.Margin = new Padding(3, 0, 0, 0);
            picFStatusGlyph.Name = "picFStatusGlyph";
            picFStatusGlyph.Size = new Size(24, 33);
            picFStatusGlyph.SizeMode = PictureBoxSizeMode.CenterImage;
            picFStatusGlyph.TabIndex = 0;
            picFStatusGlyph.TabStop = false;
            // 
            // lblFStatus
            // 
            lblFStatus.Cursor = Cursors.Hand;
            lblFStatus.Location = new Point(27, 0);
            lblFStatus.Margin = new Padding(0, 0, 3, 0);
            lblFStatus.Name = "lblFStatus";
            lblFStatus.Size = new Size(196, 33);
            lblFStatus.TabIndex = 1;
            lblFStatus.Text = "Состояние задания";
            lblFStatus.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // cbFOrderNo
            // 
            cbFOrderNo.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFOrderNo.FormattingEnabled = true;
            cbFOrderNo.IntegralHeight = false;
            cbFOrderNo.Location = new Point(179, 3);
            cbFOrderNo.Name = "cbFOrderNo";
            cbFOrderNo.Size = new Size(170, 33);
            cbFOrderNo.TabIndex = 2;
            // 
            // cbUser
            // 
            cbUser.DropDownStyle = ComboBoxStyle.DropDownList;
            cbUser.FormattingEnabled = true;
            cbUser.IntegralHeight = false;
            cbUser.Location = new Point(355, 3);
            cbUser.Name = "cbUser";
            cbUser.Size = new Size(170, 33);
            cbUser.TabIndex = 3;
            // 
            // cbFCreated
            // 
            cbFCreated.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFCreated.FormattingEnabled = true;
            cbFCreated.IntegralHeight = false;
            cbFCreated.Location = new Point(531, 3);
            cbFCreated.Name = "cbFCreated";
            cbFCreated.Size = new Size(170, 33);
            cbFCreated.TabIndex = 4;
            // 
            // cbFReceived
            // 
            cbFReceived.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFReceived.FormattingEnabled = true;
            cbFReceived.IntegralHeight = false;
            cbFReceived.Location = new Point(707, 3);
            cbFReceived.Name = "cbFReceived";
            cbFReceived.Size = new Size(170, 33);
            cbFReceived.TabIndex = 5;
            // 
            // dgvJobs
            // 
            dgvJobs.AllowUserToAddRows = false;
            dgvJobs.AllowUserToDeleteRows = false;
            dgvJobs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvJobs.BackgroundColor = Color.White;
            dgvJobs.BorderStyle = BorderStyle.None;
            dgvJobs.CellBorderStyle = DataGridViewCellBorderStyle.None;
            dgvJobs.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvJobs.Columns.AddRange(new DataGridViewColumn[] { colStatus, colOrderNumber, colSource, colPrep, colPitstop, colHotimposing, colPrint, colReceived, colCreated });
            dgvJobs.Dock = DockStyle.Fill;
            dgvJobs.Location = new Point(3, 87);
            dgvJobs.Name = "dgvJobs";
            dgvJobs.ReadOnly = true;
            dgvJobs.RowHeadersVisible = false;
            dgvJobs.RowHeadersWidth = 62;
            dgvJobs.Size = new Size(1743, 1108);
            dgvJobs.TabIndex = 2;
            // 
            // colStatus
            // 
            colStatus.HeaderText = "Состояние";
            colStatus.MinimumWidth = 8;
            colStatus.Name = "colStatus";
            colStatus.ReadOnly = true;
            // 
            // colOrderNumber
            // 
            colOrderNumber.HeaderText = "№ заказа";
            colOrderNumber.MinimumWidth = 8;
            colOrderNumber.Name = "colOrderNumber";
            colOrderNumber.ReadOnly = true;
            // 
            // colSource
            // 
            colSource.HeaderText = "Исходные";
            colSource.MinimumWidth = 8;
            colSource.Name = "colSource";
            colSource.ReadOnly = true;
            colSource.Visible = false;
            // 
            // colPrep
            // 
            colPrep.HeaderText = "Заголовок задания";
            colPrep.MinimumWidth = 8;
            colPrep.Name = "colPrep";
            colPrep.ReadOnly = true;
            // 
            // colPitstop
            // 
            colPitstop.HeaderText = "Pitstop";
            colPitstop.MinimumWidth = 8;
            colPitstop.Name = "colPitstop";
            colPitstop.ReadOnly = true;
            // 
            // colHotimposing
            // 
            colHotimposing.HeaderText = "HotImposing";
            colHotimposing.MinimumWidth = 8;
            colHotimposing.Name = "colHotimposing";
            colHotimposing.ReadOnly = true;
            // 
            // colPrint
            // 
            colPrint.HeaderText = "Печать";
            colPrint.MinimumWidth = 8;
            colPrint.Name = "colPrint";
            colPrint.ReadOnly = true;
            // 
            // colReceived
            // 
            colReceived.HeaderText = "Начало обработки";
            colReceived.MinimumWidth = 8;
            colReceived.Name = "colReceived";
            colReceived.ReadOnly = true;
            // 
            // colCreated
            // 
            colCreated.HeaderText = "Дата поступления";
            colCreated.MinimumWidth = 8;
            colCreated.Name = "colCreated";
            colCreated.ReadOnly = true;
            // 
            // tsMainActions
            // 
            tsMainActions.ImageScalingSize = new Size(24, 24);
            tsMainActions.Items.AddRange(new ToolStripItem[] { tsbNewJob, tsbRun, tsbStop, tsbRemove, tsbBrowse, tsbConsole, tsbParameters });
            tsMainActions.Location = new Point(0, 0);
            tsMainActions.Name = "tsMainActions";
            tsMainActions.Padding = new Padding(6);
            tsMainActions.Size = new Size(1749, 46);
            tsMainActions.TabIndex = 0;
            tsMainActions.Text = "tsMainActions";
            tsMainActions.ItemClicked += TsMainActions_ItemClicked;
            // 
            // tsbNewJob
            // 
            tsbNewJob.Name = "tsbNewJob";
            tsbNewJob.Size = new Size(81, 29);
            tsbNewJob.Text = "Создать";
            // 
            // tsbRun
            // 
            tsbRun.Name = "tsbRun";
            tsbRun.Size = new Size(95, 29);
            tsbRun.Text = "Запустить";
            // 
            // tsbStop
            // 
            tsbStop.Name = "tsbStop";
            tsbStop.Size = new Size(111, 29);
            tsbStop.Text = "Остановить";
            // 
            // tsbRemove
            // 
            tsbRemove.Name = "tsbRemove";
            tsbRemove.Size = new Size(80, 29);
            tsbRemove.Text = "Удалить";
            // 
            // tsbBrowse
            // 
            tsbBrowse.Name = "tsbBrowse";
            tsbBrowse.Size = new Size(66, 29);
            tsbBrowse.Text = "Папка";
            // 
            // tsbConsole
            // 
            tsbConsole.Name = "tsbConsole";
            tsbConsole.Size = new Size(46, 29);
            tsbConsole.Text = "Лог";
            // 
            // tsbParameters
            // 
            tsbParameters.Name = "tsbParameters";
            tsbParameters.Size = new Size(111, 29);
            tsbParameters.Text = "Параметры";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(2278, 1244);
            Controls.Add(scMain);
            Controls.Add(pnlSidebar);
            Name = "MainForm";
            Text = "MainForm";
            scMain.Panel1.ResumeLayout(false);
            scMain.Panel2.ResumeLayout(false);
            scMain.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)scMain).EndInit();
            scMain.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            pnlHeader.ResumeLayout(false);
            pnlHeader.PerformLayout();
            pnlFilters.ResumeLayout(false);
            flpFilters.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)picFStatusGlyph).EndInit();
            ((System.ComponentModel.ISupportInitialize)dgvJobs).EndInit();
            tsMainActions.ResumeLayout(false);
            tsMainActions.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel pnlSidebar;
        private System.Windows.Forms.SplitContainer scMain;
        private System.Windows.Forms.TreeView treeView1;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel pnlHeader;
        private Panel pnlFilters;
        private DataGridView dgvJobs;
        private ComboBox cbQueue;
        private TextBox tbSearch;
        private Button btnViewTiles;
        private Button btnViewList;
        private FlowLayoutPanel flpFilters;
        private PictureBox picFStatusGlyph;
        private Label lblFStatus;
        private ComboBox cbFOrderNo;
        private ComboBox cbUser;
        private ComboBox cbFReceived;
        private DataGridViewTextBoxColumn colStatus;
        private DataGridViewTextBoxColumn colOrderNumber;
        private DataGridViewTextBoxColumn colSource;
        private DataGridViewTextBoxColumn colPrep;
        private DataGridViewTextBoxColumn colPitstop;
        private DataGridViewTextBoxColumn colHotimposing;
        private DataGridViewTextBoxColumn colPrint;
        private DataGridViewTextBoxColumn colReceived;
        private DataGridViewTextBoxColumn colCreated;
        private ToolStrip tsMainActions;
        private ToolStripButton tsbNewJob;
        private ToolStripButton tsbRun;
        private ToolStripButton tsbStop;
        private ToolStripButton tsbRemove;
        private ToolStripButton tsbBrowse;
        private ToolStripButton tsbConsole;
        private ToolStripButton tsbParameters;
        private ComboBox cbFCreated;
    }
}
