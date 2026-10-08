"""Stable per-user settings storage for manual and Windows logon launches."""

import json
import os
import shutil
import sys
import tempfile
from pathlib import Path


APP_NAME = "QuickAudioRecorder"


def settings_path():
    """Use AppData rather than the current working directory (which varies at logon)."""
    root = os.environ.get("APPDATA")
    if not root:
        root = str(Path.home() / "AppData" / "Roaming") if os.name == "nt" else str(Path.home() / ".config")
    destination = Path(root) / APP_NAME / "settings.json"
    if not destination.is_file():
        # Older releases used settings.json relative to their working directory.
        candidates = (
            Path.cwd() / "settings.json",
            Path(sys.executable).resolve().parent / "settings.json",
            Path(__file__).resolve().parent / "settings.json",
        )
        for previous in candidates:
            if previous.is_file() and previous != destination:
                destination.parent.mkdir(parents=True, exist_ok=True)
                try:
                    shutil.copyfile(previous, destination)
                except OSError:
                    # A readable settings file must never prevent the app from starting.
                    pass
                break
    return str(destination)


def write_settings(path, values):
    """Write atomically, preserving the last working configuration on failure."""
    folder = os.path.dirname(os.path.abspath(path))
    os.makedirs(folder, exist_ok=True)
    name = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=folder, suffix=".tmp", delete=False) as stream:
            name = stream.name
            json.dump(values, stream, ensure_ascii=False, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if name and os.path.exists(name):
            os.unlink(name)
