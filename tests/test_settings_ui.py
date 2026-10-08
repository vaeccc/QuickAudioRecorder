"""Test settings save without real microphone discovery, registry writes or Windows hooks."""

import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt6.QtWidgets import QApplication

import gui


class SettingsWindowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def test_settings_save_is_nonmodal_and_does_not_touch_hotkeys(self):
        with tempfile.TemporaryDirectory() as folder:
            config_file = str(Path(folder) / "settings.json")
            with patch.object(gui, "CONFIG_FILE", config_file), patch(
                "gui.get_devices", return_value=[]
            ), patch("gui.sc.default_microphone", return_value=None), patch(
                "gui.is_startup_enabled", return_value=False
            ), patch("gui.set_startup_enabled") as startup_change:
                window = gui.SettingsWindow()
                saved = []
                window.settings_saved.connect(lambda: saved.append(True))
                window.combo_language.setCurrentIndex(window.combo_language.findData("zh_CN"))
                window.save_settings()
                self.assertEqual(saved, [True])
                self.assertTrue(Path(config_file).is_file())
                self.assertTrue(window.lbl_save_status.text())
                startup_change.assert_not_called()
                window.close()

    def test_startup_enabled_only_on_saving(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(gui, "CONFIG_FILE", str(Path(folder) / "settings.json")), patch(
                "gui.get_devices", return_value=[]
            ), patch("gui.sc.default_microphone", return_value=None), patch(
                "gui.is_startup_enabled", return_value=False
            ), patch("gui.set_startup_enabled") as startup_change:
                window = gui.SettingsWindow()
                window.chk_autostart.setEnabled(True)
                window.chk_autostart.setChecked(True)
                startup_change.assert_not_called()
                window.save_settings()
                startup_change.assert_called_once_with(True)
                window.close()


if __name__ == "__main__":
    unittest.main()
