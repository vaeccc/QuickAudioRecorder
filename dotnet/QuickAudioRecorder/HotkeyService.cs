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
                    error = "无效快捷键: " + values[i] + "（例如 Ctrl+Alt+R、数字小键盘 + 或 -）";
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
            if (string.IsNullOrWhiteSpace(value)) return false;

            // Legacy Python settings store a bare "+" / "-" for the numeric keypad.
            // Do not split "+" as a modifier delimiter or translate it to Shift+OemPlus.
            string normalized = value.Trim();
            if (normalized == "+")
            {
                binding = new Binding { Id = id, Key = (uint)Keys.Add };
                return true;
            }
            if (normalized == "-")
            {
                binding = new Binding { Id = id, Key = (uint)Keys.Subtract };
                return true;
            }

            uint modifiers = 0, key = 0;
            foreach (string raw in normalized.Replace(" ", "").Split('+'))
            {
                if (raw.Length == 0) return false;
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= 0x0002; break;
                    case "alt": modifiers |= 0x0001; break;
                    case "shift": modifiers |= 0x0004; break;
                    case "win": case "windows": modifiers |= 0x0008; break;
                    case "numpadadd": case "add": key = key == 0 ? (uint)Keys.Add : 0; break;
                    case "numpadsubtract": case "subtract": case "minus": key = key == 0 ? (uint)Keys.Subtract : 0; break;
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
                if (key == 0 && (raw.Equals("add", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("numpadadd", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("subtract", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("numpadsubtract", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals("minus", StringComparison.OrdinalIgnoreCase)))
                    return false; // Duplicate physical key.
            }
            if (key == 0) return false;
            binding = new Binding { Id = id, Modifiers = modifiers, Key = key };
            return true;
        }

        // Pure parser regression checks; no global hotkeys are registered.
        internal static bool VerifyLegacyShortcuts()
        {
            Binding b;
            return TryParse("+", 1, out b) && b.Key == (uint)Keys.Add && b.Modifiers == 0 &&
                   TryParse("-", 4, out b) && b.Key == (uint)Keys.Subtract && b.Modifiers == 0 &&
                   TryParse("Ctrl+Alt+D", 1, out b) && b.Key == (uint)Keys.D &&
                   TryParse("Shift+NumPadAdd", 1, out b) && b.Key == (uint)Keys.Add && b.Modifiers == 0x0004 &&
                   TryParse("Ctrl+NumPadSubtract", 4, out b) && b.Key == (uint)Keys.Subtract &&
                   TryParse("F12", 2, out b) && b.Key == (uint)Keys.F12 &&
                   !TryParse("Ctrl++", 1, out b);
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
