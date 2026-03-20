using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Replica
{
    public static class ConfigService
    {
        private const string PitStopDefaultFile = "pitstop_actions.json";
        private const string ImposingDefaultFile = "imposing_configs.json";
        private static readonly ISettingsProvider DefaultSettingsProvider = new FileSettingsProvider();
        private static ISettingsProvider _settingsProvider = DefaultSettingsProvider;

        public static ISettingsProvider SettingsProvider
        {
            get => _settingsProvider;
            set => _settingsProvider = value ?? DefaultSettingsProvider;
        }

        // --- PitStop ---
        public static List<ActionConfig> GetAllPitStopConfigs()
        {
            var configs = LoadJson<ActionConfig>(ResolvePitStopConfigPath(forRead: true));
            if (NormalizePitStopConfigs(configs))
                SavePitStopConfigs(configs);

            return configs;
        }

        public static ActionConfig? GetPitStopConfigByName(string name)
            => GetAllPitStopConfigs().FirstOrDefault(c => c.Name == name);

        public static void SavePitStopConfigs(List<ActionConfig> configs)
            => SaveJson(ResolvePitStopConfigPath(forRead: false), configs);

        // --- Imposing ---
        public static List<ImposingConfig> GetAllImposingConfigs()
        {
            var configs = LoadJson<ImposingConfig>(ResolveImposingConfigPath(forRead: true));
            if (NormalizeImposingConfigs(configs))
                SaveImposingConfigs(configs);

            return configs;
        }

        public static ImposingConfig? GetImposingConfigByName(string name)
            => GetAllImposingConfigs().FirstOrDefault(c => c.Name == name);

        public static void SaveImposingConfigs(List<ImposingConfig> configs)
            => SaveJson(ResolveImposingConfigPath(forRead: false), configs);

        // --- Универсальные методы работы с JSON ---
        private static string ResolvePitStopConfigPath(bool forRead)
        {
            var settings = SettingsProvider.Load();
            var configuredPath = settings.PitStopConfigFilePath;
            return forRead
                ? StoragePaths.ResolveExistingFilePath(configuredPath, PitStopDefaultFile)
                : StoragePaths.ResolveFilePath(configuredPath, PitStopDefaultFile);
        }

        private static string ResolveImposingConfigPath(bool forRead)
        {
            var settings = SettingsProvider.Load();
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
            catch (Exception ex)
            {
                Logger.Warn($"CONFIG | load-json-failed | path={resolvedPath} | {ex.Message}");
                return new List<T>();
            }
        }

        private static void SaveJson<T>(string resolvedPath, List<T> data)
        {
            var dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(resolvedPath, JsonSerializer.Serialize(data, options));
        }

        private static bool NormalizePitStopConfigs(List<ActionConfig> configs)
        {
            var changed = false;
            foreach (var config in configs)
                changed |= NormalizeActionConfig(config);

            return changed;
        }

        private static bool NormalizeImposingConfigs(List<ImposingConfig> configs)
        {
            var changed = false;
            foreach (var config in configs)
                changed |= NormalizeImposingConfig(config);

            return changed;
        }

        private static bool NormalizeActionConfig(ActionConfig config)
        {
            if (config == null)
                return false;

            var changed = false;
            changed |= UpdatePath(config.BaseFolder, value => config.BaseFolder = value);
            changed |= UpdatePath(config.InputFolder, value => config.InputFolder = value);
            changed |= UpdatePath(config.ReportSuccess, value => config.ReportSuccess = value);
            changed |= UpdatePath(config.ReportError, value => config.ReportError = value);
            changed |= UpdatePath(config.OriginalSuccess, value => config.OriginalSuccess = value);
            changed |= UpdatePath(config.OriginalError, value => config.OriginalError = value);
            changed |= UpdatePath(config.ProcessedSuccess, value => config.ProcessedSuccess = value);
            changed |= UpdatePath(config.ProcessedError, value => config.ProcessedError = value);
            changed |= UpdatePath(config.NonPdfLogs, value => config.NonPdfLogs = value);
            changed |= UpdatePath(config.NonPdfFiles, value => config.NonPdfFiles = value);
            return changed;
        }

        private static bool NormalizeImposingConfig(ImposingConfig config)
        {
            if (config == null)
                return false;

            var changed = false;
            changed |= UpdatePath(config.BaseFolder, value => config.BaseFolder = value);
            changed |= UpdatePath(config.In, value => config.In = value);
            changed |= UpdatePath(config.Out, value => config.Out = value);
            changed |= UpdatePath(config.Done, value => config.Done = value);
            changed |= UpdatePath(config.Error, value => config.Error = value);
            return changed;
        }

        private static bool UpdatePath(string value, Action<string> setter)
        {
            var normalized = NormalizeLegacyBaseFolderPath(value);
            if (string.Equals(value, normalized, StringComparison.Ordinal))
                return false;

            setter(normalized);
            return true;
        }

        private static string NormalizeLegacyBaseFolderPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var trimmed = path.Trim();
            return ReplaceRoot(trimmed, AppSettings.LegacyDefaultBaseFolderPath, AppSettings.DefaultBaseFolderPath);
        }

        private static string ReplaceRoot(string path, string oldRoot, string newRoot)
        {
            if (!path.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
                return path;

            return newRoot + path.Substring(oldRoot.Length);
        }
    }
}
