"""Quick Audio Recorder entry point (normal GUI or isolated device probe)."""

import sys


def main():
    # Must happen before importing Qt or the GUI. The frozen EXE also uses
    # this entry point for the isolated, timeout-controlled WASAPI probe.
    if len(sys.argv) >= 3 and sys.argv[1] == "--scan-devices":
        from device_probe import main as probe_main
        return probe_main(sys.argv[2])

    from PyQt6.QtWidgets import QApplication

    from diagnostics import configure_diagnostics, install_gui_watchdog
    configure_diagnostics()

    from gui import TrayApplication

    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)
    heartbeat_timer = install_gui_watchdog(app)
    tray = TrayApplication(app)
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
