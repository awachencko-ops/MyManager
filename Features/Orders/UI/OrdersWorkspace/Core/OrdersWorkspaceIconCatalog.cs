using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Svg;

namespace Replica
{
    internal static class OrdersWorkspaceIconCatalog
    {
        private static readonly ConcurrentDictionary<string, Bitmap> IconTemplateCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> MaterialIconDirectoryCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> MaterialSvgPathCache = new(StringComparer.OrdinalIgnoreCase);
        private static int _warmupStarted;
        private static string? _materialRootPath;
        private static readonly string[] MaterialCategories =
        {
            "action",
            "alert",
            "av",
            "communication",
            "content",
            "device",
            "editor",
            "file",
            "hardware",
            "home",
            "image",
            "maps",
            "navigation",
            "notification",
            "places",
            "search",
            "social",
            "toggle"
        };
        private static readonly string[] PreferredStyles =
        {
            "materialicons",
            "materialiconsoutlined",
            "materialiconsround",
            "materialiconssharp",
            "materialiconstwotone"
        };

        public static Image? LoadIcon(string iconFolder, string fileNameHint, int size, params (string Folder, string FileNameHint)[] fallbacks)
        {
            var candidates = new List<(string Folder, string FileNameHint)> { (iconFolder, fileNameHint) };
            if (fallbacks != null && fallbacks.Length > 0)
                candidates.AddRange(fallbacks);

            foreach (var candidate in candidates)
            {
                var icon = TryLoadSingleIcon(candidate.Folder, candidate.FileNameHint, size);
                if (icon != null)
                    return icon;
            }

            return null;
        }

