using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace QuickAudioRecorder
{
    internal sealed class SettingsForm : Form
    {
        public Func<AppSettings, bool, string> SaveRequested;
        private readonly AppSettings _original;
        private readonly bool _english;
        private readonly ComboBox _mic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 335 };
        private readonly ComboBox _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
        private readonly ComboBox _left = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270 };
        private readonly ComboBox _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly TextBox _folder = new TextBox { Width = 295 };
        private readonly CheckBox _normalize = new CheckBox { AutoSize = true };
        private readonly CheckBox _clipboard = new CheckBox { AutoSize = true };
        private readonly CheckBox _deleteAfter = new CheckBox { AutoSize = true };
        private readonly CheckBox _startup = new CheckBox { AutoSize = true };
        private readonly TextBox[] _hotkeys = new TextBox[4];
        private readonly Button _refresh = new Button { Width = 90 };
        private readonly Label _deviceStatus = new Label { AutoSize = true };
        private bool _loading;

        public SettingsForm(AppSettings settings)
        {
            _original = settings.Clone();
            _english = settings.Language == "en_US" ||
                (settings.Language == "system" && !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh"));
            Text = T("QuickAudioRecorder 设置", "QuickAudioRecorder Settings");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(480, 440);
            Font = new Font("Microsoft YaHei UI", 9f);
            BuildUi();
            LoadValues(settings);
            Shown += async (_, __) => await RefreshDevicesAsync();
        }

        private string T(string zh, string en) => _english ? en : zh;

        private void BuildUi()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var audioTab = new TabPage(T("录音", "Audio"));
            var hotkeyTab = new TabPage(T("快捷键", "Hotkeys"));
            var generalTab = new TabPage(T("常规", "General"));
            tabs.TabPages.AddRange(new[] { audioTab, hotkeyTab, generalTab });

            var audio = Panel();
            AddRow(audio, 12, T("麦克风设备", "Microphone device"), _mic, 38);
            _refresh.Text = T("刷新设备", "Refresh");
            _refresh.Location = new Point(350, 37);
            _refresh.Click += async (_, __) => await RefreshDevicesAsync();
            audio.Controls.Add(_refresh);
            _deviceStatus.Location = new Point(16, 71);
            audio.Controls.Add(_deviceStatus);

            _folder.Text = _original.OutputFolder;
            AddRow(audio, 100, T("保存目录", "Output folder"), _folder, 128);
            var browse = new Button { Text = T("浏览…", "Browse…"), Location = new Point(350, 127), Size = new Size(90, 27) };
            browse.Click += (_, __) =>
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = _folder.Text,
                    Description = T("选择录音保存目录", "Select output folder") })
                    if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
            };
            audio.Controls.Add(browse);

            AddRow(audio, 164, T("录音格式", "File format"), _format, 192);
            _format.Items.AddRange(new object[] { "MP3", "WAV" });
            AddRow(audio, 224, T("托盘左键操作", "Tray left click"), _left, 252);
            _left.Items.AddRange(new object[]
            {
                T("上次使用的录音来源", "Last used source"),
                T("麦克风", "Microphone"),
                T("电脑声音", "System audio"),
                T("混合录音", "Both sources")
            });
            _normalize.Text = T("结束录音后标准化音量", "Normalize volume after recording");
            _normalize.Location = new Point(16, 298);
            audio.Controls.Add(_normalize);
            _clipboard.Text = T("录音后复制文件到剪贴板", "Copy file to clipboard after recording");
            _clipboard.Location = new Point(16, 326);
            _clipboard.CheckedChanged += (_, __) => _deleteAfter.Enabled = _clipboard.Checked;
            audio.Controls.Add(_clipboard);
            _deleteAfter.Text = T("复制前移至临时目录", "Move file to temporary folder before copy");
            _deleteAfter.Location = new Point(36, 354);
            audio.Controls.Add(_deleteAfter);
            audioTab.Controls.Add(audio);

            var hot = Panel();
            string[] labels = _english ?
                new[] { "Record microphone", "Record system audio", "Record both", "Stop & save" } :
                new[] { "录制麦克风", "录制电脑声音", "混合录音", "停止并保存" };
            for (int i = 0; i < 4; i++)
            {
                _hotkeys[i] = new TextBox { Width = 190, Location = new Point(156, 33 + i * 62) };
                hot.Controls.Add(new Label { Text = labels[i], AutoSize = true, Location = new Point(16, 37 + i * 62) });
                hot.Controls.Add(_hotkeys[i]);
            }
            hot.Controls.Add(new Label { AutoSize = true, Location = new Point(16, 297),
                Text = T("例如 Ctrl+Alt+R；留空则禁用；支持字母、数字、F1–F12。", "E.g. Ctrl+Alt+R; blank = disabled; letters, digits, F1–F12.") });
            hotkeyTab.Controls.Add(hot);

            var general = Panel();
            AddRow(general, 18, T("界面语言（重启生效）", "Language (requires restart)"), _language, 49);
            _language.Items.AddRange(new object[] { T("跟随系统", "Follow system"), "简体中文", "English" });
            _startup.Text = T("登录 Windows 时自动启动（默认关闭）", "Launch when signing in to Windows");
            _startup.Location = new Point(16, 116);
            general.Controls.Add(_startup);
            general.Controls.Add(new Label { AutoSize = true, Location = new Point(16, 170),
                Text = T("无主窗口 · 原生系统托盘 · 本地录音", "No main window · Native tray · Local recording") });
            generalTab.Controls.Add(general);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var cancel = new Button { Text = T("取消", "Cancel"), Location = new Point(278, 9), Width = 80 };
            cancel.Click += (_, __) => Close();
            var save = new Button { Text = T("保存设置", "Save"), Location = new Point(368, 9), Width = 94 };
            save.Click += (_, __) => Save();
            footer.Controls.Add(cancel);
            footer.Controls.Add(save);
            Controls.Add(tabs);
            Controls.Add(footer);
        }

        private static Panel Panel() => new Panel { Dock = DockStyle.Fill };
        private static void AddRow(Panel panel, int labelY, string label, Control field, int fieldY)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(16, labelY) });
            field.Location = new Point(16, fieldY);
            panel.Controls.Add(field);
        }

        private void LoadValues(AppSettings settings)
        {
            _format.SelectedItem = string.Equals(settings.Format, "WAV", StringComparison.OrdinalIgnoreCase) ? "WAV" : "MP3";
            _left.SelectedIndex = new[] { "last_used", "mic", "loopback", "both" }.ToList().IndexOf(settings.TrayClickMode);
            if (_left.SelectedIndex < 0) _left.SelectedIndex = 0;
            _language.SelectedIndex = settings.Language == "zh_CN" ? 1 : settings.Language == "en_US" ? 2 : 0;
            _normalize.Checked = settings.Normalize;
            _clipboard.Checked = settings.Clipboard;
            _deleteAfter.Checked = settings.DeleteAfter;
            _deleteAfter.Enabled = settings.Clipboard;
            _startup.Checked = Startup.Enabled;
            var list = new[] { settings.HotkeyMic, settings.HotkeySystem, settings.HotkeyBoth, settings.HotkeyStop };
            for (int i = 0; i < 4; i++) _hotkeys[i].Text = list[i] ?? "";
        }

        private async Task RefreshDevicesAsync()
        {
            if (_loading) return;
            _loading = true;
            _refresh.Enabled = false;
            _deviceStatus.Text = T("正在检查设备…", "Checking devices…");
            string remembered = (_mic.SelectedItem as Device)?.Id ?? _original.DeviceId;
            try
            {
                var results = await Task.Run(() =>
                {
                    using (var enumerator = new MMDeviceEnumerator())
                    {
                        var found = new List<Device> { new Device { Id = "", Name = T("系统默认麦克风", "Default microphone") } };
                        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                            found.Add(new Device { Id = device.ID, Name = device.FriendlyName });
                        return found;
                    }
                });
                if (IsDisposed) return;
                _mic.Items.Clear();
                foreach (var d in results) _mic.Items.Add(d);
                _mic.SelectedIndex = Math.Max(0, results.FindIndex(d => d.Id == remembered));
                _deviceStatus.Text = T("已找到 ", "Found ") + (results.Count - 1) + T(" 个设备", " devices");
            }
            catch (Exception ex)
            {
                Diagnostics.Write("Device scan: " + ex);
                _deviceStatus.Text = T("设备检查失败，可使用默认设备", "Device scan failed; default device available");
                _mic.Items.Clear();
                _mic.Items.Add(new Device { Id = "", Name = T("系统默认麦克风", "Default microphone") });
                _mic.SelectedIndex = 0;
            }
            finally { if (!IsDisposed) _refresh.Enabled = true; _loading = false; }
        }

        private void Save()
        {
            var settings = _original.Clone();
            settings.DeviceId = (_mic.SelectedItem as Device)?.Id ?? "";
            settings.OutputFolder = _folder.Text.Trim();
            settings.Format = _format.SelectedItem?.ToString() ?? "MP3";
            settings.TrayClickMode = new[] { "last_used", "mic", "loopback", "both" }[Math.Max(0, _left.SelectedIndex)];
            settings.Language = new[] { "system", "zh_CN", "en_US" }[Math.Max(0, _language.SelectedIndex)];
            settings.Normalize = _normalize.Checked;
            settings.Clipboard = _clipboard.Checked;
            settings.DeleteAfter = _deleteAfter.Checked && _clipboard.Checked;
            settings.HotkeyMic = _hotkeys[0].Text.Trim();
            settings.HotkeySystem = _hotkeys[1].Text.Trim();
            settings.HotkeyBoth = _hotkeys[2].Text.Trim();
            settings.HotkeyStop = _hotkeys[3].Text.Trim();
            try
            {
                settings.Validate();
                string error = SaveRequested?.Invoke(settings, _startup.Checked);
                if (error != null) { MessageBox.Show(this, error, Text); return; }
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        }

        private sealed class Device
        {
            public string Id;
            public string Name;
            public override string ToString() => Name;
        }
    }
}
