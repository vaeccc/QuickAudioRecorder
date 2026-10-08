import sys
import os
import json
import shutil
import tempfile
import logging
import time
from PyQt6.QtWidgets import (QApplication, QSystemTrayIcon, QMenu, QMainWindow, 
                             QWidget, QVBoxLayout, QHBoxLayout, QLabel, QComboBox, 
                             QPushButton, QFileDialog, QMessageBox, QGroupBox, 
                             QLineEdit, QFormLayout, QCheckBox, QScrollArea)
from PyQt6.QtGui import QIcon, QAction, QColor, QPixmap, QPainter, QBrush, QKeySequence
from PyQt6.QtCore import pyqtSignal, pyqtSlot, QObject, Qt, QUrl, QMimeData, QDir
from device_scanner import DeviceScanner
from app_config import settings_path, write_settings
from hotkeys import HotkeyManager
from startup import is_startup_enabled, set_startup_enabled
from audio_recorder import AudioRecorder
from clipboard_utils import copy_file_to_clipboard
from localization import tr, ui_language, read_settings, normalize_tray_mode

CONFIG_FILE = settings_path()

def resource_path(relative_path):
    try:
        base_path = sys._MEIPASS
    except Exception:
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)

class SignalManager(QObject):
    recording_finished = pyqtSignal(str, str)
    hotkey_requested = pyqtSignal(str)
    hotkey_error = pyqtSignal(str, object)

class HotkeyEdit(QLineEdit):
    """
    Custom widget to capture hotkeys by pressing them.
    Maps Qt events to 'keyboard' library compatible strings.
    """
    def __init__(self, parent=None, language="en_US"):
        super().__init__(parent)
        self.setPlaceholderText(tr("Click to set hotkey...", language))
        self.setReadOnly(True) 
        self.current_sequence = None

    def mousePressEvent(self, event):
        self.setFocus()
        super().mousePressEvent(event)

    def keyPressEvent(self, event):
        key = event.key()
        modifiers = event.modifiers()
        
        if key == Qt.Key.Key_Backspace or key == Qt.Key.Key_Delete:
            self.clear()
            self.current_sequence = None
            return
            
        if key == Qt.Key.Key_Escape:
            self.clearFocus()
            return

        if key in (Qt.Key.Key_Control, Qt.Key.Key_Shift, Qt.Key.Key_Alt, Qt.Key.Key_Meta):
            return

        parts = []
        if modifiers & Qt.KeyboardModifier.ControlModifier: parts.append("ctrl")
        if modifiers & Qt.KeyboardModifier.ShiftModifier:   parts.append("shift")
        if modifiers & Qt.KeyboardModifier.AltModifier:     parts.append("alt")
        if modifiers & Qt.KeyboardModifier.MetaModifier:    parts.append("windows")

        key_text = ""
        if key >= 0x20 and key <= 0x7E:
            key_text = chr(key).lower()
        else:
            key_map = {
                Qt.Key.Key_F1: "f1", Qt.Key.Key_F2: "f2", Qt.Key.Key_F3: "f3", Qt.Key.Key_F4: "f4",
                Qt.Key.Key_F5: "f5", Qt.Key.Key_F6: "f6", Qt.Key.Key_F7: "f7", Qt.Key.Key_F8: "f8",
                Qt.Key.Key_F9: "f9", Qt.Key.Key_F10: "f10", Qt.Key.Key_F11: "f11", Qt.Key.Key_F12: "f12",
                Qt.Key.Key_Left: "left", Qt.Key.Key_Right: "right", Qt.Key.Key_Up: "up", Qt.Key.Key_Down: "down",
                Qt.Key.Key_Space: "space", Qt.Key.Key_Tab: "tab", Qt.Key.Key_Return: "enter", Qt.Key.Key_Enter: "enter",
                Qt.Key.Key_Backspace: "backspace", Qt.Key.Key_Delete: "delete", Qt.Key.Key_Insert: "insert",
                Qt.Key.Key_Home: "home", Qt.Key.Key_End: "end", Qt.Key.Key_PageUp: "pageup", Qt.Key.Key_PageDown: "pagedown",
                Qt.Key.Key_CapsLock: "capslock", Qt.Key.Key_NumLock: "numlock", Qt.Key.Key_ScrollLock: "scrolllock",
                Qt.Key.Key_Print: "print_screen", Qt.Key.Key_Pause: "pause"
            }
            key_text = key_map.get(key)
            if not key_text:
                try: key_text = QKeySequence(key).toString().lower()
                except: pass

        if key_text:
            parts.append(key_text)
            
        final_hotkey = "+".join(parts)
        self.setText(final_hotkey)
        self.current_sequence = final_hotkey
        self.clearFocus()

