using System;
using System.Windows.Forms;
using PdfSharp.Fonts;

namespace Replica
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ConfigurePdfSharpFonts();
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        private static void ConfigurePdfSharpFonts()
        {
            var settings = AppSettings.Load();
            GlobalFontSettings.FontResolver = new SimpleFontResolver(settings.FontsFolderPath);
        }
    }
}
