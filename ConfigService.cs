using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MyManager
{
    public static class ConfigService
    {
        private static readonly string PitStopFile = "pitstop_actions.json";
        private static readonly string ImposingFile = "imposing_configs.json";

        // --- PitStop ---
        public static List<ActionConfig> GetAllPitStopConfigs()
            => LoadJson<ActionConfig>(PitStopFile);

        public static ActionConfig GetPitStopConfigByName(string name)
            => GetAllPitStopConfigs().FirstOrDefault(c => c.Name == name);

        public static void SavePitStopConfigs(List<ActionConfig> configs)
            => SaveJson(PitStopFile, configs);

        // --- Imposing ---
        public static List<ImposingConfig> GetAllImposingConfigs()
            => LoadJson<ImposingConfig>(ImposingFile);

        public static ImposingConfig GetImposingConfigByName(string name)
            => GetAllImposingConfigs().FirstOrDefault(c => c.Name == name);

        public static void SaveImposingConfigs(List<ImposingConfig> configs)
            => SaveJson(ImposingFile, configs);

        // --- Универсальные методы работы с JSON ---
        private static List<T> LoadJson<T>(string filePath)
        {
            if (!File.Exists(filePath)) return new List<T>();
            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
            }
            catch { return new List<T>(); }
        }

        private static void SaveJson<T>(string filePath, List<T> data)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, JsonSerializer.Serialize(data, options));
        }
    }
}