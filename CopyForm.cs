using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MyManager
{
    public partial class CopyForm : Form
    {
        public string ResultName { get; private set; } = "";

        private readonly string _keyword;
        private readonly string _ext;

        public CopyForm(string keyword, string ext)
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterParent;

            _keyword = (keyword ?? "").Trim();
            _ext = string.IsNullOrWhiteSpace(ext) ? ".pdf" : ext;

            this.Text = "Копировать в подготовку";

            // Автоподстановка: keyword в поле "Имя" (у тебя textBoxSuffix)
            textBoxSuffix.Text = SanitizeKeyword(_keyword);

            btnOk.Click += BtnOk_Click;
            btnCancel.Click += (s, e) => this.Close();
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            string size = (textBoxSize.Text ?? "").Trim();
            string kw = (textBoxSuffix.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(size))
            {
                MessageBox.Show("Введите размер!");
                return;
            }

            if (string.IsNullOrWhiteSpace(kw))
            {
                MessageBox.Show("Keyword пустой!");
                return;
            }

            // размер тоже чистим
            size = SanitizePart(size);
            kw = SanitizePart(kw);

            // Имя: size_keyword.pdf
            ResultName = $"{size}_{kw}{_ext}";

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private static string SanitizeKeyword(string s)
        {
            // пробелы -> _
            s = (s ?? "").Trim().Replace(" ", "_");
            return SanitizePart(s);
        }

        private static string SanitizePart(string s)
        {
            // убираем запрещённые символы для Windows имён файлов
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Where(ch => !invalid.Contains(ch)).ToArray());

            // на всякий случай схлопнем двойные "__"
            while (cleaned.Contains("__")) cleaned = cleaned.Replace("__", "_");

            return cleaned.Trim('_');
        }
    }
}
