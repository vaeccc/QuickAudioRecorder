"""Register global hotkeys without blocking Qt's GUI event loop."""

import logging
import queue
import threading


class HotkeyManager:
    """Own only our hotkey handles; never unhook unrelated application's bindings."""

    def __init__(self, notify, report_error, backend=None):
        if backend is None:
            import keyboard
            backend = keyboard
        self._backend = backend
        self._notify = notify
        self._report_error = report_error
        self._pending = queue.Queue()
        self._last_requested = None
        self._closed = False
        self._thread = threading.Thread(target=self._worker, name="RecorderHotkeys", daemon=True)
        self._thread.start()

    def update(self, bindings):
        """Enqueue hook work; callers on the GUI thread return immediately."""
        if self._closed:
            return
        cleaned = {
            action: (hotkey or "").strip().lower()
            for action, hotkey in bindings.items()
        }
        if cleaned != self._last_requested:
            self._last_requested = cleaned.copy()
            self._pending.put(cleaned)

    def _worker(self):
        handles = []
        while True:
            requested = self._pending.get()
            try:
                for handle in handles:
                    try:
                        self._backend.remove_hotkey(handle)
                    except Exception:
                        logging.exception("Failed to remove old hotkey")
                handles.clear()
                if requested is None:
                    return
                seen = {}
                for action, key in requested.items():
                    if not key:
                        continue
                    if key in seen:
                        self._report_error("The hotkey {hotkey} is assigned to multiple actions.".format(hotkey=key))
                        continue
                    seen[key] = action
                    try:
                        handle = self._backend.add_hotkey(
                            key,
                            lambda name=action: self._notify(name),
                        )
                        handles.append(handle)
                    except Exception as exc:
                        logging.exception("Failed to register hotkey %s", key)
                        self._report_error("Could not register hotkey {hotkey}: {error}".format(
                            hotkey=key, error=exc,
                        ))
            finally:
                self._pending.task_done()

    def wait_idle(self):
        """For regression tests; never call this from the GUI thread."""
        self._pending.join()

    def close(self):
        if not self._closed:
            self._closed = True
            self._pending.put(None)
