using PdfSharp.Fonts;

namespace MyManager
{
    public class SimpleFontResolver : IFontResolver
    {
        public byte[] GetFont(string faceName) => File.ReadAllBytes($@"C:\Windows\Fonts\{faceName}.ttf");

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo("arial");
            return null;
        }
    }
}