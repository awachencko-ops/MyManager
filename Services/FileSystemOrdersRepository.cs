using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Replica
{
    public sealed class FileSystemOrdersRepository : IOrdersRepository
    {
        private readonly string _historyFilePath;

        public FileSystemOrdersRepository(string historyFilePath)
        {
            _historyFilePath = string.IsNullOrWhiteSpace(historyFilePath)
                ? AppSettings.DefaultHistoryFilePath
                : historyFilePath;
        }

        public string BackendName => "filesystem";

        public bool TryLoadAll(out List<OrderData> orders, out string error)
        {
            orders = new List<OrderData>();
            error = string.Empty;

            try
            {
                var resolvedPath = StoragePaths.ResolveExistingFilePath(_historyFilePath, "history.json");
                if (!File.Exists(resolvedPath))
                    return true;

                var json = File.ReadAllText(resolvedPath);
                var parsed = JsonSerializer.Deserialize<List<OrderData>>(json);
                if (parsed != null)
                    orders = parsed;

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                orders = new List<OrderData>();
                return false;
            }
        }

        public bool TrySaveAll(IReadOnlyCollection<OrderData> orders, out string error)
        {
            error = string.Empty;

            try
            {
                var resolvedPath = StoragePaths.ResolveFilePath(_historyFilePath, "history.json");
                var directoryPath = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                var json = JsonSerializer.Serialize(
                    orders ?? Array.Empty<OrderData>(),
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(resolvedPath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
