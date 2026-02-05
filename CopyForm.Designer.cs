namespace MyManager
{
    partial class CopyForm
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
            LayoutSize = new Label();
            textBoxSize = new TextBox();
            label2 = new Label();
            btnCancel = new Button();
            btnOk = new Button();
            textBoxSuffix = new TextBox();
            dateCreate = new Label();
            SuspendLayout();
            // 
            // LayoutSize
            // 
            LayoutSize.AutoSize = true;
            LayoutSize.Location = new Point(66, 67);
            LayoutSize.Margin = new Padding(2, 0, 2, 0);
            LayoutSize.Name = "LayoutSize";
            LayoutSize.Size = new Size(47, 15);
            LayoutSize.TabIndex = 13;
            LayoutSize.Text = "Размер";
            // 
            // textBoxSize
            // 
            textBoxSize.Location = new Point(126, 59);
            textBoxSize.Margin = new Padding(2, 2, 2, 2);
            textBoxSize.Name = "textBoxSize";
            textBoxSize.Size = new Size(308, 23);
            textBoxSize.TabIndex = 12;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(185, 20);
            label2.Margin = new Padding(2, 0, 2, 0);
            label2.Name = "label2";
            label2.Size = new Size(104, 15);
            label2.TabIndex = 11;
            label2.Text = "Копировать файл";
            // 
            // btnCancel
            // 
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(356, 139);
            btnCancel.Margin = new Padding(2, 2, 2, 2);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(78, 29);
            btnCancel.TabIndex = 10;
            btnCancel.Text = "Отмена";
            btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnOk
            // 
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Location = new Point(274, 139);
            btnOk.Margin = new Padding(2, 2, 2, 2);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(78, 29);
            btnOk.TabIndex = 9;
            btnOk.Text = "Сохранить";
            btnOk.UseVisualStyleBackColor = true;
            // 
            // textBoxSuffix
            // 
            textBoxSuffix.Location = new Point(125, 90);
            textBoxSuffix.Margin = new Padding(2, 2, 2, 2);
            textBoxSuffix.Name = "textBoxSuffix";
            textBoxSuffix.Size = new Size(308, 23);
            textBoxSuffix.TabIndex = 8;
            // 
            // dateCreate
            // 
            dateCreate.AutoSize = true;
            dateCreate.Location = new Point(82, 98);
            dateCreate.Margin = new Padding(2, 0, 2, 0);
            dateCreate.Name = "dateCreate";
            dateCreate.Size = new Size(31, 15);
            dateCreate.TabIndex = 7;
            dateCreate.Text = "Имя";
            // 
            // CopyForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(464, 199);
            Controls.Add(LayoutSize);
            Controls.Add(textBoxSize);
            Controls.Add(label2);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);
            Controls.Add(textBoxSuffix);
            Controls.Add(dateCreate);
            Margin = new Padding(2, 2, 2, 2);
            Name = "CopyForm";
            Text = "CopyForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label LayoutSize;
        private TextBox textBoxSize;
        private Label label2;
        private Button btnCancel;
        private Button btnOk;
        private TextBox textBoxSuffix;
        private Label dateCreate;
    }
}