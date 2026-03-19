using System;
using System.Collections.Generic;

namespace Replica
{
    public class OrderFileItem
    {
        public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

        // Бизнес-представление
        public string ClientFileLabel { get; set; } = "";
        public string Variant { get; set; } = "";

        // Сквозные стадии
        public string SourcePath { get; set; } = "";
        public long? SourceFileSizeBytes { get; set; }
        public string PreparedPath { get; set; } = "";
        public long? PreparedFileSizeBytes { get; set; }
        public string PrintPath { get; set; } = "";
        public long? PrintFileSizeBytes { get; set; }

        // Технические вложения
        public List<string> TechnicalFiles { get; set; } = new();

        // Состояния
        public string FileStatus { get; set; } = WorkflowStatusNames.Waiting;
        public string LastReason { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Локальные операции item (если пусто/"-", используются операции заказа)
        public string PitStopAction { get; set; } = "-";
        public string ImposingAction { get; set; } = "-";

        // Порядок поступления (FIFO)
        public long SequenceNo { get; set; }
    }
}
