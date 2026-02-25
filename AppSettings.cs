using System;
using System.IO;
using System.Text.Json;

namespace MyManager
{
    public class AppSettings
    {
        public string OrdersRootPath { get; set; } = @"C:\MyManager\Orders";
        public string GrandpaPath { get; set; } = @"C:\MyManager\Archive";
        public string ArchiveDoneSubfolder { get; set; } = "Готово";

        public int RunTimeoutMinutes { get; set; } = 10;
        public bool UseExtendedMode { get; set; } = true;
        public string TempFolderName { get; set; } = "TempMyManager";
        public string TempFolderPath { get; set; } = "";
        public bool SortArrivalDescending { get; set; } = true;

        public string HistoryFilePath { get; set; } = "history.json";
        public string ManagerLogFilePath { get; set; } = "manager.log";
        public string OrderLogsFolderPath { get; set; } = "";

        public static string FileName => "settings.json";

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FileName))
                    return new AppSettings();

                var json = File.ReadAllText(FileName);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FileName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // молча
            }
        }
    }
}
