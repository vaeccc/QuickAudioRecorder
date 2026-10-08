"""Timeout-bounded out-of-process device discovery for the Qt settings UI."""

import json
import logging
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from PyQt6.QtCore import QObject, QProcess, QTimer, pyqtSignal

LOGGER = logging.getLogger(__name__)


def probe_command():
    """Build a command that also works in a PyInstaller one-file GUI build."""
    if getattr(sys, "frozen", False):
        return sys.executable, ["--scan-devices"]
    return sys.executable, [str(Path(__file__).resolve().parent / "main.py"), "--scan-devices"]


class DeviceScanner(QObject):
    completed = pyqtSignal(bool, list, object, str)
    busy_changed = pyqtSignal(bool)

    def __init__(self, parent=None, timeout_ms=15000):
        super().__init__(parent)
        self.timeout_ms = timeout_ms
        self._process = None
        self._result_file = None
        self._cancelled = False
        self._timed_out = False
        self._started_at = None
        self._timer = QTimer(self)
        self._timer.setSingleShot(True)
        self._timer.timeout.connect(self._on_timeout)

    @property
    def busy(self):
        return self._process is not None

    def start_scan(self):
        if self.busy:
            return False
        fd, path = tempfile.mkstemp(prefix="quick-audio-devices-", suffix=".json")
        os.close(fd)
        self._result_file = path
        process = QProcess(self)
        process.finished.connect(self._on_finished)
        process.errorOccurred.connect(self._on_error)
        self._process = process
        self._cancelled = False
        self._timed_out = False
        self._started_at = time.monotonic()
        executable, args = probe_command()
        process.setProgram(executable)
        process.setArguments([*args, path])
        self.busy_changed.emit(True)
        LOGGER.info("device_scan.started timeout_ms=%s", self.timeout_ms)
        self._timer.start(self.timeout_ms)
        process.start()
        return True

    def cancel(self):
        if not self.busy:
            return
        self._cancelled = True
        LOGGER.info("device_scan.cancel_requested")
        self._stop_worker()

    def _on_timeout(self):
        if not self.busy:
            return
        self._timed_out = True
        LOGGER.warning("device_scan.timeout")
        self._stop_worker()

    def _stop_worker(self):
        process = self._process
        if process is None:
            return
        pid = process.processId()
        if os.name == "nt" and pid:
            # PyInstaller --onefile may spawn a child bootloader process.
            # taskkill /T requests termination of the entire process tree.
            try:
                subprocess.Popen(
                    ["taskkill", "/F", "/T", "/PID", str(pid)],
                    stdin=subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                )
                QTimer.singleShot(600, self._force_kill)
                return
            except OSError:
                LOGGER.exception("device_scan.taskkill_failed")
        self._force_kill()

    def _force_kill(self):
        process = self._process
        if process is not None and process.state() != QProcess.ProcessState.NotRunning:
            process.kill()

    def _on_error(self, error):
        if error == QProcess.ProcessError.FailedToStart:
            self._complete(False, [], None, "Device scan process could not start.")

    def _on_finished(self, exit_code, exit_status):
        if self._cancelled:
            self._complete(False, [], None, "Device scan cancelled.")
            return
        if self._timed_out:
            self._complete(False, [], None, "Device scan timed out.")
            return
        if exit_status != QProcess.ExitStatus.NormalExit:
            self._complete(False, [], None, "Device scan process crashed.")
            return
        try:
            with open(self._result_file, "r", encoding="utf-8") as stream:
                payload = json.load(stream)
            if not isinstance(payload, dict):
                raise ValueError("Malformed response")
            if not payload.get("ok"):
                raise ValueError(str(payload.get("error", "Unknown device scan error")))
            devices = payload["devices"]
            if not isinstance(devices, list) or any(
                not isinstance(d, dict)
                or not isinstance(d.get("name"), str)
                or not isinstance(d.get("id"), str)
                for d in devices
            ):
                raise ValueError("Malformed device list")
            self._complete(True, devices, payload.get("default_id"), "")
        except (OSError, ValueError, KeyError, TypeError) as exc:
            LOGGER.warning("device_scan.invalid_result type=%s", type(exc).__name__)
            self._complete(False, [], None, "Device scan failed. Check diagnostic logs.")

    def _complete(self, success, devices, default_id, message):
        if self._process is None:
            return
        self._timer.stop()
        process = self._process
        self._process = None
        process.deleteLater()
        path = self._result_file
        self._result_file = None
        if path:
            try:
                os.unlink(path)
            except OSError:
                pass
        elapsed = time.monotonic() - self._started_at
        LOGGER.info("device_scan.finished success=%s elapsed_s=%.2f", success, elapsed)
        self.busy_changed.emit(False)
        self.completed.emit(success, devices, default_id, message)
