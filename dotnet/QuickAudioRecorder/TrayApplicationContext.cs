using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickAudioRecorder
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly HotkeyService _hotkeys;
        private readonly RecordingService _recorder = new RecordingService();
        private readonly Timer _timer = new Timer { Interval = 1000 };
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Icon _idleIcon;
        private readonly Icon _recordingIcon;
        private SettingsForm _settingsForm;
        private AppSettings _settings;
        private AudioSource _lastMode = AudioSource.Microphone;
        private bool _busy;
        private readonly bool _english;

        public TrayApplicationContext()
        {
            Diagnostics.Write("Tray initialization: loading settings.");
            _settings = SettingsStore.Load();
            _english = _settings.Language == "en_US" ||
                (_settings.Language == "system" &&
                 !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh"));
            _idleIcon = IconResources.LoadIdle();
            _recordingIcon = IconResources.LoadRecording();
            _tray = new NotifyIcon { Icon = _idleIcon, Visible = true, ContextMenuStrip = _menu };
            Diagnostics.Write("Tray initialization: NotifyIcon created and visible.");
            _hotkeys = new HotkeyService();
            Diagnostics.Write("Tray initialization: hotkey service created.");
            _tray.MouseClick += async (_, e) =>
            {
                if (e.Button == MouseButtons.Left) await ToggleAsync();
            };
            _timer.Tick += (_, __) => UpdateTrayAppearance();
            _hotkeys.Triggered += async id =>
            {
                if (id == 4) await StopAsync();
                else if (id >= 1 && id <= 3) await StartAsync((AudioSource)(id - 1));
            };
            if (!_hotkeys.TryUpdate(_settings, out string error))
                Diagnostics.Write("Hotkey initialization: " + error);
            RefreshTray();
            Diagnostics.Write("Tray initialization complete.");
        }

        private string T(string zh, string en) => _english ? en : zh;

        private async Task ToggleAsync()
        {
            if (_busy) return;
            if (_recorder.IsRecording) await StopAsync();
            else
            {
                var mode = _lastMode;
                switch (_settings.TrayClickMode)
                {
                    case "mic": mode = AudioSource.Microphone; break;
                    case "loopback": mode = AudioSource.System; break;
                    case "both": mode = AudioSource.Both; break;
                }
                await StartAsync(mode);
            }
        }

        private async Task StartAsync(AudioSource mode)
        {
            if (_busy || _recorder.IsRecording) return;
            _busy = true;
            RefreshTray();
            try
            {
                await _recorder.StartAsync(mode, _settings.Clone());
                _lastMode = mode;
                _clock.Restart();
                _timer.Start();
            }
            catch (Exception ex)
            {
                Diagnostics.Write("Start failed: " + ex);
                Balloon(T("录音启动失败", "Start failed"), ex.Message, ToolTipIcon.Error);
            }
            finally { _busy = false; RefreshTray(); }
        }

        private async Task StopAsync()
        {
            if (_busy || !_recorder.IsRecording) return;
            _busy = true;
            _timer.Stop();
            RefreshTray();
            try
            {
                var result = await _recorder.StopAsync();
                _clock.Stop();
                if (!result.Success)
                {
                    Balloon(T("保存失败", "Save failed"),
                        result.Error + (result.RecoveryFolder == null ? "" :
                        "\n" + T("已保留恢复文件: ", "Recovery files: ") + result.RecoveryFolder),
                        ToolTipIcon.Error);
                    return;
                }
                string path = result.FilePath;
                if (_settings.Clipboard)
                {
                    try
                    {
                        if (_settings.DeleteAfter)
                        {
                            string destination = Path.Combine(Path.GetTempPath(),
                                "qar_clip_" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                            File.Move(path, destination);
                            path = destination;
                        }
                        var paths = new StringCollection();
                        paths.Add(path);
                        Clipboard.SetFileDropList(paths);
                    }
                    catch (Exception ex) { Diagnostics.Write("Clipboard: " + ex); }
                }
                Balloon(T("录音已保存", "Recording saved"), Path.GetFileName(path), ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Diagnostics.Write("Stop failed: " + ex);
                Balloon(T("停止录音失败", "Stop failed"), ex.Message, ToolTipIcon.Error);
            }
            finally { _busy = false; RefreshTray(); }
        }

        private void UpdateTrayAppearance()
        {
            bool recording = _recorder.IsRecording;
            _tray.Icon = recording ? _recordingIcon : _idleIcon;
            string text = recording ? T("录音中 ", "Recording ") + _clock.Elapsed.ToString(@"hh\:mm\:ss")
                : _busy ? T("正在处理录音", "Processing recording") : "QuickAudioRecorder";
            _tray.Text = text.Length > 60 ? text.Substring(0, 60) : text;
        }

        private void RefreshTray()
        {
            bool recording = _recorder.IsRecording;
            UpdateTrayAppearance();
            _menu.Items.Clear();
            if (recording || _busy)
            {
                Add(T("停止并保存", "Stop and save"), async () => await StopAsync(), recording && !_busy);
                _menu.Items.Add(new ToolStripSeparator());
            }
            Add(T("录制麦克风", "Record microphone"), async () => await StartAsync(AudioSource.Microphone), !recording && !_busy);
            Add(T("录制电脑声音", "Record system audio"), async () => await StartAsync(AudioSource.System), !recording && !_busy);
            Add(T("混合录音", "Record both"), async () => await StartAsync(AudioSource.Both), !recording && !_busy);
            _menu.Items.Add(new ToolStripSeparator());
            Add(T("打开录音文件夹", "Open recording folder"), () =>
            {
                Directory.CreateDirectory(_settings.OutputFolder);
                Process.Start("explorer.exe", "\"" + _settings.OutputFolder + "\"");
                return Task.CompletedTask;
            }, !_busy);
            Add(T("设置…", "Settings…"), () => { OpenSettings(); return Task.CompletedTask; }, !_busy);
            _menu.Items.Add(new ToolStripSeparator());
            Add(T("退出", "Exit"), async () => await ExitAsync(), !_busy);
        }

        private void Add(string label, Func<Task> action, bool enabled)
        {
            var item = new ToolStripMenuItem(label) { Enabled = enabled };
            item.Click += async (_, __) =>
            {
                try { await action(); }
                catch (Exception ex) { Diagnostics.Write("Menu: " + ex); Balloon(T("操作失败", "Action failed"), ex.Message, ToolTipIcon.Error); }
            };
            _menu.Items.Add(item);
        }

        private void OpenSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                return;
            }
            _hotkeys.Suspend();
            SettingsForm form;
            try { form = new SettingsForm(_settings.Clone()); }
            catch
            {
                _hotkeys.TryUpdate(_settings, out _);
                throw;
            }
            form.SaveRequested = (next, autoStart) =>
            {
                if (!_hotkeys.TryUpdate(next, out string hotkeyError)) return hotkeyError;
                var previous = _settings;
                try
                {
                    SettingsStore.Save(next);
                    Startup.Set(autoStart);
                    _settings = next;
                    RefreshTray();
                    return null;
                }
                catch (Exception ex)
                {
                    _hotkeys.TryUpdate(previous, out _);
                    return ex.Message;
                }
            };
            _settingsForm = form;
            form.FormClosed += (_, __) =>
            {
                if (_settingsForm == form) _settingsForm = null;
                if (!_hotkeys.TryUpdate(_settings, out string error)) Diagnostics.Write("Hotkey restore after settings: " + error);
            };
            form.Show();
        }

        private void Balloon(string title, string message, ToolTipIcon icon)
        {
            if (!_tray.Visible) return;
            _tray.ShowBalloonTip(2500, title, message, icon);
        }

        private async Task ExitAsync()
        {
            if (_busy) return;
            if (_recorder.IsRecording) await StopAsync();
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _hotkeys.Dispose();
            _recordingIcon.Dispose();
            _idleIcon.Dispose();
            ExitThread();
        }

    }
}
