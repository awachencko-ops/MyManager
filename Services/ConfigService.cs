using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MyManager
{
    public static class ConfigService
    {
        private const string PitStopDefaultFile = "pitstop_actions.json";
        private const string ImposingDefaultFile = "imposing_configs.json";

        // --- PitStop ---
        public static List<ActionConfig> GetAllPitStopConfigs()
            => LoadJson<ActionConfig>(ResolvePitStopConfigPath(forRead: true));

        public static ActionConfig? GetPitStopConfigByName(string name)
            => GetAllPitStopConfigs().FirstOrDefault(c => c.Name == name);

        public static void SavePitStopConfigs(List<ActionConfig> configs)
            => SaveJson(ResolvePitStopConfigPath(forRead: false), configs);

        // --- Imposing ---
        public static List<ImposingConfig> GetAllImposingConfigs()
            => LoadJson<ImposingConfig>(ResolveImposingConfigPath(forRead: true));

        public static ImposingConfig? GetImposingConfigByName(string name)
            => GetAllImposingConfigs().FirstOrDefault(c => c.Name == name);

        public static void SaveImposingConfigs(List<ImposingConfig> configs)
            => SaveJson(ResolveImposingConfigPath(forRead: false), configs);

        // --- Универсальные методы работы с JSON ---
        private static string ResolvePitStopConfigPath(bool forRead)
        {
            var settings = AppSettings.Load();
            var configuredPath = settings.PitStopConfigFilePath;
            return forRead
                ? StoragePaths.ResolveExistingFilePath(configuredPath, PitStopDefaultFile)
                : StoragePaths.ResolveFilePath(configuredPath, PitStopDefaultFile);
        }

        private static string ResolveImposingConfigPath(bool forRead)
        {
            var settings = AppSettings.Load();
            var configuredPath = settings.ImposingConfigFilePath;
            return forRead
                ? StoragePaths.ResolveExistingFilePath(configuredPath, ImposingDefaultFile)
                : StoragePaths.ResolveFilePath(configuredPath, ImposingDefaultFile);
        }

        private static List<T> LoadJson<T>(string resolvedPath)
        {
            if (!File.Exists(resolvedPath))
                return new List<T>();

            try
            {
                string json = File.ReadAllText(resolvedPath);
                return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
            }
            catch { return new List<T>(); }
        }

        private static void SaveJson<T>(string resolvedPath, List<T> data)
        {
            var dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(resolvedPath, JsonSerializer.Serialize(data, options));
        }
    }
}