        public static void QueueWarmup()
        {
            if (Interlocked.Exchange(ref _warmupStarted, 1) != 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    _ = ResolveMaterialRoot();

                    var warmupIcons = new (string Folder, string Hint)[]
                    {
                        ("check", "check"),
                        ("error", "error"),
                        ("cancel", "cancel"),
                        ("archive", "archive"),
                        ("upload", "upload"),
                        ("cards", "cards"),
                        ("file", "folder_open"),
                        ("file", "attach_file"),
                        ("action", "delete"),
                        ("av", "play_arrow"),
                        ("av", "stop"),
                        ("action", "settings"),
                        ("action", "terminal"),
                        ("action", "grid_view"),
                        ("action", "view_headline"),
                        ("content", "content_copy"),
                        ("content", "content_paste"),
                        ("file", "drive_file_rename_outline"),
                        ("image", "branding_watermark")
                    };

                    foreach (var (folder, hint) in warmupIcons)
                    {
                        using var image = LoadIcon(folder, hint, size: 16);
                    }
                }
                catch
                {
                    // Warmup is best-effort.
                }
            });
        }

        private static Image? TryLoadSingleIcon(string iconFolder, string fileNameHint, int size)
        {
            if (string.IsNullOrWhiteSpace(iconFolder) && string.IsNullOrWhiteSpace(fileNameHint))
                return null;

            var cacheKey = $"{NormalizeToken(iconFolder)}|{NormalizeToken(fileNameHint)}|{size}";
            if (IconTemplateCache.TryGetValue(cacheKey, out var cachedTemplate))
                return new Bitmap(cachedTemplate);

            var materialIcon = TryLoadMaterialSvg(iconFolder, fileNameHint, size);
            if (materialIcon != null)
            {
                IconTemplateCache[cacheKey] = new Bitmap(materialIcon);
                return materialIcon;
            }

            return null;
        }

        private static Bitmap? TryLoadMaterialSvg(string iconFolder, string fileNameHint, int size)
        {
            var materialRoot = ResolveMaterialRoot();
            if (string.IsNullOrWhiteSpace(materialRoot))
                return null;

            var candidateNames = BuildMaterialCandidateNames(iconFolder, fileNameHint);
            foreach (var iconName in candidateNames)
            {
                var svgPath = ResolveMaterialSvgPath(materialRoot, iconName);
                if (string.IsNullOrWhiteSpace(svgPath))
                    continue;

                try
                {
                    var document = SvgDocument.Open(svgPath);
                    if (document == null)
                        continue;

                    document.Width = size;
                    document.Height = size;
                    using var rendered = document.Draw(size, size);
                    return rendered != null ? new Bitmap(rendered) : null;
                }
                catch
                {
                    // Ignore malformed SVGs and continue to fallback candidates.
                }
            }

            return null;
        }

        private static string? ResolveMaterialRoot()
        {
            if (!string.IsNullOrWhiteSpace(_materialRootPath) && Directory.Exists(_materialRootPath))
                return _materialRootPath;

            var roots = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Icons", "src"),
                Path.Combine(Environment.CurrentDirectory, "Icons", "src"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Icons", "src")
            };

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var full = Path.GetFullPath(root);
                    if (!Directory.Exists(full))
                        continue;

                    _materialRootPath = full;
                    return full;
                }
                catch
                {
                    // Ignore invalid paths.
                }
            }

            return null;
        }

        private static string? ResolveMaterialIconDirectory(string materialRoot, string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
                return null;

            var cacheKey = $"{materialRoot}|{iconName}";
            if (MaterialIconDirectoryCache.TryGetValue(cacheKey, out var cached))
                return string.IsNullOrWhiteSpace(cached) ? null : cached;

            foreach (var category in MaterialCategories)
            {
                var candidate = Path.Combine(materialRoot, category, iconName);
                if (Directory.Exists(candidate))
                {
                    MaterialIconDirectoryCache[cacheKey] = candidate;
                    return candidate;
                }
            }

            MaterialIconDirectoryCache[cacheKey] = string.Empty;
            return null;
        }

        private static IEnumerable<string> BuildMaterialCandidateNames(string iconFolder, string fileNameHint)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string value)
            {
                var normalized = NormalizeToken(value);
                if (!string.IsNullOrWhiteSpace(normalized))
                    names.Add(normalized);
            }

            Add(fileNameHint);
            Add(iconFolder);

            var mapped = ResolveMaterialAlias(fileNameHint, iconFolder);
            if (!string.IsNullOrWhiteSpace(mapped))
                Add(mapped);

            return names;
        }

        private static string? ResolveMaterialAlias(string fileNameHint, string iconFolder)
        {
            var hint = NormalizeToken(fileNameHint);
            return hint switch
            {
                "check" => "check",
                "file_export" => "upload_file",
                "upload" => "upload_file",
                "files" => "folder",
                "cards" => "inventory_2",
                "folder_open" => "folder_open",
                "arrow_drop_down" => "keyboard_arrow_down",
                "attach_file_add" => "attach_file",
                _ => NormalizeToken(iconFolder) switch
                {
                    "check" => "check",
                    "error" => "error",
                    "cancel" => "cancel",
                    "archive" => "archive",
                    "file_export" => "upload_file",
                    "upload" => "upload_file",
                    "files" => "folder",
                    "cards" => "inventory_2",
                    "addbox" => "add_box",
                    "addfile" => "attach_file",
                    "folder_open" => "folder_open",
                    "move_to_inbox" => "move_to_inbox",
                    "play_arrow" => "play_arrow",
                    "grid_view" => "grid_view",
                    "view_cozy" => "view_cozy",
                    "headline" => "view_headline",
                    "arrow_drop_down" => "keyboard_arrow_down",
                    _ => null
                }
            };
        }

        private static string? ResolveMaterialSvgPath(string materialRoot, string iconName)
        {
            if (string.IsNullOrWhiteSpace(materialRoot) || string.IsNullOrWhiteSpace(iconName))
                return null;

            var cacheKey = $"{materialRoot}|{iconName}";
            if (MaterialSvgPathCache.TryGetValue(cacheKey, out var cachedPath))
                return string.IsNullOrWhiteSpace(cachedPath) ? null : cachedPath;

            var iconDirectory = ResolveMaterialIconDirectory(materialRoot, iconName);
            if (string.IsNullOrWhiteSpace(iconDirectory))
            {
                MaterialSvgPathCache[cacheKey] = string.Empty;
                return null;
            }

            var resolved = ResolveMaterialSvgPathByDirectory(iconDirectory);
            MaterialSvgPathCache[cacheKey] = resolved ?? string.Empty;
            return resolved;
        }

        private static string? ResolveMaterialSvgPathByDirectory(string iconDirectory)
        {
            foreach (var style in PreferredStyles)
            {
                var styleDir = Path.Combine(iconDirectory, style);
                if (!Directory.Exists(styleDir))
                    continue;

                var candidate24 = Path.Combine(styleDir, "24px.svg");
                if (File.Exists(candidate24))
                    return candidate24;

                var anySvg = Directory.GetFiles(styleDir, "*.svg", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(anySvg))
                    return anySvg;
            }

            return null;
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = value.Trim().ToLowerInvariant().ToCharArray();
            var result = new char[chars.Length];
            var index = 0;
            var previousUnderscore = false;
            foreach (var ch in chars)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    result[index++] = ch;
                    previousUnderscore = false;
                    continue;
                }

                if (previousUnderscore)
                    continue;

                result[index++] = '_';
                previousUnderscore = true;
            }

            return new string(result, 0, index).Trim('_');
        }
    }
}