class SettingsWindow(QMainWindow):
    settings_saved = pyqtSignal()
    def __init__(self, parent=None):
        super().__init__(parent)
        self.lang = ui_language(CONFIG_FILE)
        self.setWindowTitle(tr("Settings - Quick Audio Recorder", self.lang))
        self.setGeometry(100, 100, 540, 640)
        self._saved_device_id = None
        self.device_scanner = DeviceScanner(self)
        self.device_scanner.completed.connect(self.on_devices_loaded)
        self.device_scanner.busy_changed.connect(self.on_scan_busy_changed)

        self.init_ui()
        self.load_settings()

    def init_ui(self):
        layout = QVBoxLayout()
        container = QWidget()
        container.setLayout(layout)
        scroll = QScrollArea(self)
        scroll.setWidgetResizable(True)
        scroll.setWidget(container)
        self.setCentralWidget(scroll)

        # Language
        group_language = QGroupBox(tr("Language:", self.lang).rstrip("：:"))
        layout_language = QVBoxLayout()
        self.combo_language = QComboBox()
        self.combo_language.addItem(tr("Follow system", self.lang), "system")
        self.combo_language.addItem("简体中文", "zh_CN")
        self.combo_language.addItem("English", "en_US")
        layout_language.addWidget(self.combo_language)
        note = QLabel(tr("Language changes take effect after restarting the app.", self.lang))
        note.setWordWrap(True)
        layout_language.addWidget(note)
        group_language.setLayout(layout_language)
        layout.addWidget(group_language)

        # Windows logon startup
        group_startup = QGroupBox(tr("Startup", self.lang))
        layout_startup = QVBoxLayout()
        self.chk_autostart = QCheckBox(tr("Start when I sign in to Windows", self.lang))
        self.chk_autostart.setChecked(is_startup_enabled())
        self.chk_autostart.setEnabled(os.name == "nt")
        layout_startup.addWidget(self.chk_autostart)
        group_startup.setLayout(layout_startup)
        layout.addWidget(group_startup)

        # Microphone
        group_mic = QGroupBox(tr("Input Device", self.lang))
        layout_mic = QVBoxLayout()
        self.combo_mic = QComboBox()
        layout_mic.addWidget(self.combo_mic)
        self.lbl_device_status = QLabel("")
        self.lbl_device_status.setWordWrap(True)
        layout_mic.addWidget(self.lbl_device_status)
        self.btn_refresh = QPushButton(tr("Refresh Devices", self.lang))
        self.btn_refresh.clicked.connect(self.refresh_devices)
        layout_mic.addWidget(self.btn_refresh)
        group_mic.setLayout(layout_mic)
        layout.addWidget(group_mic)

        # Output
        group_out = QGroupBox(tr("Output Configuration", self.lang))
        layout_out = QFormLayout()
        
        layout_folder_inner = QHBoxLayout()
        self.lbl_folder = QLineEdit(os.getcwd())
        self.lbl_folder.setPlaceholderText(tr("Enter or paste an output folder path", self.lang))
        self.lbl_folder.setToolTip(tr("You can paste a folder path without opening the browser.", self.lang))
        btn_browse = QPushButton(tr("Browse...", self.lang))
        btn_browse.clicked.connect(self.browse_folder)
        layout_folder_inner.addWidget(self.lbl_folder)
        layout_folder_inner.addWidget(btn_browse)
        
        self.combo_fmt = QComboBox()
        self.combo_fmt.addItems(["MP3", "WAV"])
        
        layout_out.addRow(tr("Folder:", self.lang), layout_folder_inner)
        layout_out.addRow(tr("Format:", self.lang), self.combo_fmt)
        group_out.setLayout(layout_out)
        layout.addWidget(group_out)

        # Tray Interaction
        group_tray = QGroupBox(tr("Tray Icon Behavior", self.lang))
        layout_tray = QFormLayout()
        self.combo_left_click = QComboBox()
        self.combo_left_click.addItem(tr("Last Used", self.lang), "last_used")
        self.combo_left_click.addItem(tr("Microphone", self.lang), "mic")
        self.combo_left_click.addItem(tr("Loopback", self.lang), "loopback")
        self.combo_left_click.addItem(tr("Both", self.lang), "both")
        layout_tray.addRow(tr("Left Click Action:", self.lang), self.combo_left_click)
        group_tray.setLayout(layout_tray)
        layout.addWidget(group_tray)

        # Post-Processing
        group_post = QGroupBox(tr("Post-Processing & Clipboard", self.lang))
        layout_post = QVBoxLayout()
        self.chk_normalize = QCheckBox(tr("Normalize Audio (Apply first)", self.lang))
        self.chk_clipboard = QCheckBox(tr("Copy File to Clipboard", self.lang))
        self.chk_delete = QCheckBox(tr("Delete after Copy (Move to Temp)", self.lang))
        self.chk_delete.setToolTip(tr("Moves the file to the system temp folder before copying, keeping your output folder clean.", self.lang))
        self.chk_delete.setEnabled(False)
        self.chk_clipboard.toggled.connect(lambda c: self.chk_delete.setEnabled(c))
        
        layout_post.addWidget(self.chk_normalize)
        layout_post.addWidget(self.chk_clipboard)
        layout_post.addWidget(self.chk_delete)
        group_post.setLayout(layout_post)
        layout.addWidget(group_post)

        # Hotkeys
        group_hotkeys = QGroupBox(tr("Global Hotkeys", self.lang))
        layout_hotkeys = QFormLayout()
        self.hk_mic = HotkeyEdit(language=self.lang)
        self.hk_loop = HotkeyEdit(language=self.lang)
        self.hk_both = HotkeyEdit(language=self.lang)
        self.hk_stop = HotkeyEdit(language=self.lang)
        layout_hotkeys.addRow(tr("Record Mic:", self.lang), self.hk_mic)
        layout_hotkeys.addRow(tr("Record Loopback:", self.lang), self.hk_loop)
        layout_hotkeys.addRow(tr("Record Both:", self.lang), self.hk_both)
        layout_hotkeys.addRow(tr("Stop Recording:", self.lang), self.hk_stop)
        group_hotkeys.setLayout(layout_hotkeys)
        layout.addWidget(group_hotkeys)

        btn_save = QPushButton(tr("Save Settings", self.lang))
        btn_save.clicked.connect(self.save_settings)
        layout.addWidget(btn_save)
        self.lbl_save_status = QLabel("")
        self.lbl_save_status.setWordWrap(True)
        layout.addWidget(self.lbl_save_status)

        self.refresh_devices()

    def refresh_devices(self):
        if self.device_scanner.busy:
            self.device_scanner.cancel()
            return
        logging.getLogger(__name__).info("ui.refresh_devices.clicked")
        self.lbl_device_status.setText(tr("Scanning audio devices...", self.lang))
        self.device_scanner.start_scan()

    @pyqtSlot(bool)
    def on_scan_busy_changed(self, busy):
        self.btn_refresh.setText(
            tr("Cancel scan", self.lang) if busy else tr("Refresh Devices", self.lang)
        )

    @pyqtSlot(bool, list, object, str)
    def on_devices_loaded(self, success, mics, default_id, error):
        if not success:
            logging.getLogger(__name__).warning("ui.refresh_devices.failed")
            self.lbl_device_status.setText(tr(error, self.lang))
            return

        selected_id = self.combo_mic.currentData() or self._saved_device_id or default_id
        self.combo_mic.clear()
        for mic in mics:
            self.combo_mic.addItem(mic["name"], mic["id"])
        idx = self.combo_mic.findData(selected_id)
        if idx < 0 and self.combo_mic.count():
            idx = self.combo_mic.findData(default_id)
        if idx >= 0:
            self.combo_mic.setCurrentIndex(idx)
        self._saved_device_id = self.combo_mic.currentData()
        self.lbl_device_status.setText(
            tr("Found {count} audio devices.", self.lang, count=len(mics))
        )

    def browse_folder(self):
        started = time.monotonic()
        logging.getLogger(__name__).info("ui.browse_folder.clicked")
        try:
            folder = QFileDialog.getExistingDirectory(
                self,
                tr("Select Output Folder", self.lang),
                self.lbl_folder.text() or os.path.expanduser("~"),
                options=QFileDialog.Option.DontUseNativeDialog,
            )
            if folder:
                self.lbl_folder.setText(folder)
            logging.getLogger(__name__).info(
                "ui.browse_folder.finished elapsed_s=%.2f selected=%s",
                time.monotonic() - started, bool(folder),
            )
        except Exception:
            logging.getLogger(__name__).exception("ui.browse_folder.failed")
            self.lbl_save_status.setText(
                tr("Cannot open folder browser. Paste the folder path directly.", self.lang)
            )

    def load_settings(self):
        if os.path.exists(CONFIG_FILE):
            try:
                data = read_settings(CONFIG_FILE)
                    
                language_idx = self.combo_language.findData(data.get("language", "system"))
                if language_idx >= 0:
                    self.combo_language.setCurrentIndex(language_idx)

                self.lbl_folder.setText(data.get("output_folder", os.getcwd()))
                fmt_idx = self.combo_fmt.findText(data.get("format", "MP3"))
                if fmt_idx >= 0: self.combo_fmt.setCurrentIndex(fmt_idx)
                
                saved_id = data.get("device_id")
                self._saved_device_id = saved_id
                if saved_id:
                    idx = self.combo_mic.findData(saved_id)
                    if idx >= 0: self.combo_mic.setCurrentIndex(idx)

                mode = normalize_tray_mode(data.get("tray_click_mode", "last_used"))
                mode_idx = self.combo_left_click.findData(mode)
                if mode_idx >= 0: self.combo_left_click.setCurrentIndex(mode_idx)

                self.chk_normalize.setChecked(data.get("normalize", False))
                self.chk_clipboard.setChecked(data.get("clipboard", False))
                self.chk_delete.setChecked(data.get("delete_after", False))
                self.chk_delete.setEnabled(self.chk_clipboard.isChecked())

                self.hk_mic.setText(data.get("hk_mic", ""))
                self.hk_loop.setText(data.get("hk_loop", ""))
                self.hk_both.setText(data.get("hk_both", ""))
                self.hk_stop.setText(data.get("hk_stop", ""))
            except Exception as e:
                print(f"Error loading settings: {e}")

    def save_settings(self):
        data = self.get_settings()
        try:
            write_settings(CONFIG_FILE, data)
        except Exception as exc:
            QMessageBox.critical(
                self, tr("Error", self.lang),
                tr("Failed to save settings: {error}", self.lang, error=exc),
            )
            return

        try:
            if self.chk_autostart.isChecked() != is_startup_enabled():
                set_startup_enabled(self.chk_autostart.isChecked())
            self.lbl_save_status.setText(tr("Settings saved successfully.", self.lang))
        except OSError as exc:
            self.chk_autostart.setChecked(is_startup_enabled())
            self.lbl_save_status.setText(
                tr("Settings saved, but Windows startup could not be updated: {error}",
                   self.lang, error=exc)
            )
        # Do not show a modal success dialog or synchronously install Windows hooks.
        self.settings_saved.emit()

    def get_settings(self):
        return {
            "device_id": self.combo_mic.currentData(),
            "output_folder": self.lbl_folder.text(),
            "format": self.combo_fmt.currentText(),
            "tray_click_mode": self.combo_left_click.currentData(),
            "language": self.combo_language.currentData(),
            "normalize": self.chk_normalize.isChecked(),
            "clipboard": self.chk_clipboard.isChecked(),
            "delete_after": self.chk_delete.isChecked(),
            "hk_mic": self.hk_mic.text(),
            "hk_loop": self.hk_loop.text(),
            "hk_both": self.hk_both.text(),
            "hk_stop": self.hk_stop.text()
        }

