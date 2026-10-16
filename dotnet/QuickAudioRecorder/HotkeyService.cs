using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickAudioRecorder
{
    internal sealed class HotkeyService : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const uint ModNoRepeat = 0x4000;
        private readonly List<Binding> _registered = new List<Binding>();
        public event Action<int> Triggered;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr handle, int id);

        private sealed class Binding
        {
            public int Id;
            public uint Modifiers;
            public uint Key;
        }

        public HotkeyService() { CreateHandle(new CreateParams()); }

        public bool TryUpdate(AppSettings settings, out string error)
        {
            var requested = new List<Binding>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = new[] { settings.HotkeyMic, settings.HotkeySystem, settings.HotkeyBoth, settings.HotkeyStop };
            var names = new[] { "录制麦克风", "录制电脑声音", "混合录音", "停止并保存" };
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i])) continue;
                Binding binding;
                if (!TryParse(values[i], i + 1, out binding))
                {
                    error = "无效快捷键: " + values[i] + "（例如 Ctrl+Alt+R）";
                    return false;
                }
                string key = binding.Modifiers + ":" + binding.Key;
                if (!unique.Add(key))
                {
                    error = "快捷键重复：" + names[i] + " 与 " + owners[key] + " 都使用 " + values[i];
                    return false;
                }
                owners[key] = names[i];
                requested.Add(binding);
            }

            var old = new List<Binding>(_registered);
            UnregisterAll();
            foreach (var binding in requested)
            {
                if (!RegisterHotKey(Handle, binding.Id, binding.Modifiers | ModNoRepeat, binding.Key))
                {
                    error = "快捷键被其他程序占用：" + names[binding.Id - 1] + "（" + values[binding.Id - 1] + "）";
                    UnregisterAll();
                    foreach (var previous in old)
                    {
                        if (RegisterHotKey(Handle, previous.Id, previous.Modifiers | ModNoRepeat, previous.Key))
                            _registered.Add(previous);
                    }
                    return false;
                }
                _registered.Add(binding);
            }
            error = null;
            return true;
        }

        public void Suspend() { UnregisterAll(); }

        private static bool TryParse(string value, int id, out Binding binding)
        {
            binding = null;
            uint modifiers = 0, key = 0;
            foreach (string raw in value.Replace(" ", "").Split('+'))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= 0x0002; break;
                    case "alt": modifiers |= 0x0001; break;
                    case "shift": modifiers |= 0x0004; break;
                    case "win": case "windows": modifiers |= 0x0008; break;
                    default:
                        if (key != 0) return false;
                        var part = raw.ToUpperInvariant();
                        if (part.Length == 1 && part[0] >= 'A' && part[0] <= 'Z') key = part[0];
                        else if (part.Length == 1 && part[0] >= '0' && part[0] <= '9') key = part[0];
                        else if (part.StartsWith("F") && int.TryParse(part.Substring(1), out int f) && f >= 1 && f <= 12)
                            key = (uint)(Keys.F1 + (f - 1));
                        else return false;
                        break;
                }
            }
            if (key == 0 || modifiers == 0) return false;
            binding = new Binding { Id = id, Modifiers = modifiers, Key = key };
            return true;
        }

        private void UnregisterAll()
        {
            foreach (var binding in _registered) UnregisterHotKey(Handle, binding.Id);
            _registered.Clear();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey) Triggered?.Invoke(message.WParam.ToInt32());
            base.WndProc(ref message);
        }

        public void Dispose() { UnregisterAll(); DestroyHandle(); }
    }
}
