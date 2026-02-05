using System;
using System.IO;
using System.Text.Json;

namespace MyManager
{
    public class AppSettings
    {
        public string OrdersRootPath { get; set; } = @"C:\Андрей ПК";
        public string GrandpaPath { get; set; } = @"\\NAS\work\Temp\!!!Дедушка";
        public int RunTimeoutMinutes { get; set; } = 10;

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