class TrayApplication(QObject):
    def __init__(self, app):
        super().__init__()
        self.app = app
        self.lang = ui_language(CONFIG_FILE)
        self.recorder = None
        self.last_mode = "mic" 
        
        self.signals = SignalManager()
        self.signals.recording_finished.connect(self.on_recording_finished)
        self.signals.hotkey_requested.connect(self.on_hotkey_requested)
        self.signals.hotkey_error.connect(self.on_hotkey_error)
        self.hotkey_manager = HotkeyManager(
            self.signals.hotkey_requested.emit,
            self.signals.hotkey_error.emit,
        )

        self.icon_idle_path = resource_path("icon_idle.png")
        self.icon_rec_path = resource_path("icon_rec.png")
        self.generate_icons()
            
        self.tray_icon = QSystemTrayIcon(QIcon(self.icon_idle_path), self.app)
        self.tray_icon.setToolTip(tr("Quick Audio Recorder (Idle)", self.lang))
        self.tray_icon.activated.connect(self.on_tray_activated)
        
        self.build_menu()
        self.tray_icon.show()
        
        self.settings_window = SettingsWindow()
        self.settings_window.settings_saved.connect(self.register_hotkeys)
        
        self.tray_icon.showMessage(tr("Ready", self.lang), tr("Left-click to toggle recording.", self.lang), QSystemTrayIcon.MessageIcon.Information, 2000)
        self.register_hotkeys()

    def display_mode(self, mode):
        return tr({"mic": "Microphone", "loopback": "Loopback", "both": "Both"}.get(mode, mode), self.lang)

    def generate_icons(self):
        if not os.path.exists(self.icon_idle_path):
            pix = QPixmap(64, 64)
            pix.fill(Qt.GlobalColor.transparent)
            painter = QPainter(pix)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setBrush(QBrush(QColor(80, 80, 80)))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.drawEllipse(4, 4, 56, 56)
            painter.end()
            pix.save(self.icon_idle_path)

        if not os.path.exists(self.icon_rec_path):
            pix = QPixmap(64, 64)
            pix.fill(Qt.GlobalColor.transparent)
            painter = QPainter(pix)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setBrush(QBrush(QColor(220, 0, 0)))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.drawEllipse(4, 4, 56, 56)
            painter.setBrush(QBrush(QColor(255, 255, 255)))
            painter.drawEllipse(22, 22, 20, 20)
            painter.end()
            pix.save(self.icon_rec_path)

    def build_menu(self):
        self.menu = QMenu()
        self.action_record_mic = QAction(tr("Start Recording (Mic)", self.lang), self)
        self.action_record_mic.triggered.connect(lambda: self.start_recording("mic"))
        self.action_record_loop = QAction(tr("Start Recording (Loopback)", self.lang), self)
        self.action_record_loop.triggered.connect(lambda: self.start_recording("loopback"))
        self.action_record_both = QAction(tr("Start Recording (Both)", self.lang), self)
        self.action_record_both.triggered.connect(lambda: self.start_recording("both"))
        self.action_stop = QAction(tr("Stop Recording", self.lang), self)
        self.action_stop.triggered.connect(self.stop_recording)
        self.action_stop.setEnabled(False)
        self.action_settings = QAction(tr("Settings", self.lang), self)
        self.action_settings.triggered.connect(self.open_settings)
        self.action_exit = QAction(tr("Exit", self.lang), self)
        self.action_exit.triggered.connect(self.exit_app)
        
        self.menu.addAction(self.action_record_mic)
        self.menu.addAction(self.action_record_loop)
        self.menu.addAction(self.action_record_both)
        self.menu.addAction(self.action_stop)
        self.menu.addSeparator()
        self.menu.addAction(self.action_settings)
        self.menu.addAction(self.action_exit)
        self.tray_icon.setContextMenu(self.menu)

    def register_hotkeys(self):
        settings = self.settings_window.get_settings()
        self.hotkey_manager.update({
            "mic": settings.get("hk_mic"),
            "loopback": settings.get("hk_loop"),
            "both": settings.get("hk_both"),
            "stop": settings.get("hk_stop"),
        })

    @pyqtSlot(str)
    def on_hotkey_requested(self, action):
        # The keyboard library invokes callbacks on its worker threads.
        # Queued Qt signals move the actual recording/UI operation to the GUI thread.
        if action == "stop":
            self.stop_recording()
        elif action in ("mic", "loopback", "both"):
            self.start_recording(action)

    @pyqtSlot(str, object)
    def on_hotkey_error(self, message, parameters):
        self.tray_icon.showMessage(
            tr("Hotkey error", self.lang), tr(message, self.lang, **parameters),
            QSystemTrayIcon.MessageIcon.Warning, 4000,
        )

    def on_tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            if self.recorder and self.recorder.is_alive():
                self.stop_recording()
            else:
                settings = self.settings_window.get_settings()
                click_mode = normalize_tray_mode(settings.get("tray_click_mode", "last_used"))
                target_mode = self.last_mode
                if click_mode == "mic": target_mode = "mic"
                elif click_mode == "loopback": target_mode = "loopback"
                elif click_mode == "both": target_mode = "both"
                self.start_recording(target_mode)

    def open_settings(self):
        self.settings_window.show()
        self.settings_window.raise_()
        self.settings_window.activateWindow()

    def start_recording(self, mode="mic"):
        if self.recorder and self.recorder.is_alive(): return
        self.last_mode = mode    
        settings = self.settings_window.get_settings()
        target_id = settings['device_id']
        
        def finish_callback(path, error):
            self.signals.recording_finished.emit(path if path else "", error if error else "")

        self.recorder = AudioRecorder(
            mic_id=target_id,
            source_mode=mode,
            output_folder=settings['output_folder'],
            output_format=settings['format'],
            normalize=settings['normalize'],
            on_finish_callback=finish_callback
        )
        self.recorder.start()
        self.action_record_mic.setEnabled(False)
        self.action_record_loop.setEnabled(False)
        self.action_record_both.setEnabled(False)
        self.action_stop.setEnabled(True)
        self.tray_icon.setIcon(QIcon(self.icon_rec_path)) 
        self.tray_icon.setToolTip(tr("Recording ({mode})...", self.lang, mode=self.display_mode(mode)))
        self.tray_icon.showMessage(tr("Started", self.lang), tr("Recording {mode}", self.lang, mode=self.display_mode(mode)), QSystemTrayIcon.MessageIcon.NoIcon, 1000)

    def stop_recording(self):
        if self.recorder: self.recorder.stop()

    def on_recording_finished(self, path, error):
        self.action_record_mic.setEnabled(True)
        self.action_record_loop.setEnabled(True)
        self.action_record_both.setEnabled(True)
        self.action_stop.setEnabled(False)
        self.tray_icon.setIcon(QIcon(self.icon_idle_path))
        self.tray_icon.setToolTip(tr("Quick Audio Recorder (Idle)", self.lang))
        self.recorder = None
        
        if error:
            self.tray_icon.showMessage(tr("Error", self.lang), tr("Recording failed: {error}", self.lang, error=tr(error, self.lang)), QSystemTrayIcon.MessageIcon.Critical, 4000)
            return
            
        settings = self.settings_window.get_settings()
        final_path = path
        msg = tr("Saved to {filename}", self.lang, filename=os.path.basename(path))
        
        if settings['clipboard'] and os.path.exists(path):
            try:
                if settings['delete_after']:
                    temp_dir = tempfile.gettempdir()
                    new_path = os.path.join(temp_dir, os.path.basename(path))
                    if os.path.exists(new_path):
                        base, ext = os.path.splitext(new_path)
                        import time
                        new_path = f"{base}_{int(time.time())}{ext}"
                    shutil.move(path, new_path)
                    final_path = new_path
                    msg = tr("Moved to Temp & Copied to Clipboard.", self.lang)
                else:
                    msg += "\n" + tr("Copied to clipboard.", self.lang)

                # Use Robust Clipboard Utility
                success, status = copy_file_to_clipboard(final_path)
                if not success:
                    msg += "\n" + tr("Clipboard Error: {error}", self.lang, error=status)
                else:
                    # Optional: Log success?
                    pass
                
            except Exception as e:
                msg += "\n" + tr("Clipboard/Move error: {error}", self.lang, error=e)

        self.tray_icon.showMessage(tr("Finished", self.lang), msg, QSystemTrayIcon.MessageIcon.Information, 2000)

    def exit_app(self):
        self.hotkey_manager.close()
        if self.recorder: self.recorder.stop()
        self.app.quit()
