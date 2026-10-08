"""Exercise settings controls without real devices or the Windows registry."""

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt6.QtCore import QObject, pyqtSignal
from PyQt6.QtWidgets import QApplication, QFileDialog
import gui


class FakeScanner(QObject):
    completed = pyqtSignal(bool, list, object, str)
    busy_changed = pyqtSignal(bool)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.busy = False

    def start_scan(self):
        self.busy = True
        self.busy_changed.emit(True)
        return True

    def cancel(self):
        self.busy = False
        self.busy_changed.emit(False)


class SettingsWindowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def make_window(self, config_file):
        patches = [
            patch.object(gui, "CONFIG_FILE", config_file),
            patch("gui.DeviceScanner", FakeScanner),
            patch("gui.is_startup_enabled", return_value=False),
        ]
        for mocked in patches:
            mocked.start()
            self.addCleanup(mocked.stop)
        window = gui.SettingsWindow()
        self.addCleanup(window.close)
        return window

    def test_settings_save_is_nonmodal_and_does_not_touch_hotkeys(self):
        with tempfile.TemporaryDirectory() as folder:
            config_file = str(Path(folder) / "settings.json")
            window = self.make_window(config_file)
            saved = []
            window.settings_saved.connect(lambda: saved.append(True))
            window.combo_language.setCurrentIndex(window.combo_language.findData("zh_CN"))
            with patch("gui.set_startup_enabled") as startup_change:
                window.save_settings()
                startup_change.assert_not_called()
            self.assertEqual(saved, [True])
            self.assertTrue(Path(config_file).is_file())
            self.assertTrue(window.lbl_save_status.text())

    def test_startup_enabled_only_on_saving(self):
        with tempfile.TemporaryDirectory() as folder:
            window = self.make_window(str(Path(folder) / "settings.json"))
            with patch("gui.set_startup_enabled") as startup_change:
                window.chk_autostart.setEnabled(True)
                window.chk_autostart.setChecked(True)
                startup_change.assert_not_called()
                window.save_settings()
                startup_change.assert_called_once_with(True)

    def test_browse_uses_qt_non_native_dialog_and_supports_pasted_path(self):
        with tempfile.TemporaryDirectory() as folder:
            window = self.make_window(str(Path(folder) / "settings.json"))
            with patch.object(QFileDialog, "getExistingDirectory", return_value=folder) as dialog:
                window.browse_folder()
                self.assertEqual(window.lbl_folder.text(), folder)
                self.assertEqual(
                    dialog.call_args.kwargs["options"],
                    QFileDialog.Option.DontUseNativeDialog,
                )
            direct_path = str(Path(folder) / "my audio files")
            window.lbl_folder.setText(direct_path)
            self.assertEqual(window.get_settings()["output_folder"], direct_path)
            window.save_settings()
            self.assertEqual(
                json.loads(Path(gui.CONFIG_FILE).read_text("utf-8"))["output_folder"],
                direct_path,
            )

    def test_cancel_and_failed_refresh_preserve_previous_devices(self):
        with tempfile.TemporaryDirectory() as folder:
            window = self.make_window(str(Path(folder) / "settings.json"))
            window.device_scanner.busy = False
            window.on_devices_loaded(True, [{"name": "Existing Mic", "id": "x"}], "x", "")
            self.assertEqual(window.combo_mic.currentData(), "x")
            window.refresh_devices()
            self.assertTrue(window.device_scanner.busy)
            window.refresh_devices()  # second click cancels
            self.assertFalse(window.device_scanner.busy)
            window.on_devices_loaded(False, [], None, "Device scan timed out.")
            self.assertEqual(window.combo_mic.currentData(), "x")


if __name__ == "__main__":
    unittest.main()
