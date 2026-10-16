using System;
using System.Diagnostics;
using System.IO;

namespace QuickAudioRecorder
{
    // Open the configured recording directory via ShellExecute instead of passing
    // a user-controlled path as arguments to explorer.exe. Explorer treats '/' as
    // a command switch and can navigate to the parent of paths such as C:/Users/...
    internal static class RecordingFolderLauncher
    {
        internal static string NormalizePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                throw new ArgumentException("录音保存目录不能为空。", nameof(configuredPath));

            string expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (expanded.Length >= 2 && expanded[0] == '"' &&
                expanded[expanded.Length - 1] == '"')
                expanded = expanded.Substring(1, expanded.Length - 2);

            // Preserve UNC paths and Unicode folder names; accept either separator.
            string normalized = expanded.Replace(Path.AltDirectorySeparatorChar,
                                                 Path.DirectorySeparatorChar);
            return Path.GetFullPath(normalized);
        }

        internal static ProcessStartInfo CreateOpenFolderInfo(string configuredPath)
        {
            return new ProcessStartInfo
            {
                FileName = NormalizePath(configuredPath),
                UseShellExecute = true,
                Verb = "open"
            };
        }

        internal static void Open(string configuredPath)
        {
            ProcessStartInfo info = CreateOpenFolderInfo(configuredPath);
            Directory.CreateDirectory(info.FileName);
            // Use the folder as the shell target. Do not use explorer.exe /select
            // or append the folder to command-line arguments.
            Process.Start(info);
        }

        // Pure regression check invoked against the unpacked EXE by Windows CI.
        // Verifies the actual shell target without opening an Explorer window.
        internal static bool VerifyFolderPath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "MiniRec 测试 folder", "录音 文件");
            string forwardSlashes = folder.Replace('\\', '/');
            ProcessStartInfo info = CreateOpenFolderInfo(forwardSlashes);
            bool targetCorrect = string.Equals(
                info.FileName, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
            bool noExplorerArguments = info.UseShellExecute && info.Verb == "open" &&
                                       string.IsNullOrEmpty(info.Arguments);
            string unc = @"\\server\share\录音";
            bool uncPreserved = string.Equals(
                NormalizePath(unc), unc, StringComparison.OrdinalIgnoreCase);
            return targetCorrect && noExplorerArguments && uncPreserved;
        }
    }
}
