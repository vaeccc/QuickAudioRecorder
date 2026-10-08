# QuickAudioRecorder v1.0.0

这是 QuickAudioRecorder 的首个正式 Windows 发行版，提供 Windows x64 单文件 EXE，无需安装 Python。

## 功能

- 麦克风、系统声音（Loopback）与混合录音。
- MP3 / WAV、音量标准化、剪贴板复制和可自定义的全局快捷键。
- 简体中文、English 和跟随系统语言，切换语言后重启软件生效。
- 可选的 Windows 登录后自动启动，默认关闭，不需要管理员权限。

## 稳定性修复

- 浏览文件夹采用 Qt 非原生对话框，也能直接粘贴保存路径。
- 刷新设备使用独立子进程与超时控制。扫描可取消，失败保留现有设备列表。
- 快捷键注册从 GUI 线程移出，录音/界面操作由 Qt 信号回到主线程。
- 配置迁移至 %APPDATA%\\QuickAudioRecorder\\settings.json，并采用原子写入。
- 诊断日志：%LOCALAPPDATA%\\QuickAudioRecorder\\Logs。

## 下载与使用

1. 下载附件 QuickAudioRecorder.exe，放到长期使用的位置。
2. 双击启动，在 Windows 系统托盘找到录音图标，右键进入设置。
3. 选择语言、设备、输出文件夹、格式及快捷键。
4. 如需登录后自动启动，在设置中勾选自启动并保存。

本 EXE 未进行代码签名。如果 Windows SmartScreen 显示未知发布者，请先核对本页提供的 SHA-256 校验值与文件来源，仅在确认信任后运行。

## 验证情况

- GitHub Actions：Windows 自动化单元测试、PyInstaller x64 构建、冻结 EXE 设备探针与 JSON 通信验证通过。
- 用户 Windows 实机反馈：浏览文件夹、刷新设备、麦克风、系统声音及混合录音均正常。
- 全局快捷键和开机自启动尚未得到单独的实机验收确认，不标记为已通过。

---

## English

First official Windows x64 release of QuickAudioRecorder, including microphone, system loopback and mixed recording; MP3/WAV; configurable global hotkeys; Simplified Chinese/English localization; optional Windows startup; and responsive settings with isolated device discovery.

Portable unsigned executable; SHA-256 checksum attached.

Windows CI tests, build and frozen audio probe passed. User confirmed microphone, loopback, mixed recording, folder selection and device refresh. Global hotkeys and startup are implemented but have not been separately confirmed on the user's installation.
