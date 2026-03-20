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
            var settingsProvider = new FileSettingsProvider();
            var settings = settingsProvider.Load();
            ConfigurePdfSharpFonts(settings);
            ApplicationConfiguration.Initialize();
            AutoUpdateBootstrapper.TryStart(settings);
            Application.Run(new OrdersWorkspaceForm(settingsProvider));
        }

        private static void ConfigurePdfSharpFonts(AppSettings settings)
        {
            if (settings == null)
                settings = new AppSettings();

            GlobalFontSettings.FontResolver = new SimpleFontResolver(settings.FontsFolderPath);
        }
    }
}
