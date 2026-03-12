using PdfSharp.Fonts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MyManager
{
    public sealed class SimpleFontResolver : IFontResolver
    {
        private readonly string[] _fontFolders;

        private static readonly Dictionary<string, string> FaceToFileName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["arial"] = "arial.ttf",
            ["arial#b"] = "arialbd.ttf",
            ["arial#i"] = "ariali.ttf",
            ["arial#bi"] = "arialbi.ttf",
            ["helvetica"] = "arial.ttf",
            ["helvetica#b"] = "arialbd.ttf",
            ["helvetica#i"] = "ariali.ttf",
            ["helvetica#bi"] = "arialbi.ttf"
        };

        public SimpleFontResolver(string? preferredFontsFolder = null)
        {
            var folders = new List<string>();

            if (!string.IsNullOrWhiteSpace(preferredFontsFolder))
                folders.Add(preferredFontsFolder.Trim());

            var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrWhiteSpace(windowsFonts))
                folders.Add(windowsFonts);

            _fontFolders = folders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public byte[] GetFont(string faceName)
        {
            var fileName = FaceToFileName.TryGetValue(faceName, out var mappedFileName)
                ? mappedFileName
                : (faceName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? faceName : $"{faceName}.ttf");

            foreach (var folder in _fontFolders)
            {
                var path = Path.Combine(folder, fileName);
                if (!File.Exists(path))
                    continue;

                return File.ReadAllBytes(path);
            }

            throw new FileNotFoundException(
                $"Font file '{fileName}' was not found. Checked folders: {string.Join("; ", _fontFolders)}");
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var normalizedFamily = NormalizeFamily(familyName);
            var styleSuffix = isBold && isItalic ? "#bi" : isBold ? "#b" : isItalic ? "#i" : string.Empty;
            var faceKey = $"{normalizedFamily}{styleSuffix}";

            if (!FaceToFileName.ContainsKey(faceKey))
                faceKey = "arial";

            return new FontResolverInfo(faceKey);
        }

        private static string NormalizeFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
                return "arial";

            if (familyName.Equals("helvetica", StringComparison.OrdinalIgnoreCase))
                return "helvetica";

            return "arial";
        }
    }
}
