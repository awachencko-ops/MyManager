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

            bool useFieryPrototype = args.Any(a =>
                string.Equals(a, "--fiery-prototype", StringComparison.OrdinalIgnoreCase));

            Application.Run(useFieryPrototype ? new FieryPrototypeForm() : new Form1());
        }
    }
}