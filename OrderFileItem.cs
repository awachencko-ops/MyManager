using System;
using System.Collections.Generic;

namespace MyManager
{
    public class OrderFileItem
    {
        public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

        // Бизнес-представление
        public string ClientFileLabel { get; set; } = "";
        public string Variant { get; set; } = "";

        // Сквозные стадии
        public string SourcePath { get; set; } = "";
        public string PreparedPath { get; set; } = "";
        public string PrintPath { get; set; } = "";

        // Технические вложения
        public List<string> TechnicalFiles { get; set; } = new();

        // Состояния
        public string FileStatus { get; set; } = "⚪ Ожидание";
        public string LastReason { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Порядок поступления (FIFO)
        public long SequenceNo { get; set; }
    }
}
