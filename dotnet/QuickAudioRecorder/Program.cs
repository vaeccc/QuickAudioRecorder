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
            // Lightweight regression test invoked by CI from the packaged executable.
            if (Array.Exists(Environment.GetCommandLineArgs(), arg =>
                string.Equals(arg, "--verify-hotkeys", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode = HotkeyService.VerifyLegacyShortcuts() ? 0 : 1;
                return;
            }
            if (Array.Exists(Environment.GetCommandLineArgs(), arg =>
                string.Equals(arg, "--verify-branding", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var settings = new SettingsForm(new AppSettings()))
                    {
                        bool correctName = string.Equals(
                            System.IO.Path.GetFileName(Application.ExecutablePath),
                            "MiniRec.exe", StringComparison.OrdinalIgnoreCase);
                        bool correctTitle = settings.Text.StartsWith("MiniRec", StringComparison.Ordinal);
                        bool hasIcon = settings.Icon != null && settings.Icon.Handle != IntPtr.Zero;
                        Environment.ExitCode = correctName && correctTitle && hasIcon ? 0 : 1;
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.Write("Branding smoke test: " + ex);
                    Environment.ExitCode = 1;
                }
                return;
            }
            Diagnostics.Write("Process start. Version=" + typeof(Program).Assembly.GetName().Version);
            bool created;
            using (var instance = new Mutex(true, @"Local\QuickAudioRecorder-0D5AD7CA-3F13-4B23-8FA5-F9ED9B4E51D1", out created))
            {
                if (!created) { Diagnostics.Write("Another instance is already running; exiting."); return; }
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (_, e) => Diagnostics.Write("UI exception: " + e.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.Write("Unhandled exception: " + e.ExceptionObject);
                    Diagnostics.Write("Creating tray application context.");
                    using (var context = new TrayApplicationContext()) Application.Run(context);
                    Diagnostics.Write("Message loop exited normally.");
                }
                catch (Exception ex)
                {
                    Diagnostics.Write("Fatal startup/hosting exception: " + ex);
                    try { MessageBox.Show("MiniRec 启动失败。\n\n" + ex.Message, "MiniRec", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                    Environment.ExitCode = 1;
                }
            }
            Diagnostics.Write("Process exit. Code=" + Environment.ExitCode);
        }
    }
}
