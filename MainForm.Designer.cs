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
            pnlServersHeader = new Panel();
            tsMainActions = new ToolStrip();
            tsbNewJob = new ToolStripButton();
            tsbRun = new ToolStripButton();
            tsbStop = new ToolStripButton();
            tsbRemove = new ToolStripButton();
            tsbBrowse = new ToolStripButton();
            tsbConsole = new ToolStripButton();
            tsbConfig = new ToolStripButton();
            splitContainer1 = new SplitContainer();
            ((System.ComponentModel.ISupportInitialize)scMain).BeginInit();
            scMain.Panel1.SuspendLayout();
            scMain.Panel2.SuspendLayout();
            scMain.SuspendLayout();
            tsMainActions.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.SuspendLayout();
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
            scMain.Panel1.Controls.Add(pnlServersHeader);
            // 
            // scMain.Panel2
            // 
            scMain.Panel2.Controls.Add(splitContainer1);
            scMain.Panel2.Controls.Add(tsMainActions);
            scMain.Size = new Size(2213, 1244);
            scMain.SplitterDistance = 460;
            scMain.TabIndex = 1;
            // 
            // treeView1
            // 
            treeView1.Dock = DockStyle.Fill;
            treeView1.Location = new Point(0, 150);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(460, 1094);
            treeView1.TabIndex = 1;
            // 
            // pnlServersHeader
            // 
            pnlServersHeader.Dock = DockStyle.Top;
            pnlServersHeader.Location = new Point(0, 0);
            pnlServersHeader.Name = "pnlServersHeader";
            pnlServersHeader.Size = new Size(460, 150);
            pnlServersHeader.TabIndex = 0;
            // 
            // tsMainActions
            // 
            tsMainActions.ImageScalingSize = new Size(24, 24);
            tsMainActions.Items.AddRange(new ToolStripItem[] { tsbNewJob, tsbRun, tsbStop, tsbRemove, tsbBrowse, tsbConsole, tsbConfig });
            tsMainActions.Location = new Point(0, 0);
            tsMainActions.Name = "tsMainActions";
            tsMainActions.Size = new Size(1749, 34);
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
            // tsbConfig
            // 
            tsbConfig.Alignment = ToolStripItemAlignment.Right;
            tsbConfig.DisplayStyle = ToolStripItemDisplayStyle.Text;
            tsbConfig.Name = "tsbConfig";
            tsbConfig.Size = new Size(34, 29);
            tsbConfig.Text = "?";
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 34);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Size = new Size(1749, 1210);
            splitContainer1.SplitterDistance = 583;
            splitContainer1.TabIndex = 1;
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
            tsMainActions.ResumeLayout(false);
            tsMainActions.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel pnlSidebar;
        private System.Windows.Forms.SplitContainer scMain;
        private System.Windows.Forms.Panel pnlServersHeader;
        private System.Windows.Forms.TreeView treeView1;
        private ToolStrip tsMainActions;
        private ToolStripButton tsbNewJob;
        private ToolStripButton tsbRun;
        private ToolStripButton tsbStop;
        private ToolStripButton tsbRemove;
        private ToolStripButton tsbBrowse;
        private ToolStripButton tsbConsole;
        private ToolStripButton tsbConfig;
        private SplitContainer splitContainer1;
    }
}
