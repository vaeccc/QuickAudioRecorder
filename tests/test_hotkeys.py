"""Hotkey registration must remain asynchronous and not manipulate Qt widgets."""

import unittest

from hotkeys import HotkeyManager


class FakeKeyboard:
    def __init__(self):
        self.added = []
        self.removed = []
        self.callbacks = {}

    def add_hotkey(self, name, callback):
        self.added.append(name)
        self.callbacks[name] = callback
        return name

    def remove_hotkey(self, handle):
        self.removed.append(handle)
        self.callbacks.pop(handle, None)


class HotkeyManagerTests(unittest.TestCase):
    def setUp(self):
        self.backend = FakeKeyboard()
        self.events = []
        self.errors = []
        self.manager = HotkeyManager(
            self.events.append,
            lambda message, arguments: self.errors.append((message, arguments)),
            self.backend,
        )

    def tearDown(self):
        self.manager.close()
        self.manager.wait_idle()

    def test_rebind_without_touching_unchanged_shortcuts(self):
        self.manager.update({"mic": "Ctrl+Alt+R", "stop": "Ctrl+Alt+S"})
        self.manager.wait_idle()
        self.assertEqual(self.backend.added, ["ctrl+alt+r", "ctrl+alt+s"])
        self.backend.callbacks["ctrl+alt+r"]()
        self.assertEqual(self.events, ["mic"])

        self.manager.update({"mic": "Ctrl+Alt+R", "stop": "Ctrl+Alt+S"})
        self.manager.wait_idle()
        self.assertEqual(self.backend.added, ["ctrl+alt+r", "ctrl+alt+s"])
        self.assertEqual(self.backend.removed, [])

        self.manager.update({"mic": "", "stop": "ctrl+alt+z"})
        self.manager.wait_idle()
        self.assertEqual(self.backend.removed, ["ctrl+alt+r", "ctrl+alt+s"])
        self.assertEqual(self.backend.added[-1], "ctrl+alt+z")

    def test_duplicate_shortcuts_are_reported_not_registered_twice(self):
        self.manager.update({"mic": "ctrl+alt+r", "stop": "ctrl+alt+r"})
        self.manager.wait_idle()
        self.assertEqual(self.backend.added, ["ctrl+alt+r"])
        self.assertEqual(self.errors[0][1], {"hotkey": "ctrl+alt+r"})

    def test_bad_hotkey_is_reported_without_stopping_good_ones(self):
        original = self.backend.add_hotkey

        def add_hotkey(name, callback):
            if name == "invalid":
                raise ValueError("not supported")
            return original(name, callback)

        self.backend.add_hotkey = add_hotkey
        self.manager.update({"mic": "invalid", "stop": "ctrl+alt+s"})
        self.manager.wait_idle()
        self.assertEqual(self.backend.added, ["ctrl+alt+s"])
        self.assertEqual(self.errors[0][1]["error"], "not supported")


if __name__ == "__main__":
    unittest.main()
