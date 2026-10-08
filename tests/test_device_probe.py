"""Test audio enumeration JSON output with a stubbed soundcard backend."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import device_probe


class FakeMicrophone:
    name = "模拟麦克风"
    id = "device-01"


class FakeAudioBackend:
    def all_microphones(self, include_loopback=False):
        assert not include_loopback
        return [FakeMicrophone()]

    def default_microphone(self):
        return FakeMicrophone()


class ProbeTests(unittest.TestCase):
    def test_device_enumeration_preserves_unicode_names(self):
        result = device_probe.enumerate_devices(FakeAudioBackend())
        self.assertEqual(result["devices"], [{"name": "模拟麦克风", "id": "device-01"}])
        self.assertEqual(result["default_id"], "device-01")

    def test_main_writes_json_file_without_stdout(self):
        with tempfile.TemporaryDirectory() as folder:
            dest = str(Path(folder) / "devices.json")
            with patch("device_probe.enumerate_devices", return_value={
                "devices": [{"name": "麦克风", "id": "X"}], "default_id": "X"
            }):
                self.assertEqual(device_probe.main(dest), 0)
            self.assertEqual(json.loads(Path(dest).read_text("utf-8"))["devices"][0]["name"], "麦克风")

    def test_main_writes_error_json(self):
        with tempfile.TemporaryDirectory() as folder:
            dest = str(Path(folder) / "devices.json")
            with patch("device_probe.enumerate_devices", side_effect=RuntimeError("no audio")):
                self.assertEqual(device_probe.main(dest), 1)
            self.assertFalse(json.loads(Path(dest).read_text("utf-8"))["ok"])


if __name__ == "__main__":
    unittest.main()
