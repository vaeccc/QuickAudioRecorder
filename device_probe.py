"""Isolated Windows audio device probe.

This module runs in a separate process. It must not import the GUI or create a
QApplication: a faulty WASAPI device enumeration must not block the GUI.
"""

import json
import os
import sys


def enumerate_devices(backend=None):
    if backend is None:
        import soundcard as backend

    devices = [
        {"name": str(device.name), "id": str(device.id)}
        for device in backend.all_microphones(include_loopback=False)
    ]
    default_id = None
    try:
        default = backend.default_microphone()
        if default is not None:
            default_id = str(default.id)
    except Exception:
        # Listing devices should still work when there is no default microphone.
        pass
    return {"devices": devices, "default_id": default_id}


def main(result_path):
    """Write JSON to a file because PyInstaller --noconsole has no stdout."""
    try:
        result = enumerate_devices()
        payload = {"ok": True, **result}
        status = 0
    except Exception as exc:
        payload = {"ok": False, "error": f"{type(exc).__name__}: {exc}"}
        status = 1
    try:
        with open(result_path, "w", encoding="utf-8") as output:
            json.dump(payload, output, ensure_ascii=False)
            output.flush()
            os.fsync(output.fileno())
    except OSError:
        return 2
    return status


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
