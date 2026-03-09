using System;
using System.Linq;
using System.Windows.Forms;

namespace MyManager
{
    internal static class Program
    {
        private static SwitchableApplicationContext? _applicationContext;

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
            _applicationContext = new SwitchableApplicationContext(new MainForm());
            Application.Run(_applicationContext);
        }

        internal static void SwitchToLegacyInterface(Form currentForm)
        {
            _applicationContext?.SwitchMainForm(new Form1(), currentForm);
        }

        private sealed class SwitchableApplicationContext : ApplicationContext
        {
            public SwitchableApplicationContext(Form startupForm)
            {
                SwitchMainForm(startupForm);
            }

            public void SwitchMainForm(Form nextForm, Form? currentForm = null)
            {
                if (MainForm != null)
                    MainForm.FormClosed -= MainForm_FormClosed;

                currentForm ??= MainForm;
                MainForm = nextForm;
                MainForm.FormClosed += MainForm_FormClosed;
                MainForm.Show();

                if (currentForm != null && !ReferenceEquals(currentForm, nextForm) && !currentForm.IsDisposed)
                    currentForm.Close();
            }

            private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
            {
                if (ReferenceEquals(sender, MainForm))
                    ExitThread();
            }
        }
    }
}
