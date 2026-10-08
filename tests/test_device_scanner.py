"""Test real QProcess success, timeout and cancellation without audio hardware."""

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PyQt6.QtCore import QEventLoop, QTimer
from PyQt6.QtWidgets import QApplication

from device_scanner import DeviceScanner


class DeviceScannerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def execute(self, worker_source, timeout_ms=1800):
        with tempfile.TemporaryDirectory() as folder:
            worker = Path(folder) / "worker.py"
            worker.write_text(worker_source, encoding="utf-8")
            scanner = DeviceScanner(timeout_ms=timeout_ms)
            loop = QEventLoop()
            results = []
            scanner.completed.connect(lambda *parts: (results.append(parts), loop.quit()))
            watchdog = QTimer()
            watchdog.setSingleShot(True)
            watchdog.timeout.connect(loop.quit)
            with patch("device_scanner.probe_command", return_value=(sys.executable, [str(worker)])):
                self.assertTrue(scanner.start_scan())
                self.assertFalse(scanner.start_scan())  # no overlapping scan
                watchdog.start(7000)
                loop.exec()
                watchdog.stop()
            if scanner.busy:
                scanner.cancel()
                self.fail("Device scanner did not finish in bounded time")
            self.assertEqual(len(results), 1)
            return results[0]

    def test_successful_worker_response(self):
        success, devices, default_id, message = self.execute(
            "import json,sys\n"
            "with open(sys.argv[-1], 'w', encoding='utf-8') as f:\n"
            "    json.dump({'ok': True, 'devices': [{'id':'mic-1','name':'Test Mic'}], "
            "'default_id':'mic-1'}, f)\n"
        )
        self.assertTrue(success, message)
        self.assertEqual(devices[0]["id"], "mic-1")
        self.assertEqual(default_id, "mic-1")

    def test_timeout_kills_unresponsive_probe(self):
        success, devices, default_id, message = self.execute(
            "import time\ntime.sleep(60)\n", timeout_ms=600
        )
        self.assertFalse(success)
        self.assertEqual(devices, [])
        self.assertEqual(message, "Device scan timed out.")

    def test_worker_failed_without_json(self):
        success, _, _, message = self.execute(
            "import sys\nsys.exit(1)\n"
        )
        self.assertFalse(success)
        self.assertIn("Device scan failed", message)


if __name__ == "__main__":
    unittest.main()
