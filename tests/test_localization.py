"""Regression tests for Chinese/English UI message and legacy configuration."""

import json
import os
import tempfile
import unittest

from localization import (
    ZH_CN,
    normalize_tray_mode,
    read_settings,
    resolve_language,
    tr,
)


class LocalizationTests(unittest.TestCase):
    def test_chinese_mode_and_formatting(self):
        self.assertEqual(tr("Settings", "zh_CN"), "设置")
        self.assertEqual(
            tr("Recording {mode}", "zh_CN", mode="麦克风"),
            "正在录制麦克风",
        )

    def test_english_and_unknown_message_fallback(self):
        self.assertEqual(tr("Settings", "en_US"), "Settings")
        self.assertEqual(tr("Untranslated UI message", "zh_CN"), "Untranslated UI message")

    def test_system_locale_fallback(self):
        self.assertEqual(resolve_language("system", "zh_CN"), "zh_CN")
        self.assertEqual(resolve_language("system", "zh_HK"), "zh_CN")
        self.assertEqual(resolve_language("system", "en_US"), "en_US")
        self.assertEqual(resolve_language("system", "ja_JP"), "en_US")
        self.assertEqual(resolve_language("invalid", "zh_CN"), "zh_CN")
        self.assertEqual(resolve_language("en_US", "zh_CN"), "en_US")

    def test_legacy_and_canonical_tray_modes(self):
        for old, new in {
            "Last Used": "last_used",
            "Microphone": "mic",
            "Loopback": "loopback",
            "Both": "both",
            "last_used": "last_used",
            "mic": "mic",
            "loopback": "loopback",
            "both": "both",
        }.items():
            with self.subTest(old=old):
                self.assertEqual(normalize_tray_mode(old), new)
        self.assertEqual(normalize_tray_mode("unknown"), "last_used")

    def test_settings_file_round_trip_and_invalid_data(self):
        with tempfile.TemporaryDirectory() as folder:
            filename = os.path.join(folder, "settings.json")
            self.assertEqual(read_settings(filename), {})
            data = {"language": "zh_CN", "tray_click_mode": "Both", "hk_mic": "ctrl+alt+r"}
            with open(filename, "w", encoding="utf-8") as stream:
                json.dump(data, stream, ensure_ascii=False)
            self.assertEqual(read_settings(filename), data)
            with open(filename, "w", encoding="utf-8") as stream:
                stream.write("{broken")
            self.assertEqual(read_settings(filename), {})
            with open(filename, "w", encoding="utf-8") as stream:
                json.dump(["not", "object"], stream)
            self.assertEqual(read_settings(filename), {})

    def test_complete_translation_catalog(self):
        self.assertTrue(all(key and value for key, value in ZH_CN.items()))


if __name__ == "__main__":
    unittest.main()
