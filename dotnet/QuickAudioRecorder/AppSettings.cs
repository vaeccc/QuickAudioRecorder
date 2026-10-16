using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace QuickAudioRecorder
{
    // Names intentionally match the previous Python settings.json format.
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Name = "device_id")] public string DeviceId { get; set; }
        [DataMember(Name = "output_folder")] public string OutputFolder { get; set; }
        [DataMember(Name = "format")] public string Format { get; set; }
        [DataMember(Name = "tray_click_mode")] public string TrayClickMode { get; set; }
        [DataMember(Name = "language")] public string Language { get; set; }
        [DataMember(Name = "normalize")] public bool Normalize { get; set; }
        [DataMember(Name = "clipboard")] public bool Clipboard { get; set; }
        [DataMember(Name = "delete_after")] public bool DeleteAfter { get; set; }
        [DataMember(Name = "hk_mic")] public string HotkeyMic { get; set; }
        [DataMember(Name = "hk_loop")] public string HotkeySystem { get; set; }
        [DataMember(Name = "hk_both")] public string HotkeyBoth { get; set; }
        [DataMember(Name = "hk_stop")] public string HotkeyStop { get; set; }

        public AppSettings()
        {
            OutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickAudioRecorder");
            Format = "MP3";
            TrayClickMode = "last_used";
            Language = "system";
            HotkeyMic = HotkeySystem = HotkeyBoth = HotkeyStop = "";
        }

        public AppSettings Clone() => (AppSettings)MemberwiseClone();
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(OutputFolder)) throw new ArgumentException("请选择录音保存目录。");
            if (!string.Equals(Format, "WAV", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Format, "MP3", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("录音格式必须是 WAV 或 MP3。");
        }
    }

    internal static class SettingsStore
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickAudioRecorder");
        public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(AppSettings));

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var file = File.OpenRead(FilePath))
                        return (AppSettings)Serializer.ReadObject(file);
            }
            catch (Exception ex) { Diagnostics.Write("Settings load: " + ex.Message); }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(DirectoryPath);
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) Serializer.WriteObject(stream, settings);
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null);
                else
                    File.Move(temp, FilePath);
            }
            finally { TryDelete(temp); }
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Entry = "QuickAudioRecorder";
        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return string.Equals(key?.GetValue(Entry) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        private static string Command => "\"" + System.Windows.Forms.Application.ExecutablePath + "\"";
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(Entry, Command);
                else key.DeleteValue(Entry, false);
            }
        }
    }

    internal static class Diagnostics
    {
        private static readonly object Sync = new object();
        public static void Write(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickAudioRecorder", "Logs");
                Directory.CreateDirectory(dir);
                lock (Sync)
                    File.AppendAllText(Path.Combine(dir, "app.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
