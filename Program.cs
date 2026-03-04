using System;
using System.Linq;
using System.Windows.Forms;

namespace MyManager
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
            ApplicationConfiguration.Initialize();

            // New startup: MainForm is the primary shell; Form1 stays archived in Forms/Archive as legacy fallback during migration.
            Application.Run(new MainForm());
        }
    }
}