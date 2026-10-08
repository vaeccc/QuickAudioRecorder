"""Windows-friendly diagnostics and GUI heartbeat hang stack dumps."""

import faulthandler
import logging
import os
import threading
import time
from logging.handlers import RotatingFileHandler
from pathlib import Path

from PyQt6.QtCore import QTimer

LOGGER = logging.getLogger("quick_audio_recorder")
_watchdog_file = None


def logs_directory():
    root = os.environ.get("LOCALAPPDATA")
    if not root:
        root = str(Path.home() / "AppData" / "Local") if os.name == "nt" else str(Path.home() / ".local" / "state")
    return Path(root) / "QuickAudioRecorder" / "Logs"


def configure_diagnostics():
    directory = logs_directory()
    directory.mkdir(parents=True, exist_ok=True)
    handler = RotatingFileHandler(
        directory / "app.log", maxBytes=2_000_000, backupCount=2, encoding="utf-8"
    )
    handler.setFormatter(logging.Formatter(
        "%(asctime)s %(levelname)s [%(threadName)s] %(name)s: %(message)s"
    ))
    logging.getLogger().addHandler(handler)
    logging.getLogger().setLevel(logging.INFO)
    LOGGER.info("app.started")
    return directory


def install_gui_watchdog(app, timeout_seconds=8):
    """Dump Python stacks if the Qt event loop stops delivering timer ticks."""
    global _watchdog_file
    directory = logs_directory()
    directory.mkdir(parents=True, exist_ok=True)
    # Keep the file handle alive for faulthandler, including frozen GUI builds.
    _watchdog_file = open(directory / "hang-stacks.log", "a", encoding="utf-8")
    try:
        faulthandler.enable(file=_watchdog_file, all_threads=True)
    except Exception:
        LOGGER.exception("diagnostics.faulthandler_unavailable")
    heartbeat = [time.monotonic()]
    timer = QTimer(app)
    timer.setInterval(500)
    timer.timeout.connect(lambda: heartbeat.__setitem__(0, time.monotonic()))
    timer.start()

    def monitor():
        reported = False
        while True:
            time.sleep(1)
            stalled = time.monotonic() - heartbeat[0]
            if stalled > timeout_seconds:
                if not reported:
                    LOGGER.error("gui.hang_detected blocked_seconds=%.1f", stalled)
                    try:
                        _watchdog_file.write(
                            "\nGUI heartbeat stalled for %.1f seconds at %s\n"
                            % (stalled, time.strftime("%Y-%m-%d %H:%M:%S"))
                        )
                        _watchdog_file.flush()
                        faulthandler.dump_traceback(file=_watchdog_file, all_threads=True)
                        _watchdog_file.flush()
                    except Exception:
                        LOGGER.exception("diagnostics.stack_dump_failed")
                    reported = True
            else:
                if reported:
                    LOGGER.info("gui.responsive_again")
                reported = False

    threading.Thread(target=monitor, name="GuiHangWatchdog", daemon=True).start()
    return timer
