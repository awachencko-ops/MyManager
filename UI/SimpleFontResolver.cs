using PdfSharp.Fonts;

namespace MyManager
{
    public class SimpleFontResolver : IFontResolver
    {
        public byte[] GetFont(string faceName)
        {
            var fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var fontPath = Path.Combine(fontsFolder, $"{faceName}.ttf");
            return File.ReadAllBytes(fontPath);
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (string.Equals(familyName, "Arial", StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo("arial");
            return new FontResolverInfo("arial");
        }
    }
}
