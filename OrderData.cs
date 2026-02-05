using System;

namespace MyManager
{
    public class OrderData
    {
        public string Id { get; set; } = "";

        public string Keyword { get; set; } = ""; // Новое поле
        public DateTime OrderDate { get; set; } = DateTime.Now;
        public string FolderName { get; set; } = ""; // Например: "23_10_25 №123"
        public string Status { get; set; } = "⚪ Ожидание";

        // Храним ПОЛНЫЕ пути к файлам
        public string SourcePath { get; set; } = "";
        public string PreparedPath { get; set; } = "";
        public string PrintPath { get; set; } = "";

        // Настройки автоматизации
        public string PitStopAction { get; set; } = "-";
        public string ImposingAction { get; set; } = "-";
    }
}