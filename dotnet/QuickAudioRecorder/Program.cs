using System;
using System.Threading;
using System.Windows.Forms;

namespace QuickAudioRecorder
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var instance = new Mutex(true, @"Local\QuickAudioRecorder-0D5AD7CA-3F13-4B23-8FA5-F9ED9B4E51D1", out created))
            {
                if (!created) return; // Only one tray icon and one recorder per user session.
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (_, e) => Diagnostics.Write("UI exception: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.Write("Unhandled exception: " + e.ExceptionObject);
                Application.Run(new TrayApplicationContext());
            }
        }
    }
}
