using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
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
        private readonly ComboBox _mic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly TextBox _folder = new TextBox { Dock = DockStyle.Fill };
        private readonly RadioButton _mp3 = new RadioButton { AutoSize = true };
        private readonly RadioButton _wav = new RadioButton { AutoSize = true };
        private readonly ComboBox _left = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly ComboBox _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckBox _normalize = new CheckBox { AutoSize = true };
        private readonly CheckBox _clipboard = new CheckBox { AutoSize = true };
        private readonly CheckBox _deleteAfter = new CheckBox { AutoSize = true, Margin = new Padding(24, 3, 0, 3) };
        private readonly CheckBox _startup = new CheckBox { AutoSize = true };
        private readonly KeyGestureTextBox[] _hotkeys = new KeyGestureTextBox[4];
        private readonly Button _refresh = new Button { AutoSize = true };
        private readonly Label _deviceStatus = new Label { AutoSize = true };
        private readonly ToolTip _tips = new ToolTip();
        private readonly ErrorProvider _errors = new ErrorProvider();
        private CancellationTokenSource _refreshCancellation;
        private Task<List<Device>> _deviceScan;
        private bool _loading;
        private bool _closing;

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
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(560, 475);
            MinimumSize = new Size(500, 420);
            _errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
            BuildUi();
            LoadValues(settings);
            Shown += async (_, __) => await RefreshDevicesAsync();
            FormClosing += (_, __) => { _closing = true; _refreshCancellation?.Cancel(); };
            FormClosed += (_, __) => { _refreshCancellation?.Dispose(); _tips.Dispose(); _errors.Dispose(); };
        }

        private string T(string zh, string en) => _english ? en : zh;

        private void BuildUi()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
            tabs.TabPages.Add(BuildAudioTab());
            tabs.TabPages.Add(BuildHotkeyGeneralTab());
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(8, 10, 12, 8) };
            var save = new Button { Text = T("保存设置", "Save settings"), AutoSize = true, Height = 28 };
            var cancel = new Button { Text = T("取消", "Cancel"), AutoSize = true, Height = 28, DialogResult = DialogResult.Cancel };
            save.Click += (_, __) => Save();
            cancel.Click += (_, __) => Close();
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            AcceptButton = save; CancelButton = cancel;
            Controls.Add(tabs); Controls.Add(footer);
        }

        private TabPage BuildAudioTab()
        {
            var page = new TabPage(T("录音设置", "Recording")) { Padding = new Padding(14) };
            var table = Table(4);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddField(table, 0, T("麦克风设备", "Microphone"), _mic);
            _refresh.Text = T("刷新", "Refresh"); _refresh.Margin = new Padding(8, 0, 0, 0); _refresh.Click += async (_, __) => await RefreshDevicesAsync();
            table.Controls.Add(_refresh, 2, 0);
            _deviceStatus.Text = T("准备检查设备", "Ready to check devices"); _deviceStatus.ForeColor = SystemColors.GrayText; _deviceStatus.Margin = new Padding(8, 5, 0, 0); table.Controls.Add(_deviceStatus, 3, 0);
            _tips.SetToolTip(_mic, T("可从下拉列表选择录音设备", "Select an available recording device"));
            AddField(table, 1, T("保存位置", "Save location"), _folder);
            var browse = new Button { Text = T("浏览…", "Browse…"), AutoSize = true, Margin = new Padding(8, 0, 0, 0) }; browse.Click += BrowseForFolder; table.Controls.Add(browse, 2, 1);
            _folder.TextChanged += (_, __) => _tips.SetToolTip(_folder, _folder.Text);
            AddField(table, 2, T("录音格式", "Format"), FormatPanel());
            AddField(table, 3, T("托盘左键", "Tray left click"), _left);
            _left.Items.AddRange(new object[] { T("使用上次录音模式", "Last used mode"), T("录制麦克风", "Record microphone"), T("录制电脑声音", "Record system audio"), T("混合录音", "Record both") });
            var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = new Padding(102, 14, 0, 0) };
            options.Controls.Add(_normalize, 0, 0); options.Controls.Add(_clipboard, 0, 1); options.Controls.Add(_deleteAfter, 0, 2);
            _normalize.Text = T("录音后标准化音量", "Normalize volume after recording");
            _clipboard.Text = T("录音后复制文件到剪贴板", "Copy file to clipboard after recording");
            _deleteAfter.Text = T("复制前将文件移至临时目录", "Move file to temporary folder before copying");
            _clipboard.CheckedChanged += (_, __) => _deleteAfter.Enabled = _clipboard.Checked;
            var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; content.Controls.Add(options); content.Controls.Add(table); page.Controls.Add(content);
            return page;
        }

        private TabPage BuildHotkeyGeneralTab()
        {
            var page = new TabPage(T("快捷键与常规", "Hotkeys & General")) { Padding = new Padding(14) };
            var table = Table(2); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            var hotkeyGroup = new GroupBox { Text = T("全局快捷键", "Global hotkeys"), Dock = DockStyle.Fill, Padding = new Padding(12, 20, 12, 10), AutoSize = true };
            var hotkeys = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 5 };
            hotkeys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); hotkeys.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            string[] labels = _english ? new[] { "Record microphone", "Record system audio", "Record both", "Stop and save" } : new[] { "录制麦克风", "录制电脑声音", "混合录音", "停止并保存" };
            for (int i = 0; i < labels.Length; i++)
            {
                _hotkeys[i] = new KeyGestureTextBox { Dock = DockStyle.Fill, Margin = new Padding(8, 2, 0, 2) };
                hotkeys.Controls.Add(new Label { Text = labels[i], AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 0, 5) }, 0, i);
                hotkeys.Controls.Add(_hotkeys[i], 1, i);
            }
            var hint = new Label { Text = T("点击后按组合键；Esc 保留原值，Delete 清除。留空表示禁用。", "Click and press a combination; Esc keeps the old value, Delete clears it. Blank disables it."), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 8, 0, 0) };
            hotkeys.Controls.Add(hint, 0, 4); hotkeys.SetColumnSpan(hint, 2); hotkeyGroup.Controls.Add(hotkeys);
            var generalGroup = new GroupBox { Text = T("常规", "General"), Dock = DockStyle.Fill, Padding = new Padding(12, 20, 12, 10), AutoSize = true };
            var general = Table(2); general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            var hint2 = new Label { Text = T("托盘左键操作可在“录音设置”中调整。", "Tray left-click behavior is on the Recording tab."), AutoSize = true, ForeColor = SystemColors.GrayText }; general.Controls.Add(hint2, 0, 0); general.SetColumnSpan(hint2, 2);
            _startup.Text = T("登录 Windows 时自动启动", "Launch when signing in to Windows"); general.Controls.Add(_startup, 0, 1);
            AddLabeledCombo(general, 2, T("界面语言", "Language"), _language); _language.Items.AddRange(new object[] { T("跟随系统", "Follow system"), "简体中文", "English" });
            var hint3 = new Label { Text = T("语言切换在重启软件后生效。", "Language changes take effect after restarting."), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 10, 0, 0) }; general.Controls.Add(hint3, 0, 3); general.SetColumnSpan(hint3, 2);
            generalGroup.Controls.Add(general); table.Controls.Add(hotkeyGroup, 0, 0); table.Controls.Add(generalGroup, 1, 0); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.Controls.Add(table);
            return page;
        }

        private static TableLayoutPanel Table(int columns) => new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns, Padding = new Padding(0), Margin = new Padding(0), GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        private static void AddField(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) }, 0, row); control.Margin = new Padding(0, 2, 0, 2); table.Controls.Add(control, 1, row);
        }
        private static void AddLabeledCombo(TableLayoutPanel table, int row, string label, Control combo)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) }, 0, row); table.Controls.Add(combo, 1, row);
        }
        private Control FormatPanel()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) }; _mp3.Text = "MP3"; _wav.Text = "WAV"; panel.Controls.Add(_mp3); panel.Controls.Add(_wav); return panel;
        }
        private void BrowseForFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = _folder.Text, Description = T("选择录音保存目录", "Select output folder") }) if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
        }

        private void LoadValues(AppSettings settings)
        {
            _folder.Text = settings.OutputFolder ?? ""; _mp3.Checked = !string.Equals(settings.Format, "WAV", StringComparison.OrdinalIgnoreCase); _wav.Checked = !_mp3.Checked;
            _left.SelectedIndex = new[] { "last_used", "mic", "loopback", "both" }.ToList().IndexOf(settings.TrayClickMode); if (_left.SelectedIndex < 0) _left.SelectedIndex = 0;
            _language.SelectedIndex = settings.Language == "zh_CN" ? 1 : settings.Language == "en_US" ? 2 : 0; _normalize.Checked = settings.Normalize; _clipboard.Checked = settings.Clipboard; _deleteAfter.Checked = settings.DeleteAfter; _deleteAfter.Enabled = settings.Clipboard; _startup.Checked = Startup.Enabled;
            var values = new[] { settings.HotkeyMic, settings.HotkeySystem, settings.HotkeyBoth, settings.HotkeyStop }; for (int i = 0; i < _hotkeys.Length; i++) _hotkeys[i].Text = values[i] ?? "";
        }

        private async Task RefreshDevicesAsync()
        {
            if (_loading || _closing) return; _loading = true; _refresh.Enabled = false; _deviceStatus.Text = T("正在检查设备…", "Checking devices…");
            if (_deviceScan != null && !_deviceScan.IsCompleted) return;
            string remembered = (_mic.SelectedItem as Device)?.Id ?? _original.DeviceId ?? ""; var previous = _mic.SelectedItem as Device;
            _refreshCancellation?.Cancel(); _refreshCancellation?.Dispose(); _refreshCancellation = new CancellationTokenSource();
            try
            {
                _deviceScan = Task.Run(() => EnumerateDevices(), _refreshCancellation.Token);
                var completed = await Task.WhenAny(_deviceScan, Task.Delay(TimeSpan.FromSeconds(10), _refreshCancellation.Token));
                if (completed != _deviceScan) throw new TimeoutException("音频设备检查超时。");
                var results = await _deviceScan; if (_closing || IsDisposed) return;
                _mic.BeginUpdate(); try { _mic.Items.Clear(); foreach (var device in results) _mic.Items.Add(device); int selected = results.FindIndex(d => d.Id == remembered); _mic.SelectedIndex = selected >= 0 ? selected : 0; } finally { _mic.EndUpdate(); }
                _deviceStatus.Text = T("已找到 ", "Found ") + Math.Max(0, results.Count - 1) + T(" 个设备", " devices");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Diagnostics.Write("Device scan: " + ex); if (!_closing && !IsDisposed) { if (_mic.Items.Count == 0) _mic.Items.Add(new Device { Id = "", Name = T("系统默认麦克风", "Default microphone") }); if (previous != null && !_mic.Items.Contains(previous)) _mic.Items.Add(previous); if (previous != null) _mic.SelectedItem = previous; else _mic.SelectedIndex = 0; _deviceStatus.Text = ex is TimeoutException ? T("检查设备超时，已保留当前选择", "Device check timed out; current selection retained") : T("检查失败，已保留当前选择", "Check failed; current selection retained"); }
            }
            finally
            {
                _loading = false;
                if (!_closing && !IsDisposed) _refresh.Enabled = _deviceScan == null || _deviceScan.IsCompleted;
                if (_deviceScan != null && !_deviceScan.IsCompleted)
                    _deviceScan.ContinueWith(_ => { if (!_closing && !IsDisposed) BeginInvoke((Action)(() => _refresh.Enabled = true)); }, TaskScheduler.Default);
            }
        }
        private List<Device> EnumerateDevices()
        {
            using (var enumerator = new MMDeviceEnumerator()) { var found = new List<Device> { new Device { Id = "", Name = T("系统默认麦克风", "Default microphone") } }; foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)) found.Add(new Device { Id = device.ID, Name = device.FriendlyName }); return found; }
        }

        private void Save()
        {
            _errors.Clear(); var settings = _original.Clone(); settings.DeviceId = (_mic.SelectedItem as Device)?.Id ?? _original.DeviceId ?? ""; settings.OutputFolder = _folder.Text.Trim(); settings.Format = _wav.Checked ? "WAV" : "MP3"; settings.TrayClickMode = new[] { "last_used", "mic", "loopback", "both" }[Math.Max(0, _left.SelectedIndex)]; settings.Language = new[] { "system", "zh_CN", "en_US" }[Math.Max(0, _language.SelectedIndex)]; settings.Normalize = _normalize.Checked; settings.Clipboard = _clipboard.Checked; settings.DeleteAfter = _deleteAfter.Checked && _clipboard.Checked; settings.HotkeyMic = _hotkeys[0].Text.Trim(); settings.HotkeySystem = _hotkeys[1].Text.Trim(); settings.HotkeyBoth = _hotkeys[2].Text.Trim(); settings.HotkeyStop = _hotkeys[3].Text.Trim();
            try { settings.Validate(); string error = SaveRequested?.Invoke(settings, _startup.Checked); if (error != null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } Close(); }
            catch (Exception ex) { _errors.SetError(_folder, ex.Message); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private sealed class Device { public string Id; public string Name; public override string ToString() => Name; }
        private sealed class KeyGestureTextBox : TextBox
        {
            private string _beforeCapture;
            public KeyGestureTextBox() { ReadOnly = true; ShortcutsEnabled = false; }
            protected override void OnEnter(EventArgs e) { _beforeCapture = Text; base.OnEnter(e); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { Text = _beforeCapture ?? Text; Parent?.Focus(); return; }
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) { Text = ""; return; }
                if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin || e.Modifiers.HasFlag(Keys.LWin) || e.Modifiers.HasFlag(Keys.RWin)) { System.Media.SystemSounds.Beep.Play(); return; }
                Keys modifiers = e.Modifiers & Keys.Modifiers; bool validKey = (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) || (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) || (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F12);
                if (modifiers == Keys.None || !validKey) { System.Media.SystemSounds.Beep.Play(); return; }
                var parts = new List<string>(); if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl"); if ((modifiers & Keys.Alt) != 0) parts.Add("Alt"); if ((modifiers & Keys.Shift) != 0) parts.Add("Shift"); string key = e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9 ? ((int)e.KeyCode - (int)Keys.NumPad0).ToString() : e.KeyCode.ToString().Replace("D", ""); parts.Add(key); Text = string.Join("+", parts);
            }
        }
    }
}
