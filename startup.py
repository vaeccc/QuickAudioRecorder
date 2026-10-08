"""Opt-in startup at Windows user logon (no administrator rights required)."""

import os
import subprocess
import sys
from pathlib import Path


RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
RUN_VALUE = "QuickAudioRecorder"


def startup_command():
    """Make startup independent of the working directory, with no console for source runs."""
    executable = Path(sys.executable).resolve()
    if getattr(sys, "frozen", False):
        return subprocess.list2cmdline([str(executable)])
    if executable.name.lower() == "python.exe":
        candidate = executable.with_name("pythonw.exe")
        if candidate.is_file():
            executable = candidate
    script = Path(__file__).resolve().parent / "main.py"
    return subprocess.list2cmdline([str(executable), str(script)])


def registry_command():
    if os.name != "nt":
        return None
    import winreg

    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_READ) as key:
            value, _ = winreg.QueryValueEx(key, RUN_VALUE)
            return value
    except FileNotFoundError:
        return None


def is_startup_enabled():
    return registry_command() == startup_command()


def set_startup_enabled(enabled):
    if os.name != "nt":
        raise OSError("Windows startup is supported on Windows only.")
    import winreg

    if enabled:
        with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as key:
            winreg.SetValueEx(key, RUN_VALUE, 0, winreg.REG_SZ, startup_command())
    else:
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as key:
                winreg.DeleteValue(key, RUN_VALUE)
        except FileNotFoundError:
            pass
