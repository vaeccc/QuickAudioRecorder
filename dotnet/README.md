# QuickAudioRecorder — Windows 原生托盘版（预览）

这是 QuickAudioRecorder 的 **.NET Framework 4.8 + WinForms + NAudio 2.4.0** 重写原型。
原 Python / PyQt6 版本保留在仓库根目录及 `master` 分支，互不覆盖。

## 设计目标

- 不显示主窗口，启动后直接常驻系统托盘
- 左键点击托盘：使用上次录音模式快速开始或停止
- 右键：录制麦克风、电脑声音、混合录音、打开文件夹、设置、退出
- 系统原生快捷键 `RegisterHotKey`；冲突时拒绝保存有问题的绑定
- 通过 NAudio WASAPI 捕获麦克风与系统播放音频
- WAV / MP3；可选音量标准化；可选复制文件到剪贴板
- 不额外安装 .NET 10 Desktop Runtime；使用 Windows 自带的 .NET Framework 4.8/4.8.1
- 不引入数据库、后台服务、转写或音频编辑功能

## 当前状态

**未经过 Windows 实机编译、录音和性能验收，禁止视为正式版。**
本分支提供首个技术验证原型，GitHub Actions 自动执行 Windows 构建并生成预览 ZIP。
打开 GitHub Actions 的对应构建运行，在 Artifacts 中领取 ZIP；不是 Releases 正式包。

## 本地编译

需要 Windows 10/11、Visual Studio 2022（.NET Framework 4.8 targeting pack）和网络下载 NuGet 依赖：

```powershell
msbuild dotnet/QuickAudioRecorder/QuickAudioRecorder.csproj /restore /p:Configuration=Release /p:Platform=x64
```

发布的是 **EXE + NAudio DLL** 的便携目录，不是单独一个 EXE。复制完整文件夹即可运行，避免为嵌入 DLL 引入额外加载器。首次运行默认录音目录为“文档\\QuickAudioRecorder”。

## 迁移旧配置

尝试读取当前用户的 `%APPDATA%\\QuickAudioRecorder\\settings.json`，维持与 Python 版本相同的主要字段；升级前请备份该文件。
预览版首次运行会直接读取旧设置；保存会写回同一个文件。因此强烈建议**先关闭旧 Python 进程，备份 JSON，再测试新版本**。
两套程序不能同时常驻（老版不参与新版单实例锁）。

## 需要在真实 Windows 设备上验证

1. 空闲时资源占用与托盘退出 / 重新启动、Windows 10/11 的运行时兼容
2. 麦克风、系统声音和两者同时录音；耳机、蓝牙和采样率不同的设备
3. 长时间录音、设备消失、系统睡眠与硬盘写入失败
4. MP3 Media Foundation 编码器兼容性和音量标准化输出
5. 快捷键注册失败与冲突、开机启动和剪贴板文件粘贴
6. UI 语言、旧版 JSON 设置导入及设备刷新
7. 混合录音时间同步与主观音质；确认符合要求后再发布 Release

## 说明

混合录音采用两路 WAV 采集、流式重采样为 44.1 kHz/16-bit/stereo，再按启动时间作近似对齐并按半音量混音。该实现不会将整个录音读入内存，但不是样本级时钟同步；需要在目标设备上测试延迟和漂移。

录音异常时尝试将原始 WAV 保存到输出目录的 `Recovery` 子目录。日志在 `%LOCALAPPDATA%\\QuickAudioRecorder\\Logs\\app.log`。
