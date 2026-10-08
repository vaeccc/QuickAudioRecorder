# Quick Audio Recorder 中文说明

[English](README.md)

Quick Audio Recorder 是一款运行在 **Windows** 系统托盘中的轻量级录音工具，支持麦克风、系统声音（Loopback）和两者混合录音。可以用托盘图标或全局快捷键快速开始、停止录音。

## 主要功能

- **麦克风录音**：只录制麦克风输入。
- **系统声音录音**：录制电脑播放的声音。
- **混合录音**：将麦克风和系统声音合成为同一个录音文件。
- **输出格式**：MP3 或 WAV。
- **全局快捷键**：分别指定录制麦克风、系统声音、混合声音与停止录音的快捷键。
- **音量标准化**：录音结束后按需要自动调整音量。
- **剪贴板**：可将录音文件复制到剪贴板，或在复制前移动到系统临时文件夹。

## 如何使用

1. 启动软件，右下角系统托盘会出现录音图标。
2. **右键单击**托盘图标，选择「设置」。
3. 选择麦克风、录音保存位置和输出格式。
4. 按需设置全局快捷键、左键单击操作及录音后的处理方式。
5. **左键单击**托盘图标或使用快捷键开始录音；再次单击或使用停止快捷键即可结束。

## 设置界面语言

在「设置」顶部的语言下拉框选择：

- **跟随系统**：Windows 为中文时使用简体中文，其他语言默认使用英文。
- **简体中文**：始终使用简体中文界面。
- **English**：始终使用英文界面。

保存后**退出并重新启动软件**即可切换界面语言。旧版 `settings.json` 中的英文托盘模式值会自动兼容，不会修改录音文件或快捷键配置。

注意：麦克风名称、文件名、技术异常的原始信息等可能沿用系统语言或英文。语言切换不影响录音内容。

## 运行与打包

需要 Python 3.12 及以下依赖：

```powershell
pip install PyQt6 soundcard soundfile numpy lameenc keyboard
python main.py
```

打包为单文件 EXE：

```powershell
pip install pyinstaller
pyinstaller --noconsole --onefile --name QuickAudioRecorder main.py
```

生成的程序位于 `dist/QuickAudioRecorder.exe`。

## 反馈

如发现未翻译文本、中文显示溢出、快捷键异常或录音问题，请在 GitHub Issues 反馈并附上 Windows 版本和复现步骤。
