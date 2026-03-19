using System;
using System.Collections.Generic;
using System.Linq;

namespace Replica
{
    public enum OrderStartMode
    {
        Unknown = 0,
        Extended = 1,
        Simple = 2
    }

    // Маркер бизнес-топологии: "сингл-заказ" или "мульти-заказ".
    // Сингл-заказ — это частный случай групповой модели (ровно один item).
    public enum OrderFileTopologyMarker
    {
        Unknown = 0,
        SingleOrder = 1,
        MultiOrder = 2
    }

    public class OrderData
    {
        public static readonly DateTime PlaceholderOrderDate = new DateTime(2000, 1, 1);

        public string InternalId { get; set; } = Guid.NewGuid().ToString("N");
        public long StorageVersion { get; set; }
        public string Id { get; set; } = "";
        public OrderStartMode StartMode { get; set; } = OrderStartMode.Unknown;
        public OrderFileTopologyMarker FileTopologyMarker { get; set; } = OrderFileTopologyMarker.Unknown;

        public string Keyword { get; set; } = ""; // Новое поле
        public string UserName { get; set; } = "";
        public DateTime ArrivalDate { get; set; } = DateTime.Now; // Дата поступления заказа в препресс-менеджер
        public DateTime OrderDate { get; set; } = PlaceholderOrderDate; // Дата формирования заказа (ДелаемДело)
        public string FolderName { get; set; } = ""; // Например: "23_10_25 №123"
        public string Status { get; set; } = WorkflowStatusNames.Waiting;

        // Новый контейнер файлов заказа
        public List<OrderFileItem> Items { get; set; } = new();

        // Храним ПОЛНЫЕ пути к файлам (legacy, для обратной совместимости)
        public string SourcePath { get; set; } = "";
        public long? SourceFileSizeBytes { get; set; }
        public string SourceFileHash { get; set; } = "";
        public string PreparedPath { get; set; } = "";
        public long? PreparedFileSizeBytes { get; set; }
        public string PreparedFileHash { get; set; } = "";
        public string PrintPath { get; set; } = "";
        public long? PrintFileSizeBytes { get; set; }
        public string PrintFileHash { get; set; } = "";

        // Настройки автоматизации
        public string PitStopAction { get; set; } = "-";
        public string ImposingAction { get; set; } = "-";

        // Служебные данные по последнему переходу статуса
        public string LastStatusReason { get; set; } = "";
        public string LastStatusSource { get; set; } = "";
        public DateTime LastStatusAt { get; set; } = DateTime.Now;

        public int ItemsCount => Items?.Count ?? 0;
        public bool IsSingleOrderMarked => FileTopologyMarker == OrderFileTopologyMarker.SingleOrder;
        public bool IsMultiOrderMarked => FileTopologyMarker == OrderFileTopologyMarker.MultiOrder;

        public void RefreshAggregatedStatus()
        {
            if (Items == null || Items.Count == 0)
                return;

            var normalizedItems = Items.Where(x => x != null).ToList();
            if (normalizedItems.Count == 0)
                return;

            int total = normalizedItems.Count;
            int successCount = normalizedItems.Count(x =>
                string.Equals(x.FileStatus, WorkflowStatusNames.LegacyReady, StringComparison.Ordinal)
                || string.Equals(x.FileStatus, WorkflowStatusNames.Completed, StringComparison.Ordinal)
                || string.Equals(x.FileStatus, WorkflowStatusNames.Printed, StringComparison.Ordinal));
            int errorCount = normalizedItems.Count(x =>
                string.Equals(x.FileStatus, WorkflowStatusNames.LegacyError, StringComparison.Ordinal)
                || string.Equals(x.FileStatus, WorkflowStatusNames.Error, StringComparison.Ordinal));
            int inProgressCount = normalizedItems.Count(x =>
                (!string.IsNullOrWhiteSpace(x.FileStatus)
                 && x.FileStatus.Contains(WorkflowStatusNames.LegacyInWork, StringComparison.OrdinalIgnoreCase))
                || string.Equals(x.FileStatus, WorkflowStatusNames.Processing, StringComparison.Ordinal));
            int waitingCount = normalizedItems.Count(x =>
                !string.IsNullOrWhiteSpace(x.FileStatus) &&
                x.FileStatus.Contains("ожид", StringComparison.OrdinalIgnoreCase));

            if (errorCount == total)
                Status = WorkflowStatusNames.Error;
            else if (successCount == total)
                Status = WorkflowStatusNames.Completed;
            else if (waitingCount == total)
                Status = WorkflowStatusNames.Waiting;
            else if (inProgressCount > 0)
                Status = $"{WorkflowStatusNames.Processing} ({successCount + inProgressCount}/{total})";
            else
                Status = $"⚠ Частично готово ({successCount}/{total})";
        }
    }
}
