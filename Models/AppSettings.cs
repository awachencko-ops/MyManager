using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Replica
{
    public class AppSettings
    {
        public const string DefaultBaseFolderPath = @"C:\Андрей ПК\Replica BASEFOLDER";
        public const string LegacyDefaultBaseFolderPath = @"\\NAS\work\Первая Чукотская Типография\Шкалы\MYMANAGER BASEFOLDER";
        public const string DefaultGrandpaPath = @"\\NAS\work\Temp\!!!Дедушка";
        public const string DefaultTempFolderName = "TempReplica";
        public const string LegacyTempFolderName = "TempMyManager";

        private static string SystemDriveRoot
        {
            get
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory);
                return string.IsNullOrWhiteSpace(root) ? Path.DirectorySeparatorChar.ToString() : root;
            }
        }

        public static string LegacyOrdersRootPath => Path.Combine(SystemDriveRoot, "Replica", "Orders");
        public static string LegacyGrandpaPath => Path.Combine(SystemDriveRoot, "Replica", "Archive");
        public static string LegacyPitStopHotfoldersRootPath => Path.Combine(SystemDriveRoot, "PitStop");
        public static string LegacyImposingHotfoldersRootPath => Path.Combine(SystemDriveRoot, "HotImposing");

        public static string DefaultOrdersRootPath => Path.Combine(DefaultBaseFolderPath, "Orders");
        public static string DefaultTempFolderPath => Path.Combine(DefaultOrdersRootPath, DefaultTempFolderName);
        public static string DefaultHistoryFilePath => Path.Combine(DefaultBaseFolderPath, "AppData", "history.json");
        public static string DefaultManagerLogFilePath => Path.Combine(DefaultBaseFolderPath, "AppData", "manager.log");
        public static string DefaultOrderLogsFolderPath => Path.Combine(DefaultBaseFolderPath, "AppData", "order-logs");
        public static string DefaultUsersFilePath => Path.Combine(DefaultBaseFolderPath, "Config", "users.json");
        public static string DefaultUsersCacheFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Replica",
            "AppData",
            "users.cache.json");
        public static string DefaultPitStopConfigFilePath => Path.Combine(DefaultBaseFolderPath, "Config", "pitstop_actions.json");
        public static string DefaultImposingConfigFilePath => Path.Combine(DefaultBaseFolderPath, "Config", "imposing_configs.json");
        public static string DefaultPitStopHotfoldersRootPath => Path.Combine(DefaultBaseFolderPath, "WARNING NOT DELETE", "PitStop");
        public static string DefaultImposingHotfoldersRootPath => Path.Combine(DefaultBaseFolderPath, "WARNING NOT DELETE", "HotImposing");
        public static string DefaultThumbnailCacheFolderPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Replica",
            "ThumbnailCache");

        public string OrdersRootPath { get; set; } = DefaultOrdersRootPath;
        public string GrandpaPath { get; set; } = DefaultGrandpaPath;
        public string ArchiveDoneSubfolder { get; set; } = "Готово";

        public int RunTimeoutMinutes { get; set; } = 10;
        public bool UseExtendedMode { get; set; } = false;
        public string TempFolderName { get; set; } = DefaultTempFolderName;
        public string TempFolderPath { get; set; } = DefaultTempFolderPath;
        public bool SortArrivalDescending { get; set; } = true;

        public string HistoryFilePath { get; set; } = DefaultHistoryFilePath;
        public string ManagerLogFilePath { get; set; } = DefaultManagerLogFilePath;
        public string OrderLogsFolderPath { get; set; } = DefaultOrderLogsFolderPath;
        public string UsersFilePath { get; set; } = DefaultUsersFilePath;
        public string UsersCacheFilePath { get; set; } = DefaultUsersCacheFilePath;
        public string FontsFolderPath { get; set; } = string.Empty;
        public string SharedThumbnailCachePath { get; set; } = string.Empty;
        public string PitStopConfigFilePath { get; set; } = DefaultPitStopConfigFilePath;
        public string ImposingConfigFilePath { get; set; } = DefaultImposingConfigFilePath;
        public string PitStopHotfoldersRootPath { get; set; } = DefaultPitStopHotfoldersRootPath;
        public string ImposingHotfoldersRootPath { get; set; } = DefaultImposingHotfoldersRootPath;

        // Настройки многофайловых заказов
        public bool AllowManualSequenceReordering { get; set; } = true;
        public int MaxParallelism { get; set; } = 4;
        public string DefaultOrderSortBy { get; set; } = "SequenceNo";
        public List<string> VariantDictionary { get; set; } = new() { "A4", "A3", "Цветной", "Ч/Б", "draft", "final" };
        public bool AutoRenameOnDuplicate { get; set; } = true;

        public static string FileName => StoragePaths.ResolveFilePath("settings.json", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                var settingsPath = StoragePaths.ResolveExistingFilePath("settings.json", "settings.json");
                if (!File.Exists(settingsPath))
                    return new AppSettings();

                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (settings.NormalizePaths())
                    settings.Save();

                return settings;
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

        private bool NormalizePaths()
        {
            bool changed = false;

            var normalizedOrdersRootPath = NormalizePathValue(OrdersRootPath, DefaultOrdersRootPath);
            if (PathEquals(normalizedOrdersRootPath, LegacyOrdersRootPath))
                normalizedOrdersRootPath = DefaultOrdersRootPath;
            if (TryMapFromLegacyRoot(normalizedOrdersRootPath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedOrdersRootPath))
                normalizedOrdersRootPath = migratedOrdersRootPath;
            changed |= SetPathIfDifferent(OrdersRootPath, normalizedOrdersRootPath, value => OrdersRootPath = value);

            var normalizedTempFolderName = string.IsNullOrWhiteSpace(TempFolderName) ? DefaultTempFolderName : TempFolderName.Trim();
            if (string.Equals(normalizedTempFolderName, LegacyTempFolderName, StringComparison.OrdinalIgnoreCase))
                normalizedTempFolderName = DefaultTempFolderName;
            if (!string.Equals(TempFolderName, normalizedTempFolderName, StringComparison.Ordinal))
            {
                TempFolderName = normalizedTempFolderName;
                changed = true;
            }

            var expectedTempFolderPath = Path.Combine(OrdersRootPath, TempFolderName);
            var normalizedTempFolderPath = NormalizePathValue(TempFolderPath, expectedTempFolderPath);
            if (TryMapFromLegacyRoot(normalizedTempFolderPath, LegacyOrdersRootPath, OrdersRootPath, out var migratedTempFolderPath))
                normalizedTempFolderPath = migratedTempFolderPath;
            if (TryMapFromLegacyRoot(normalizedTempFolderPath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out migratedTempFolderPath))
                normalizedTempFolderPath = migratedTempFolderPath;
            changed |= SetPathIfDifferent(TempFolderPath, normalizedTempFolderPath, value => TempFolderPath = value);

            var normalizedGrandpaPath = NormalizePathValue(GrandpaPath, DefaultGrandpaPath);
            if (PathEquals(normalizedGrandpaPath, LegacyGrandpaPath))
                normalizedGrandpaPath = DefaultGrandpaPath;
            changed |= SetPathIfDifferent(GrandpaPath, normalizedGrandpaPath, value => GrandpaPath = value);

            var normalizedHistoryFilePath = NormalizePathValue(HistoryFilePath, DefaultHistoryFilePath);
            if (PathEquals(normalizedHistoryFilePath, "history.json"))
                normalizedHistoryFilePath = DefaultHistoryFilePath;
            if (TryMapFromLegacyRoot(normalizedHistoryFilePath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedHistoryFilePath))
                normalizedHistoryFilePath = migratedHistoryFilePath;
            changed |= SetPathIfDifferent(HistoryFilePath, normalizedHistoryFilePath, value => HistoryFilePath = value);

            var normalizedManagerLogFilePath = NormalizePathValue(ManagerLogFilePath, DefaultManagerLogFilePath);
            if (PathEquals(normalizedManagerLogFilePath, "manager.log"))
                normalizedManagerLogFilePath = DefaultManagerLogFilePath;
            if (TryMapFromLegacyRoot(normalizedManagerLogFilePath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedManagerLogFilePath))
                normalizedManagerLogFilePath = migratedManagerLogFilePath;
            changed |= SetPathIfDifferent(ManagerLogFilePath, normalizedManagerLogFilePath, value => ManagerLogFilePath = value);

            var normalizedOrderLogsFolderPath = NormalizePathValue(OrderLogsFolderPath, DefaultOrderLogsFolderPath);
            if (PathEquals(normalizedOrderLogsFolderPath, "order-logs"))
                normalizedOrderLogsFolderPath = DefaultOrderLogsFolderPath;
            if (TryMapFromLegacyRoot(normalizedOrderLogsFolderPath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedOrderLogsFolderPath))
                normalizedOrderLogsFolderPath = migratedOrderLogsFolderPath;
            changed |= SetPathIfDifferent(OrderLogsFolderPath, normalizedOrderLogsFolderPath, value => OrderLogsFolderPath = value);

            var normalizedUsersFilePath = NormalizePathValue(UsersFilePath, DefaultUsersFilePath);
            if (PathEquals(normalizedUsersFilePath, "users.json"))
                normalizedUsersFilePath = DefaultUsersFilePath;
            if (TryMapFromLegacyRoot(normalizedUsersFilePath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedUsersFilePath))
                normalizedUsersFilePath = migratedUsersFilePath;
            changed |= SetPathIfDifferent(UsersFilePath, normalizedUsersFilePath, value => UsersFilePath = value);

            var normalizedUsersCacheFilePath = NormalizePathValue(UsersCacheFilePath, DefaultUsersCacheFilePath);
            changed |= SetPathIfDifferent(UsersCacheFilePath, normalizedUsersCacheFilePath, value => UsersCacheFilePath = value);

            var normalizedFontsFolderPath = NormalizePathValue(FontsFolderPath, string.Empty);
            changed |= SetPathIfDifferent(FontsFolderPath, normalizedFontsFolderPath, value => FontsFolderPath = value);

            var normalizedSharedThumbnailCachePath = NormalizePathValue(SharedThumbnailCachePath, string.Empty);
            if (TryMapFromLegacyRoot(normalizedSharedThumbnailCachePath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedSharedThumbnailCachePath))
                normalizedSharedThumbnailCachePath = migratedSharedThumbnailCachePath;
            changed |= SetPathIfDifferent(SharedThumbnailCachePath, normalizedSharedThumbnailCachePath, value => SharedThumbnailCachePath = value);

            var normalizedPitStopConfigFilePath = NormalizePathValue(PitStopConfigFilePath, DefaultPitStopConfigFilePath);
            if (PathEquals(normalizedPitStopConfigFilePath, "pitstop_actions.json"))
                normalizedPitStopConfigFilePath = DefaultPitStopConfigFilePath;
            if (TryMapFromLegacyRoot(normalizedPitStopConfigFilePath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedPitStopConfigFilePath))
                normalizedPitStopConfigFilePath = migratedPitStopConfigFilePath;
            changed |= SetPathIfDifferent(PitStopConfigFilePath, normalizedPitStopConfigFilePath, value => PitStopConfigFilePath = value);

            var normalizedImposingConfigFilePath = NormalizePathValue(ImposingConfigFilePath, DefaultImposingConfigFilePath);
            if (PathEquals(normalizedImposingConfigFilePath, "imposing_configs.json"))
                normalizedImposingConfigFilePath = DefaultImposingConfigFilePath;
            if (TryMapFromLegacyRoot(normalizedImposingConfigFilePath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedImposingConfigFilePath))
                normalizedImposingConfigFilePath = migratedImposingConfigFilePath;
            changed |= SetPathIfDifferent(ImposingConfigFilePath, normalizedImposingConfigFilePath, value => ImposingConfigFilePath = value);

            var normalizedPitStopHotfoldersRootPath = NormalizePathValue(PitStopHotfoldersRootPath, DefaultPitStopHotfoldersRootPath);
            if (PathEquals(normalizedPitStopHotfoldersRootPath, LegacyPitStopHotfoldersRootPath))
                normalizedPitStopHotfoldersRootPath = DefaultPitStopHotfoldersRootPath;
            if (TryMapFromLegacyRoot(normalizedPitStopHotfoldersRootPath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedPitStopHotfoldersRootPath))
                normalizedPitStopHotfoldersRootPath = migratedPitStopHotfoldersRootPath;
            changed |= SetPathIfDifferent(PitStopHotfoldersRootPath, normalizedPitStopHotfoldersRootPath, value => PitStopHotfoldersRootPath = value);

            var normalizedImposingHotfoldersRootPath = NormalizePathValue(ImposingHotfoldersRootPath, DefaultImposingHotfoldersRootPath);
            if (PathEquals(normalizedImposingHotfoldersRootPath, LegacyImposingHotfoldersRootPath))
                normalizedImposingHotfoldersRootPath = DefaultImposingHotfoldersRootPath;
            if (TryMapFromLegacyRoot(normalizedImposingHotfoldersRootPath, LegacyDefaultBaseFolderPath, DefaultBaseFolderPath, out var migratedImposingHotfoldersRootPath))
                normalizedImposingHotfoldersRootPath = migratedImposingHotfoldersRootPath;
            changed |= SetPathIfDifferent(ImposingHotfoldersRootPath, normalizedImposingHotfoldersRootPath, value => ImposingHotfoldersRootPath = value);

            return changed;
        }

        private static string NormalizePathValue(string? value, string fallbackValue)
        {
            return string.IsNullOrWhiteSpace(value) ? fallbackValue : value.Trim();
        }

        private static bool SetPathIfDifferent(string currentValue, string nextValue, Action<string> setter)
        {
            if (PathEquals(currentValue, nextValue))
                return false;

            setter(nextValue);
            return true;
        }

        private static bool TryMapFromLegacyRoot(string path, string legacyRoot, string destinationRoot, out string mappedPath)
        {
            mappedPath = path;
            var normalizedPath = NormalizeForComparison(path);
            var normalizedLegacyRoot = NormalizeForComparison(legacyRoot);
            if (!normalizedPath.StartsWith(normalizedLegacyRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedPath.Length == normalizedLegacyRoot.Length)
            {
                mappedPath = destinationRoot;
                return true;
            }

            if (normalizedPath[normalizedLegacyRoot.Length] != Path.DirectorySeparatorChar)
                return false;

            var suffix = normalizedPath[(normalizedLegacyRoot.Length + 1)..];
            mappedPath = Path.Combine(destinationRoot, suffix);
            return true;
        }

        private static bool PathEquals(string? leftPath, string? rightPath)
        {
            return string.Equals(
                NormalizeForComparison(leftPath),
                NormalizeForComparison(rightPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeForComparison(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
