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
            Text = T("MiniRec 设置", "MiniRec Settings");
            // WinForms dialogs otherwise show the generic window icon in the taskbar.
            // Use the native icon embedded in MiniRec.exe, matching File Explorer.
            try
            {
                using (var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    if (executableIcon != null)
                        Icon = (Icon)executableIcon.Clone();
            }
            catch (Exception ex)
            {
                Diagnostics.Write("Settings icon: " + ex);
            }
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(520, 430);
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
            var page = new TabPage(T("录音设置", "Recording")) { Padding = new Padding(12) };
            var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddField(layout, 0, T("麦克风设备", "Microphone"), _mic);
            _refresh.Text = T("刷新", "Refresh");
            _refresh.AutoSize = true;
            _refresh.Margin = new Padding(8, 2, 0, 2);
            _refresh.Click += async (_, __) => await RefreshDevicesAsync();
            layout.Controls.Add(_refresh, 2, 0);
            _tips.SetToolTip(_mic, T("可从下拉列表选择录音设备", "Select an available recording device"));

            // Device scan status gets its own row instead of shrinking the combobox.
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _deviceStatus.Text = T("准备检查设备", "Ready to check devices");
            _deviceStatus.ForeColor = SystemColors.GrayText;
            _deviceStatus.AutoSize = true;
            _deviceStatus.Margin = new Padding(0, 0, 0, 9);
            layout.Controls.Add(_deviceStatus, 1, 1);
            layout.SetColumnSpan(_deviceStatus, 2);

            AddField(layout, 2, T("保存位置", "Save location"), _folder);
            var browse = new Button
            {
                Text = T("浏览…", "Browse…"), AutoSize = true,
                Margin = new Padding(8, 2, 0, 2)
            };
            browse.Click += BrowseForFolder;
            layout.Controls.Add(browse, 2, 2);
            _folder.TextChanged += (_, __) => _tips.SetToolTip(_folder, _folder.Text);

            AddField(layout, 3, T("录音格式", "Format"), FormatPanel());
            AddField(layout, 4, T("托盘左键", "Tray left click"), _left);
            _left.Items.AddRange(new object[]
            {
                T("使用上次录音模式", "Last used mode"),
                T("录制麦克风", "Record microphone"),
                T("录制电脑声音", "Record system audio"),
                T("混合录音", "Record both")
            });

            var options = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
                Margin = new Padding(102, 10, 0, 0),
                Padding = Padding.Empty
            };
            _normalize.Text = T("录音后标准化音量", "Normalize volume after recording");
            _clipboard.Text = T("录音后复制文件到剪贴板", "Copy file to clipboard after recording");
            _deleteAfter.Text = T("复制前将文件移至临时目录", "Move file to temporary folder before copying");
            _normalize.Margin = new Padding(0, 5, 0, 5);
            _clipboard.Margin = new Padding(0, 5, 0, 5);
            _deleteAfter.Margin = new Padding(20, 5, 0, 5);
            _clipboard.CheckedChanged += (_, __) => _deleteAfter.Enabled = _clipboard.Checked;
            options.Controls.Add(_normalize, 0, 0);
            options.Controls.Add(_clipboard, 0, 1);
            options.Controls.Add(_deleteAfter, 0, 2);

            // Docking top fills the available width while AutoSize determines height.
            content.Controls.Add(options);
            content.Controls.Add(layout);
            page.Controls.Add(content);
            return page;
        }

        private TabPage BuildHotkeyGeneralTab()
        {
            var page = new TabPage(T("快捷键与常规", "Hotkeys & General"))
            {
                Padding = new Padding(12)
            };
            var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var sections = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var hotkeyGroup = new GroupBox
            {
                Text = T("全局快捷键", "Global hotkeys"),
                Dock = DockStyle.Fill, AutoSize = true,
                Margin = new Padding(0, 2, 0, 10),
                Padding = new Padding(12, 18, 12, 8)
            };
            var hotkeyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            string[] labels = _english
                ? new[] { "Record microphone", "Record system audio", "Record both", "Stop and save" }
                : new[] { "录制麦克风", "录制电脑声音", "混合录音", "停止并保存" };

            for (int i = 0; i < labels.Length; i++)
            {
                // Each shortcut has a full-width input with its label above, not
                // a narrow label squeezed next to an even narrower textbox.
                var entry = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1,
                    Margin = new Padding(i % 2 == 0 ? 0 : 8, 0,
                        i % 2 == 0 ? 8 : 0, 7),
                    Padding = Padding.Empty
                };
                entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var label = new Label
                {
                    Text = labels[i], AutoSize = true, Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 0, 3)
                };
                _hotkeys[i] = new KeyGestureTextBox
                {
                    Dock = DockStyle.Top,
                    Margin = Padding.Empty
                };
                entry.Controls.Add(label, 0, 0);
                entry.Controls.Add(_hotkeys[i], 0, 1);
                hotkeyLayout.Controls.Add(entry, i % 2, i / 2);
            }

            var hint = new Label
            {
                Text = T("数字小键盘 + / - 可直接录入；也支持组合键。\nEsc 取消，Delete 清除；留空表示禁用。",
                         "Use numpad + / - directly, or press a key combination.\nEsc cancels, Delete clears; blank disables."),
                AutoSize = true, Dock = DockStyle.Top,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 5, 0, 0)
            };
            hotkeyLayout.Controls.Add(hint, 0, 2);
            hotkeyLayout.SetColumnSpan(hint, 2);
            hotkeyGroup.Controls.Add(hotkeyLayout);

            var generalGroup = new GroupBox
            {
                Text = T("常规", "General"),
                Dock = DockStyle.Fill, AutoSize = true,
                Margin = Padding.Empty,
                Padding = new Padding(12, 18, 12, 10)
            };
            var general = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            general.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _startup.Text = T("登录 Windows 时自动启动", "Launch when signing in to Windows");
            _startup.Dock = DockStyle.Top;
            _startup.Margin = new Padding(0, 2, 0, 10);
            general.Controls.Add(_startup, 0, 0);
            general.SetColumnSpan(_startup, 2);

            var languageLabel = new Label
            {
                Text = T("界面语言", "Language"),
                AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 10, 4)
            };
            general.Controls.Add(languageLabel, 0, 1);
            _language.Dock = DockStyle.Fill;
            _language.Width = 160;
            _language.MinimumSize = new Size(150, 0);
            _language.Margin = new Padding(0, 2, 0, 2);
            _language.Items.AddRange(new object[]
            {
                T("跟随系统", "Follow system"), "简体中文", "English"
            });
            general.Controls.Add(_language, 1, 1);

            var languageHint = new Label
            {
                Text = T("语言切换将在重启软件后生效。", "Language changes take effect after restarting."),
                AutoSize = true, Dock = DockStyle.Top,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0)
            };
            general.Controls.Add(languageHint, 0, 2);
            general.SetColumnSpan(languageHint, 2);
            generalGroup.Controls.Add(general);

            sections.Controls.Add(hotkeyGroup, 0, 0);
            sections.Controls.Add(generalGroup, 0, 1);
            content.Controls.Add(sections);
            page.Controls.Add(content);
            return page;
        }

        private static TableLayoutPanel Table(int columns) => new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns, Padding = new Padding(0), Margin = new Padding(0), GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        private static void AddField(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) }, 0, row); control.Margin = new Padding(0, 2, 0, 2); table.Controls.Add(control, 1, row);
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
            if (_loading || _closing) return;
            if (_deviceScan != null && !_deviceScan.IsCompleted)
            {
                _deviceStatus.Text = T("设备仍在检查中，请稍后重试", "Device scan is still running");
                _refresh.Enabled = false;
                return;
            }
            _loading = true;
            _refresh.Enabled = false;
            _deviceStatus.Text = T("正在检查设备…", "Checking devices…");
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
                    _deviceScan.ContinueWith(task =>
                    {
                        // The COM enumeration may outlive a timeout or the window itself.
                        // Never touch WinForms controls from the worker thread.
                        try
                        {
                            if (task.IsFaulted) Diagnostics.Write("Late device scan: " + task.Exception);
                            if (_closing || IsDisposed || !IsHandleCreated) return;
                            BeginInvoke((Action)(() =>
                            {
                                if (!_closing && !IsDisposed && _deviceScan == task)
                                    _refresh.Enabled = true;
                            }));
                        }
                        catch (InvalidOperationException) { } // Includes ObjectDisposedException.
                    }, TaskScheduler.Default);
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
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern short GetAsyncKeyState(int virtualKey);
            private string _beforeCapture;
            public KeyGestureTextBox() { ReadOnly = true; ShortcutsEnabled = false; }
            protected override void OnEnter(EventArgs e) { _beforeCapture = Text; base.OnEnter(e); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { Text = _beforeCapture ?? Text; Parent?.Focus(); return; }
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) { Text = ""; return; }
                // Win is not part of Keys.Modifiers. Check its physical state explicitly.
                if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin ||
                    (GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                    (GetAsyncKeyState(0x5C) & 0x8000) != 0)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                Keys modifiers = e.Modifiers & Keys.Modifiers;
                // Preserve Python version's single-key numeric keypad + and -.
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Subtract)
                {
                    if (modifiers == Keys.None)
                        Text = e.KeyCode == Keys.Add ? "+" : "-";
                    else
                    {
                        var tokens = new List<string>();
                        if ((modifiers & Keys.Control) != 0) tokens.Add("Ctrl");
                        if ((modifiers & Keys.Alt) != 0) tokens.Add("Alt");
                        if ((modifiers & Keys.Shift) != 0) tokens.Add("Shift");
                        tokens.Add(e.KeyCode == Keys.Add ? "NumPadAdd" : "NumPadSubtract");
                        Text = string.Join("+", tokens);
                    }
                    return;
                }
                bool validKey = (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) ||
                    (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) ||
                    (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) ||
                    (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F12);
                // Legacy shortcuts also permit single ordinary keys.
                if (!validKey) { System.Media.SystemSounds.Beep.Play(); return; }
                var parts = new List<string>();
                if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl");
                if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
                if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");
                string key;
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                    key = ((char)(int)e.KeyCode).ToString();
                else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
                    key = ((int)e.KeyCode - (int)Keys.D0).ToString();
                else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
                    key = ((int)e.KeyCode - (int)Keys.NumPad0).ToString();
                else
                    key = e.KeyCode.ToString();
                parts.Add(key);
                Text = string.Join("+", parts);
            }
        }
    }
}
