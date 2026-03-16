using System;
using System.Collections.Generic;

namespace Replica
{
    public static class OrderStages
    {
        public const int None = 0;
        public const int Source = 1;
        public const int Prepared = 2;
        public const int Print = 3;

        public static bool IsFileStage(int stage)
        {
            return stage is Source or Prepared or Print;
        }
    }

    public static class OrderGridColumnNames
    {
        public const string Status = "colStatus";
        public const string StateLegacy = "colState";
        public const string OrderNumber = "colOrderNumber";
        public const string Source = "colSource";
        public const string Prepared = "colPrep";
        public const string PreparedLegacy = "colReady";
        public const string PitStop = "colPitstop";
        public const string PitStopLegacy = "colPitStop";
        public const string HotImposing = "colHotimposing";
        public const string ImposingLegacy = "colImposing";
        public const string Print = "colPrint";
        public const string Received = "colReceived";
        public const string Created = "colCreated";

        public static int ResolveStage(string? columnName)
        {
            return columnName switch
            {
                Source => OrderStages.Source,
                Prepared => OrderStages.Prepared,
                PreparedLegacy => OrderStages.Prepared,
                Print => OrderStages.Print,
                _ => OrderStages.None
            };
        }
    }

    public static class QueueStatusNames
    {
        public const string AllJobs = "Все задания";
        public const string Processed = "Обработанные";
        public const string Archived = "В архиве";
        public const string Processing = "Обрабатывается";
        public const string Delayed = "Задержанные";
        public const string Completed = "Завершено";

        public static readonly string[] All =
        {
            AllJobs,
            Processed,
            Archived,
            Processing,
            Delayed,
            Completed
        };
    }

    public static class WorkflowStatusNames
    {
        public const string Processed = "Обработано";
        public const string Archived = "В архиве";
        public const string Building = "Выполняется сборка";
        public const string Processing = "Обрабатывается";
        public const string Waiting = "Ожидание";
        public const string Cancelled = "Отменено";
        public const string Error = "Ошибка";
        public const string Completed = "Завершено";
        public const string Printed = "Напечатано";

        public const string LegacyReady = "✅ Готово";
        public const string LegacyInWork = "🟡 В работе";
        public const string LegacyError = "🔴 Ошибка";

        public static readonly string[] Filterable =
        {
            Processed,
            Archived,
            Building,
            Processing,
            Waiting,
            Cancelled,
            Error,
            Completed
        };

        public static readonly IReadOnlyDictionary<string, string[]> QueueMappings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [QueueStatusNames.Processed] = [Processed],
            [QueueStatusNames.Archived] = [Archived],
            [QueueStatusNames.Processing] = [Building, Processing, Waiting],
            [QueueStatusNames.Delayed] = [Cancelled, Error],
            [QueueStatusNames.Completed] = [Completed]
        };

        public static string? Normalize(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
                return null;

            var value = rawStatus.Trim();
            foreach (var status in Filterable)
            {
                if (string.Equals(value, status, StringComparison.OrdinalIgnoreCase))
                    return status;

                if (value.Contains(status, StringComparison.OrdinalIgnoreCase))
                    return status;
            }

            if (value.Contains("архив", StringComparison.OrdinalIgnoreCase))
                return Archived;

            if (value.Contains("отмен", StringComparison.OrdinalIgnoreCase))
                return Cancelled;

            if (value.Contains("ошиб", StringComparison.OrdinalIgnoreCase))
                return Error;

            if (value.Contains("сборк", StringComparison.OrdinalIgnoreCase)
                || value.Contains("imposing", StringComparison.OrdinalIgnoreCase)
                || value.Contains("pitstop", StringComparison.OrdinalIgnoreCase))
            {
                return Building;
            }

            if (value.Contains("обрабатыва", StringComparison.OrdinalIgnoreCase)
                || value.Contains("в работе", StringComparison.OrdinalIgnoreCase)
                || value.Contains("запуск", StringComparison.OrdinalIgnoreCase))
            {
                return Processing;
            }

            if (value.Contains("ожид", StringComparison.OrdinalIgnoreCase))
                return Waiting;

            if (value.Contains("обработано", StringComparison.OrdinalIgnoreCase))
                return Processed;

            if (value.Contains("готово", StringComparison.OrdinalIgnoreCase)
                || value.Contains("заверш", StringComparison.OrdinalIgnoreCase)
                || value.Contains("напечат", StringComparison.OrdinalIgnoreCase))
            {
                return Completed;
            }

            return null;
        }
    }

    public static class OrderOperationNames
    {
        public const string Run = "run";
        public const string Stop = "stop";
        public const string Delete = "delete";
        public const string AddItem = "add-item";
        public const string RemoveItem = "remove-item";
        public const string Topology = "topology";
    }

    public static class OrderStatusSourceNames
    {
        public const string Processor = "processor";
        public const string Ui = "ui";
        public const string FileSync = "file-sync";
    }
}
