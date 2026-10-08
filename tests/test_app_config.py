"""Regression coverage for persistent settings and Windows startup command."""

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import app_config
import startup


class SettingsStorageTests(unittest.TestCase):
    def test_migrate_legacy_settings_and_write_atomically(self):
        with tempfile.TemporaryDirectory() as folder:
            work = Path(folder) / "legacy"
            work.mkdir()
            (work / "settings.json").write_text(
                '{"language":"zh_CN","hk_mic":"ctrl+alt+r"}', encoding="utf-8",
            )
            with patch.dict(os.environ, {"APPDATA": str(Path(folder) / "user")}), patch(
                "app_config.Path.cwd", return_value=work
            ):
                dest = app_config.settings_path()
            self.assertEqual(json.loads(Path(dest).read_text("utf-8"))["language"], "zh_CN")
            app_config.write_settings(dest, {"language": "en_US", "tray_click_mode": "mic"})
            self.assertEqual(json.loads(Path(dest).read_text("utf-8"))["language"], "en_US")
            self.assertEqual(list(Path(dest).parent.glob("*.tmp")), [])

    def test_invalid_data_cannot_corrupt_previous_json(self):
        with tempfile.TemporaryDirectory() as folder:
            dest = str(Path(folder) / "settings.json")
            app_config.write_settings(dest, {"hk_stop": "ctrl+alt+s"})
            with self.assertRaises(TypeError):
                app_config.write_settings(dest, {"broken": object()})
            self.assertEqual(json.loads(Path(dest).read_text("utf-8"))["hk_stop"], "ctrl+alt+s")
            self.assertEqual(list(Path(dest).parent.glob("*.tmp")), [])


class StartupTests(unittest.TestCase):
    def test_frozen_executable_command_includes_quoted_full_path(self):
        with patch.object(startup.sys, "frozen", True, create=True), patch.object(
            startup.sys, "executable", str(Path(tempfile.gettempdir()) / "Audio Recorder" / "Recorder.exe")
        ):
            command = startup.startup_command()
        self.assertIn("Recorder.exe", command)
        self.assertTrue(command.startswith('"'))
        self.assertTrue(command.endswith('"'))

    def test_disabled_when_no_registry_entry(self):
        with patch("startup.registry_command", return_value=None):
            self.assertFalse(startup.is_startup_enabled())


if __name__ == "__main__":
    unittest.main()
