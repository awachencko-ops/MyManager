namespace MyManager
{
    partial class OrderForm
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
            dateCreate = new Label();
            btnOk = new Button();
            btnCancel = new Button();
            label2 = new Label();
            label1 = new Label();
            label3 = new Label();
            label4 = new Label();
            textBoxNumberOrder = new TextBox();
            textOriginal = new TextBox();
            textPrepared = new TextBox();
            label5 = new Label();
            label6 = new Label();
            comboBoxPitStop = new ComboBox();
            comboBoxHotImposing = new ComboBox();
            label7 = new Label();
            textPrint = new TextBox();
            label8 = new Label();
            dateTimeOrder = new DateTimePicker();
            btnSelectOriginal = new Button();
            btnSelectPrepared = new Button();
            btnSelectPrint = new Button();
            buttonCreateFolder = new Button();
            textFolder = new TextBox();
            label9 = new Label();
            textKey = new TextBox();
            label10 = new Label();
            SuspendLayout();
            // 
            // dateCreate
            // 
            dateCreate.AutoSize = true;
            dateCreate.Location = new Point(30, 127);
            dateCreate.Name = "dateCreate";
            dateCreate.Size = new Size(0, 25);
            dateCreate.TabIndex = 0;
            // 
            // btnOk
            // 
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Location = new Point(555, 910);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(162, 35);
            btnOk.TabIndex = 2;
            btnOk.Text = "Сохранить";
            btnOk.UseVisualStyleBackColor = true;
            btnOk.Click += btnOk_Click;
            // 
            // btnCancel
            // 
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(734, 910);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(162, 35);
            btnCancel.TabIndex = 3;
            btnCancel.Text = "Отмена";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += btnCancel_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(380, 51);
            label2.Name = "label2";
            label2.Size = new Size(203, 25);
            label2.TabIndex = 4;
            label2.Text = "Редактирование заказа";
            label2.Click += label2_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(38, 154);
            label1.Name = "label1";
            label1.Size = new Size(238, 25);
            label1.TabIndex = 5;
            label1.Text = "Дата формирования заказа (ДелаемДело)";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(185, 201);
            label3.Name = "label3";
            label3.Size = new Size(91, 25);
            label3.TabIndex = 6;
            label3.Text = "№ Заказа";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(136, 503);
            label4.Name = "label4";
            label4.Size = new Size(140, 25);
            label4.TabIndex = 7;
            label4.Text = "Исходный файл";
            // 
            // textBoxNumberOrder
            // 
            textBoxNumberOrder.Location = new Point(283, 198);
            textBoxNumberOrder.Margin = new Padding(4, 5, 4, 5);
            textBoxNumberOrder.Name = "textBoxNumberOrder";
            textBoxNumberOrder.Size = new Size(284, 31);
            textBoxNumberOrder.TabIndex = 9;
            // 
            // textOriginal
            // 
            textOriginal.Location = new Point(283, 500);
            textOriginal.Margin = new Padding(4, 5, 4, 5);
            textOriginal.Name = "textOriginal";
            textOriginal.Size = new Size(550, 31);
            textOriginal.TabIndex = 10;
            // 
            // textPrepared
            // 
            textPrepared.Location = new Point(283, 550);
            textPrepared.Margin = new Padding(4, 5, 4, 5);
            textPrepared.Name = "textPrepared";
            textPrepared.Size = new Size(550, 31);
            textPrepared.TabIndex = 12;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(78, 556);
            label5.Name = "label5";
            label5.Size = new Size(198, 25);
            label5.TabIndex = 11;
            label5.Text = "Подготовленный файл";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(207, 693);
            label6.Name = "label6";
            label6.Size = new Size(69, 25);
            label6.TabIndex = 13;
            label6.Text = "PitStop";
            // 
            // comboBoxPitStop
            // 
            comboBoxPitStop.FormattingEnabled = true;
            comboBoxPitStop.Location = new Point(283, 690);
            comboBoxPitStop.Margin = new Padding(4, 5, 4, 5);
            comboBoxPitStop.Name = "comboBoxPitStop";
            comboBoxPitStop.Size = new Size(613, 33);
            comboBoxPitStop.TabIndex = 14;
            // 
            // comboBoxHotImposing
            // 
            comboBoxHotImposing.FormattingEnabled = true;
            comboBoxHotImposing.Location = new Point(283, 739);
            comboBoxHotImposing.Margin = new Padding(4, 5, 4, 5);
            comboBoxHotImposing.Name = "comboBoxHotImposing";
            comboBoxHotImposing.Size = new Size(613, 33);
            comboBoxHotImposing.TabIndex = 16;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(164, 742);
            label7.Name = "label7";
            label7.Size = new Size(118, 25);
            label7.TabIndex = 15;
            label7.Text = "HotImposing";
            // 
            // textPrint
            // 
            textPrint.Location = new Point(283, 602);
            textPrint.Margin = new Padding(4, 5, 4, 5);
            textPrint.Name = "textPrint";
            textPrint.Size = new Size(550, 31);
            textPrint.TabIndex = 18;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(134, 602);
            label8.Name = "label8";
            label8.Size = new Size(142, 25);
            label8.TabIndex = 17;
            label8.Text = "Печатный спуск";
            // 
            // dateTimeOrder
            // 
            dateTimeOrder.Location = new Point(283, 148);
            dateTimeOrder.Margin = new Padding(4, 5, 4, 5);
            dateTimeOrder.Name = "dateTimeOrder";
            dateTimeOrder.Size = new Size(284, 31);
            dateTimeOrder.TabIndex = 19;
            // 
            // btnSelectOriginal
            // 
            btnSelectOriginal.Location = new Point(840, 500);
            btnSelectOriginal.Name = "btnSelectOriginal";
            btnSelectOriginal.Size = new Size(56, 34);
            btnSelectOriginal.TabIndex = 20;
            btnSelectOriginal.Text = "...";
            btnSelectOriginal.UseVisualStyleBackColor = true;
            // 
            // btnSelectPrepared
            // 
            btnSelectPrepared.Location = new Point(840, 550);
            btnSelectPrepared.Name = "btnSelectPrepared";
            btnSelectPrepared.Size = new Size(56, 34);
            btnSelectPrepared.TabIndex = 21;
            btnSelectPrepared.Text = "...";
            btnSelectPrepared.UseVisualStyleBackColor = true;
            // 
            // btnSelectPrint
            // 
            btnSelectPrint.Location = new Point(840, 600);
            btnSelectPrint.Name = "btnSelectPrint";
            btnSelectPrint.Size = new Size(56, 34);
            btnSelectPrint.TabIndex = 22;
            btnSelectPrint.Text = "...";
            btnSelectPrint.UseVisualStyleBackColor = true;
            // 
            // buttonCreateFolder
            // 
            buttonCreateFolder.Location = new Point(283, 315);
            buttonCreateFolder.Name = "buttonCreateFolder";
            buttonCreateFolder.Size = new Size(550, 34);
            buttonCreateFolder.TabIndex = 25;
            buttonCreateFolder.Text = "Создать заказ";
            buttonCreateFolder.UseVisualStyleBackColor = true;
            buttonCreateFolder.Click += buttonCreateFolder_Click;
            // 
            // textFolder
            // 
            textFolder.Location = new Point(283, 443);
            textFolder.Margin = new Padding(4, 5, 4, 5);
            textFolder.Name = "textFolder";
            textFolder.Size = new Size(613, 31);
            textFolder.TabIndex = 24;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(133, 446);
            label9.Name = "label9";
            label9.Size = new Size(143, 25);
            label9.TabIndex = 23;
            label9.Text = "Папка хранения";
            // 
            // textKey
            // 
            textKey.Location = new Point(283, 239);
            textKey.Margin = new Padding(4, 5, 4, 5);
            textKey.Name = "textKey";
            textKey.Size = new Size(550, 31);
            textKey.TabIndex = 27;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(127, 242);
            label10.Name = "label10";
            label10.Size = new Size(149, 25);
            label10.TabIndex = 26;
            label10.Text = "Ключевое слово";
            // 
            // OrderForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(944, 1008);
            Controls.Add(textKey);
            Controls.Add(label10);
            Controls.Add(buttonCreateFolder);
            Controls.Add(textFolder);
            Controls.Add(label9);
            Controls.Add(btnSelectPrint);
            Controls.Add(btnSelectPrepared);
            Controls.Add(btnSelectOriginal);
            Controls.Add(dateTimeOrder);
            Controls.Add(textPrint);
            Controls.Add(label8);
            Controls.Add(comboBoxHotImposing);
            Controls.Add(label7);
            Controls.Add(comboBoxPitStop);
            Controls.Add(label6);
            Controls.Add(textPrepared);
            Controls.Add(label5);
            Controls.Add(textOriginal);
            Controls.Add(textBoxNumberOrder);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label1);
            Controls.Add(label2);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);
            Controls.Add(dateCreate);
            Name = "OrderForm";
            Text = "OrderForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label dateCreate;
        private Button btnOk;
        private Button btnCancel;
        private Label label2;
        private Label label1;
        private Label label3;
        private Label label4;
        private TextBox textBoxNumberOrder;
        private TextBox textOriginal;
        private TextBox textPrepared;
        private Label label5;
        private Label label6;
        private ComboBox comboBoxPitStop;
        private ComboBox comboBoxHotImposing;
        private Label label7;
        private TextBox textPrint;
        private Label label8;
        private DateTimePicker dateTimeOrder;
        private Button btnSelectOriginal;
        private Button btnSelectPrepared;
        private Button btnSelectPrint;
        private Button buttonCreateFolder;
        private TextBox textFolder;
        private Label label9;
        private TextBox textKey;
        private Label label10;
    }
}
