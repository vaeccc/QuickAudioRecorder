"""User-facing messages and backward-compatible language/settings helpers."""

import json
import os


# Keep source strings as stable keys so English remains the fallback.
ZH_CN = {
    "Settings - Quick Audio Recorder": "设置 - Quick Audio Recorder",
    "Input Device": "输入设备",
    "Refresh Devices": "刷新设备",
    "Output Configuration": "输出设置",
    "Browse...": "浏览...",
    "Folder:": "文件夹：",
    "Format:": "格式：",
    "Tray Icon Behavior": "托盘图标行为",
    "Left Click Action:": "左键单击操作：",
    "Last Used": "上次使用的模式",
    "Microphone": "麦克风",
    "Loopback": "系统声音",
    "Both": "混合录音",
    "Post-Processing & Clipboard": "音频处理与剪贴板",
    "Normalize Audio (Apply first)": "音量标准化（优先处理）",
    "Copy File to Clipboard": "将录音文件复制到剪贴板",
    "Delete after Copy (Move to Temp)": "复制后移至临时文件夹",
    "Moves the file to the system temp folder before copying, keeping your output folder clean.": "复制前将文件移至系统临时文件夹，保持输出目录整洁。",
    "Global Hotkeys": "全局快捷键",
    "Record Mic:": "录制麦克风：",
    "Record Loopback:": "录制系统声音：",
    "Record Both:": "录制混合声音：",
    "Stop Recording:": "停止录音：",
    "Save Settings": "保存设置",
    "Select Output Folder": "选择录音保存文件夹",
    "Settings": "设置",
    "Settings saved successfully.": "设置已保存。",
    "Error": "错误",
    "Failed to save settings: {error}": "保存设置失败：{error}",
    "Click to set hotkey...": "点击并按下快捷键...",
    "Start Recording (Mic)": "开始录制（麦克风）",
    "Start Recording (Loopback)": "开始录制（系统声音）",
    "Start Recording (Both)": "开始录制（混合声音）",
    "Stop Recording": "停止录音",
    "Exit": "退出",
    "Quick Audio Recorder (Idle)": "Quick Audio Recorder（空闲）",
    "Ready": "准备就绪",
    "Left-click to toggle recording.": "左键单击托盘图标可开始或停止录音。",
    "Recording ({mode})...": "正在录音（{mode}）...",
    "Started": "已开始",
    "Recording {mode}": "正在录制{mode}",
    "Recording failed: {error}": "录音失败：{error}",
    "Could not detect System Audio loopback device.": "无法检测到系统声音回环录音设备。",
    "Saved to {filename}": "已保存至 {filename}",
    "Moved to Temp & Copied to Clipboard.": "已移至临时文件夹并复制到剪贴板。",
    "Copied to clipboard.": "已复制到剪贴板。",
    "Clipboard Error: {error}": "剪贴板错误：{error}",
    "Clipboard/Move error: {error}": "剪贴板或文件移动失败：{error}",
    "Finished": "录音完成",
    "Language:": "语言：",
    "Follow system": "跟随系统",
    "Language changes take effect after restarting the app.": "语言更改将在下次启动软件时生效。",
    "Startup": "开机自启动",
    "Start when I sign in to Windows": "登录 Windows 时自动启动",
    "Settings saved, but Windows startup could not be updated: {error}": "设置已保存，但无法更新开机自启动：{error}",
    "Hotkey error": "快捷键错误",
    "The hotkey {hotkey} is assigned to multiple actions.": "快捷键 {hotkey} 已分配给多个操作。",
    "Could not register hotkey {hotkey}: {error}": "无法注册快捷键 {hotkey}：{error}",
}

# Values stored by older versions were translated display labels.
LEGACY_TRAY_MODES = {
    "Last Used": "last_used",
    "Microphone": "mic",
    "Loopback": "loopback",
    "Both": "both",
}
TRAY_MODES = {"last_used", "mic", "loopback", "both"}


def tr(message, language="en_US", **kwargs):
    """Translate UI copy; never translate persistent IDs or device names."""
    template = ZH_CN.get(message, message) if language == "zh_CN" else message
    return template.format(**kwargs) if kwargs else template


def resolve_language(preference="system", system_locale=None):
    """Resolve system/zh_CN/en_US to an actual supported language."""
    if preference in ("zh_CN", "en_US"):
        return preference
    if system_locale is None:
        from PyQt6.QtCore import QLocale

        system_locale = QLocale.system().name()
    return "zh_CN" if str(system_locale).lower().startswith("zh") else "en_US"


def normalize_tray_mode(value):
    """Migrate the older English display strings without changing behavior."""
    canonical = LEGACY_TRAY_MODES.get(value, value)
    return canonical if canonical in TRAY_MODES else "last_used"


def read_settings(path):
    """Read an optional settings JSON file; keep defaults on malformed files."""
    try:
        with open(path, "r", encoding="utf-8") as stream:
            data = json.load(stream)
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError, UnicodeError):
        return {}


def ui_language(settings_path="settings.json"):
    config = read_settings(settings_path)
    return resolve_language(config.get("language", "system"))
