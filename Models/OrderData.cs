using System;
using System.Collections.Generic;
using System.Linq;

namespace MyManager
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
        public string InternalId { get; set; } = Guid.NewGuid().ToString("N");
        public string Id { get; set; } = "";
        public OrderStartMode StartMode { get; set; } = OrderStartMode.Unknown;
        public OrderFileTopologyMarker FileTopologyMarker { get; set; } = OrderFileTopologyMarker.Unknown;

        public string Keyword { get; set; } = ""; // Новое поле
        public DateTime ArrivalDate { get; set; } = DateTime.Now; // Дата поступления заказа в препресс-менеджер
        public DateTime OrderDate { get; set; } = DateTime.Now; // Дата формирования заказа (ДелаемДело)
        public string FolderName { get; set; } = ""; // Например: "23_10_25 №123"
        public string Status { get; set; } = "Ожидание";

        // Новый контейнер файлов заказа
        public List<OrderFileItem> Items { get; set; } = new();

        // Храним ПОЛНЫЕ пути к файлам (legacy, для обратной совместимости)
        public string SourcePath { get; set; } = "";
        public string PreparedPath { get; set; } = "";
        public string PrintPath { get; set; } = "";

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
            int successCount = normalizedItems.Count(x => x.FileStatus == "✅ Готово");
            int errorCount = normalizedItems.Count(x => x.FileStatus == "🔴 Ошибка");
            int inProgressCount = normalizedItems.Count(x => x.FileStatus == "🟡 В работе");
            int waitingCount = normalizedItems.Count(x =>
                !string.IsNullOrWhiteSpace(x.FileStatus) &&
                x.FileStatus.Contains("ожид", StringComparison.OrdinalIgnoreCase));

            if (errorCount == total)
                Status = "🔴 Ошибка";
            else if (successCount == total)
                Status = "✅ Готово";
            else if (waitingCount == total)
                Status = "Ожидание";
            else if (inProgressCount > 0)
                Status = $"🟡 В работе ({successCount + inProgressCount}/{total})";
            else
                Status = $"⚠ Частично готово ({successCount}/{total})";
        }
    }
}
