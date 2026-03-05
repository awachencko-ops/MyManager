namespace MyManager
{
    partial class ActionManagerForm
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
            txtBaseFolder = new TextBox();
            btnBrowseBase = new Button();
            txtActionName = new TextBox();
            btnCreateBasic = new Button();
            txtInput = new TextBox();
            txtRepSucc = new TextBox();
            txtOrgSucc = new TextBox();
            txtProcSucc = new TextBox();
            txtProcErr = new TextBox();
            txtOrgErr = new TextBox();
            txtRepErr = new TextBox();
            txtNonPdfLogs = new TextBox();
            txtNonPdfFiles = new TextBox();
            btnOk = new Button();
            inputLabel = new Label();
            OnSuccessLabel1 = new Label();
            NonPDFLabel1 = new Label();
            Reports = new Label();
            OnSuccessLabel2 = new Label();
            OnSuccessLabel3 = new Label();
            OnErrorLabel1 = new Label();
            OnErrorLabel2 = new Label();
            OnErrorLabel3 = new Label();
            NonPDFLabel2 = new Label();
            OriginalDocuments = new Label();
            ProccesedDocuments = new Label();
            gridActions = new DataGridView();
            columnActionName = new DataGridViewTextBoxColumn();
            buttonCreateAction = new Button();
            buttonDeleteAction = new Button();
            ((System.ComponentModel.ISupportInitialize)gridActions).BeginInit();
            SuspendLayout();
            // 
            // txtBaseFolder
            // 
            txtBaseFolder.Location = new Point(290, 560);
            txtBaseFolder.Name = "txtBaseFolder";
            txtBaseFolder.Size = new Size(605, 31);
            txtBaseFolder.TabIndex = 1;
            // 
            // btnBrowseBase
            // 
            btnBrowseBase.Location = new Point(916, 558);
            btnBrowseBase.Name = "btnBrowseBase";
            btnBrowseBase.Size = new Size(291, 34);
            btnBrowseBase.TabIndex = 2;
            btnBrowseBase.Text = "Указать путь к папке";
            btnBrowseBase.UseVisualStyleBackColor = true;
            // 
            // txtActionName
            // 
            txtActionName.Location = new Point(290, 623);
            txtActionName.Name = "txtActionName";
            txtActionName.Size = new Size(605, 31);
            txtActionName.TabIndex = 6;
            txtActionName.Text = "Action Name";
            // 
            // btnCreateBasic
            // 
            btnCreateBasic.Location = new Point(916, 623);
            btnCreateBasic.Name = "btnCreateBasic";
            btnCreateBasic.Size = new Size(291, 34);
            btnCreateBasic.TabIndex = 7;
            btnCreateBasic.Text = "Создать базовые папки";
            btnCreateBasic.UseVisualStyleBackColor = true;
            // 
            // txtInput
            // 
            txtInput.Location = new Point(50, 854);
            txtInput.Name = "txtInput";
            txtInput.Size = new Size(834, 31);
            txtInput.TabIndex = 8;
            // 
            // txtRepSucc
            // 
            txtRepSucc.Location = new Point(50, 1020);
            txtRepSucc.Name = "txtRepSucc";
            txtRepSucc.Size = new Size(231, 31);
            txtRepSucc.TabIndex = 9;
            // 
            // txtOrgSucc
            // 
            txtOrgSucc.Location = new Point(360, 1020);
            txtOrgSucc.Name = "txtOrgSucc";
            txtOrgSucc.Size = new Size(231, 31);
            txtOrgSucc.TabIndex = 10;
            // 
            // txtProcSucc
            // 
            txtProcSucc.Location = new Point(653, 1020);
            txtProcSucc.Name = "txtProcSucc";
            txtProcSucc.Size = new Size(231, 31);
            txtProcSucc.TabIndex = 11;
            // 
            // txtProcErr
            // 
            txtProcErr.Location = new Point(653, 1110);
            txtProcErr.Name = "txtProcErr";
            txtProcErr.Size = new Size(231, 31);
            txtProcErr.TabIndex = 14;
            // 
            // txtOrgErr
            // 
            txtOrgErr.Location = new Point(360, 1110);
            txtOrgErr.Name = "txtOrgErr";
            txtOrgErr.Size = new Size(231, 31);
            txtOrgErr.TabIndex = 13;
            // 
            // txtRepErr
            // 
            txtRepErr.Location = new Point(50, 1110);
            txtRepErr.Name = "txtRepErr";
            txtRepErr.Size = new Size(231, 31);
            txtRepErr.TabIndex = 12;
            // 
            // txtNonPdfLogs
            // 
            txtNonPdfLogs.Location = new Point(50, 1197);
            txtNonPdfLogs.Name = "txtNonPdfLogs";
            txtNonPdfLogs.Size = new Size(231, 31);
            txtNonPdfLogs.TabIndex = 15;
            // 
            // txtNonPdfFiles
            // 
            txtNonPdfFiles.Location = new Point(360, 1197);
            txtNonPdfFiles.Name = "txtNonPdfFiles";
            txtNonPdfFiles.Size = new Size(231, 31);
            txtNonPdfFiles.TabIndex = 16;
            // 
            // btnOk
            // 
            btnOk.Location = new Point(916, 1327);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(291, 34);
            btnOk.TabIndex = 17;
            btnOk.Text = "Закрыть";
            btnOk.UseVisualStyleBackColor = true;
            // 
            // inputLabel
            // 
            inputLabel.AutoSize = true;
            inputLabel.Location = new Point(50, 815);
            inputLabel.Name = "inputLabel";
            inputLabel.Size = new Size(109, 25);
            inputLabel.TabIndex = 19;
            inputLabel.Text = "Input Folder";
            // 
            // OnSuccessLabel1
            // 
            OnSuccessLabel1.AutoSize = true;
            OnSuccessLabel1.Location = new Point(50, 981);
            OnSuccessLabel1.Name = "OnSuccessLabel1";
            OnSuccessLabel1.Size = new Size(102, 25);
            OnSuccessLabel1.TabIndex = 20;
            OnSuccessLabel1.Text = "On Success";
            // 
            // NonPDFLabel1
            // 
            NonPDFLabel1.AutoSize = true;
            NonPDFLabel1.Location = new Point(50, 1161);
            NonPDFLabel1.Name = "NonPDFLabel1";
            NonPDFLabel1.Size = new Size(85, 25);
            NonPDFLabel1.TabIndex = 26;
            NonPDFLabel1.Text = "Non-PDF";
            // 
            // Reports
            // 
            Reports.AutoSize = true;
            Reports.Location = new Point(50, 939);
            Reports.Name = "Reports";
            Reports.Size = new Size(73, 25);
            Reports.TabIndex = 28;
            Reports.Text = "Reports";
            // 
            // OnSuccessLabel2
            // 
            OnSuccessLabel2.AutoSize = true;
            OnSuccessLabel2.Location = new Point(360, 981);
            OnSuccessLabel2.Name = "OnSuccessLabel2";
            OnSuccessLabel2.Size = new Size(102, 25);
            OnSuccessLabel2.TabIndex = 29;
            OnSuccessLabel2.Text = "On Success";
            // 
            // OnSuccessLabel3
            // 
            OnSuccessLabel3.AutoSize = true;
            OnSuccessLabel3.Location = new Point(653, 981);
            OnSuccessLabel3.Name = "OnSuccessLabel3";
            OnSuccessLabel3.Size = new Size(102, 25);
            OnSuccessLabel3.TabIndex = 30;
            OnSuccessLabel3.Text = "On Success";
            // 
            // OnErrorLabel1
            // 
            OnErrorLabel1.AutoSize = true;
            OnErrorLabel1.Location = new Point(50, 1071);
            OnErrorLabel1.Name = "OnErrorLabel1";
            OnErrorLabel1.Size = new Size(79, 25);
            OnErrorLabel1.TabIndex = 31;
            OnErrorLabel1.Text = "On Error";
            // 
            // OnErrorLabel2
            // 
            OnErrorLabel2.AutoSize = true;
            OnErrorLabel2.Location = new Point(360, 1071);
            OnErrorLabel2.Name = "OnErrorLabel2";
            OnErrorLabel2.Size = new Size(79, 25);
            OnErrorLabel2.TabIndex = 32;
            OnErrorLabel2.Text = "On Error";
            // 
            // OnErrorLabel3
            // 
            OnErrorLabel3.AutoSize = true;
            OnErrorLabel3.Location = new Point(653, 1071);
            OnErrorLabel3.Name = "OnErrorLabel3";
            OnErrorLabel3.Size = new Size(79, 25);
            OnErrorLabel3.TabIndex = 33;
            OnErrorLabel3.Text = "On Error";
            // 
            // NonPDFLabel2
            // 
            NonPDFLabel2.AutoSize = true;
            NonPDFLabel2.Location = new Point(360, 1161);
            NonPDFLabel2.Name = "NonPDFLabel2";
            NonPDFLabel2.Size = new Size(85, 25);
            NonPDFLabel2.TabIndex = 34;
            NonPDFLabel2.Text = "Non-PDF";
            // 
            // OriginalDocuments
            // 
            OriginalDocuments.AutoSize = true;
            OriginalDocuments.Location = new Point(360, 939);
            OriginalDocuments.Name = "OriginalDocuments";
            OriginalDocuments.Size = new Size(170, 25);
            OriginalDocuments.TabIndex = 35;
            OriginalDocuments.Text = "Original Documents";
            // 
            // ProccesedDocuments
            // 
            ProccesedDocuments.AutoSize = true;
            ProccesedDocuments.Location = new Point(653, 939);
            ProccesedDocuments.Name = "ProccesedDocuments";
            ProccesedDocuments.Size = new Size(188, 25);
            ProccesedDocuments.TabIndex = 36;
            ProccesedDocuments.Text = "Proccesed Documents";
            // 
            // gridActions
            // 
            gridActions.AllowUserToAddRows = false;
            gridActions.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gridActions.Columns.AddRange(new DataGridViewColumn[] { columnActionName });
            gridActions.Location = new Point(50, 40);
            gridActions.MultiSelect = false;
            gridActions.Name = "gridActions";
            gridActions.ReadOnly = true;
            gridActions.RowHeadersWidth = 62;
            gridActions.RowTemplate.Height = 33;
            gridActions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridActions.Size = new Size(1157, 472);
            gridActions.TabIndex = 37;
            gridActions.CellContentClick += gridActions_CellContentClick;
            // 
            // columnActionName
            // 
            columnActionName.HeaderText = "Название";
            columnActionName.MinimumWidth = 8;
            columnActionName.Name = "columnActionName";
            columnActionName.ReadOnly = true;
            columnActionName.Resizable = DataGridViewTriState.True;
            columnActionName.Width = 150;
            // 
            // buttonCreateAction
            // 
            buttonCreateAction.Location = new Point(916, 687);
            buttonCreateAction.Name = "buttonCreateAction";
            buttonCreateAction.Size = new Size(291, 34);
            buttonCreateAction.TabIndex = 38;
            buttonCreateAction.Text = "Создать сценарий";
            buttonCreateAction.UseVisualStyleBackColor = true;
            // 
            // buttonDeleteAction
            // 
            buttonDeleteAction.Location = new Point(604, 687);
            buttonDeleteAction.Name = "buttonDeleteAction";
            buttonDeleteAction.Size = new Size(291, 34);
            buttonDeleteAction.TabIndex = 39;
            buttonDeleteAction.Text = "Удалить сценарий";
            buttonDeleteAction.UseVisualStyleBackColor = true;
            // 
            // ActionManagerForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1246, 1421);
            Controls.Add(buttonDeleteAction);
            Controls.Add(buttonCreateAction);
            Controls.Add(gridActions);
            Controls.Add(ProccesedDocuments);
            Controls.Add(OriginalDocuments);
            Controls.Add(NonPDFLabel2);
            Controls.Add(OnErrorLabel3);
            Controls.Add(OnErrorLabel2);
            Controls.Add(OnErrorLabel1);
            Controls.Add(OnSuccessLabel3);
            Controls.Add(OnSuccessLabel2);
            Controls.Add(Reports);
            Controls.Add(NonPDFLabel1);
            Controls.Add(OnSuccessLabel1);
            Controls.Add(inputLabel);
            Controls.Add(btnOk);
            Controls.Add(txtNonPdfFiles);
            Controls.Add(txtNonPdfLogs);
            Controls.Add(txtProcErr);
            Controls.Add(txtOrgErr);
            Controls.Add(txtRepErr);
            Controls.Add(txtProcSucc);
            Controls.Add(txtOrgSucc);
            Controls.Add(txtRepSucc);
            Controls.Add(txtInput);
            Controls.Add(btnCreateBasic);
            Controls.Add(txtActionName);
            Controls.Add(btnBrowseBase);
            Controls.Add(txtBaseFolder);
            Name = "ActionManagerForm";
            ((System.ComponentModel.ISupportInitialize)gridActions).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private TextBox txtBaseFolder;
        private Button btnBrowseBase;
        private TextBox txtActionName;
        private Button btnCreateBasic;
        private TextBox txtInput;
        private TextBox txtRepSucc;
        private TextBox txtOrgSucc;
        private TextBox txtProcSucc;
        private TextBox txtProcErr;
        private TextBox txtOrgErr;
        private TextBox txtRepErr;
        private TextBox txtNonPdfLogs;
        private TextBox txtNonPdfFiles;
        private Button btnOk;
        private Label inputLabel;
        private Label OnSuccessLabel1;
        private Label label8;
        private Label NonPDFLabel1;
        private Label Reports;
        private Label OnSuccessLabel2;
        private Label OnSuccessLabel3;
        private Label OnErrorLabel1;
        private Label OnErrorLabel2;
        private Label OnErrorLabel3;
        private Label NonPDFLabel2;
        private Label OriginalDocuments;
        private Label ProccesedDocuments;
        private DataGridView gridActions;
        private Button buttonCreateAction;
        private DataGridViewTextBoxColumn columnActionName;
        private Button buttonDeleteAction;
    }
}