namespace MyManager
{
    partial class ImposingManagerForm
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
            txtName = new TextBox();
            txtBaseFolder = new TextBox();
            btnBrowseBase = new Button();
            btnCreateBasic = new Button();
            txtIn = new TextBox();
            txtOut = new TextBox();
            txtDone = new TextBox();
            txtError = new TextBox();
            btnOk = new Button();
            dataGridView1 = new DataGridView();
            inputLabel = new Label();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            buttonDeleteSequance = new Button();
            buttonCreateSequance = new Button();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            SuspendLayout();
            // 
            // txtName
            // 
            txtName.Location = new Point(290, 623);
            txtName.Name = "txtName";
            txtName.Size = new Size(605, 31);
            txtName.TabIndex = 1;
            // 
            // txtBaseFolder
            // 
            txtBaseFolder.Location = new Point(290, 560);
            txtBaseFolder.Name = "txtBaseFolder";
            txtBaseFolder.Size = new Size(605, 31);
            txtBaseFolder.TabIndex = 2;
            // 
            // btnBrowseBase
            // 
            btnBrowseBase.Location = new Point(916, 558);
            btnBrowseBase.Name = "btnBrowseBase";
            btnBrowseBase.Size = new Size(291, 34);
            btnBrowseBase.TabIndex = 3;
            btnBrowseBase.Text = "Указать путь к папке";
            btnBrowseBase.UseVisualStyleBackColor = true;
            // 
            // btnCreateBasic
            // 
            btnCreateBasic.Location = new Point(916, 623);
            btnCreateBasic.Name = "btnCreateBasic";
            btnCreateBasic.Size = new Size(291, 34);
            btnCreateBasic.TabIndex = 4;
            btnCreateBasic.Text = "Создать базовые папки";
            btnCreateBasic.UseVisualStyleBackColor = true;
            // 
            // txtIn
            // 
            txtIn.Location = new Point(49, 933);
            txtIn.Name = "txtIn";
            txtIn.Size = new Size(680, 31);
            txtIn.TabIndex = 5;
            // 
            // txtOut
            // 
            txtOut.Location = new Point(49, 1022);
            txtOut.Name = "txtOut";
            txtOut.Size = new Size(680, 31);
            txtOut.TabIndex = 6;
            // 
            // txtDone
            // 
            txtDone.Location = new Point(49, 1110);
            txtDone.Name = "txtDone";
            txtDone.Size = new Size(680, 31);
            txtDone.TabIndex = 7;
            // 
            // txtError
            // 
            txtError.Location = new Point(49, 1198);
            txtError.Name = "txtError";
            txtError.Size = new Size(680, 31);
            txtError.TabIndex = 8;
            // 
            // btnOk
            // 
            btnOk.Location = new Point(916, 1327);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(291, 34);
            btnOk.TabIndex = 9;
            btnOk.Text = "Закрыть";
            btnOk.UseVisualStyleBackColor = true;
            // 
            // dataGridView1
            // 
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Location = new Point(49, 40);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.RowHeadersWidth = 62;
            dataGridView1.RowTemplate.Height = 33;
            dataGridView1.Size = new Size(1157, 472);
            dataGridView1.TabIndex = 11;
            // 
            // inputLabel
            // 
            inputLabel.AutoSize = true;
            inputLabel.Location = new Point(49, 894);
            inputLabel.Name = "inputLabel";
            inputLabel.Size = new Size(225, 25);
            inputLabel.TabIndex = 20;
            inputLabel.Text = "Input Folder / Original PDF";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(49, 985);
            label1.Name = "label1";
            label1.Size = new Size(260, 25);
            label1.TabIndex = 21;
            label1.Text = "Output Folder / Imposed result";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(49, 1161);
            label2.Name = "label2";
            label2.Size = new Size(105, 25);
            label2.TabIndex = 22;
            label2.Text = "Error Folder";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(49, 1073);
            label3.Name = "label3";
            label3.Size = new Size(226, 25);
            label3.TabIndex = 23;
            label3.Text = "Done Folder / Original PDF";
            // 
            // buttonDeleteSequance
            // 
            buttonDeleteSequance.Location = new Point(604, 687);
            buttonDeleteSequance.Name = "buttonDeleteSequance";
            buttonDeleteSequance.Size = new Size(291, 34);
            buttonDeleteSequance.TabIndex = 24;
            buttonDeleteSequance.Text = "Удалить сценарий";
            buttonDeleteSequance.UseVisualStyleBackColor = true;
            // 
            // buttonCreateSequance
            // 
            buttonCreateSequance.Location = new Point(916, 687);
            buttonCreateSequance.Name = "buttonCreateSequance";
            buttonCreateSequance.Size = new Size(291, 34);
            buttonCreateSequance.TabIndex = 25;
            buttonCreateSequance.Text = "Создать сценарий";
            buttonCreateSequance.UseVisualStyleBackColor = true;
            // 
            // ImposingManagerForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1246, 1421);
            Controls.Add(buttonCreateSequance);
            Controls.Add(buttonDeleteSequance);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(inputLabel);
            Controls.Add(dataGridView1);
            Controls.Add(btnOk);
            Controls.Add(txtError);
            Controls.Add(txtDone);
            Controls.Add(txtOut);
            Controls.Add(txtIn);
            Controls.Add(btnCreateBasic);
            Controls.Add(btnBrowseBase);
            Controls.Add(txtBaseFolder);
            Controls.Add(txtName);
            Name = "ImposingManagerForm";
            Text = "ImposingManagerForm";
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private TextBox txtName;
        private TextBox txtBaseFolder;
        private Button btnBrowseBase;
        private Button btnCreateBasic;
        private TextBox txtIn;
        private TextBox txtOut;
        private TextBox txtDone;
        private TextBox txtError;
        private Button btnOk;
        private DataGridView dataGridView1;
        private Label inputLabel;
        private Label label1;
        private Label label2;
        private Label label3;
        private Button buttonDeleteSequance;
        private Button buttonCreateSequance;
    }
}