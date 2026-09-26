# -*- coding: utf-8 -*-
"""
零零快捷剪貼板 — Windows 專用快捷剪貼板管理工具
單檔案 Python 應用程式  v1.0.0
"""

import json
import os
import sys
import threading
import time
import ctypes
import ctypes.wintypes
import tkinter as tk
import winreg
from pathlib import Path

import customtkinter as ctk
import pyperclip
import keyboard

# ─────────────────── 單實例鎖 (Mutex) ───────────────────

def acquire_single_instance_lock():
    """用 Win32 Named Mutex 確保只有一個實例在執行。回傳 handle 或 None。"""
    MUTEX_NAME = "Global\\ZeroZeroClipboard_SingleInstance"
    ERROR_ALREADY_EXISTS = 183
    handle = ctypes.windll.kernel32.CreateMutexW(None, False, MUTEX_NAME)
    if ctypes.windll.kernel32.GetLastError() == ERROR_ALREADY_EXISTS:
        ctypes.windll.kernel32.CloseHandle(handle)
        return None
    return handle

# ─────────────────────────── 常數與路徑 ───────────────────────────

APP_NAME = "零零快捷剪貼板"
APP_VERSION = "1.0.1"
GITHUB_REPO = "morrisxlee/TseroShortClipBoard"
GITHUB_API_LATEST = f"https://api.github.com/repos/{GITHUB_REPO}/releases/latest"

# (#2) 資料存放在 %APPDATA% — 重裝軟體後資料仍然保留
_APPDATA_DIR = Path(os.environ.get("APPDATA", os.path.expanduser("~"))) / APP_NAME
_APPDATA_DIR.mkdir(parents=True, exist_ok=True)
_EXE_DIR = Path(os.path.dirname(os.path.abspath(
    sys.executable if getattr(sys, 'frozen', False) else __file__)))

DATA_FILE = _APPDATA_DIR / "clipboard_data.json"
SETTINGS_FILE = _APPDATA_DIR / "settings.json"

def _migrate_old_data():
    """將舊位置 (exe 旁邊) 的資料遷移到 %APPDATA%"""
    for fname in ("clipboard_data.json", "settings.json"):
        old = _EXE_DIR / fname
        new = _APPDATA_DIR / fname
        if old.exists() and not new.exists():
            try:
                import shutil
                shutil.copy2(str(old), str(new))
            except Exception:
                pass

_migrate_old_data()

DEFAULT_CATEGORIES = ["All", "Copied", "自訂", "聊天", "上班族", "電商", "嘴砲"]
COPIED_MAX = 50

# (#7) 預設寬度 > 高度
DEFAULT_SETTINGS = {
    "hotkey": "right alt",
    "size": "580 x 440",
    "theme": "dark",
    "autostart": False,
}

# (#5) 快捷鍵選項列表
HOTKEY_OPTIONS = [
    "right alt",
    "right ctrl",
    "right shift",
    "f1", "f2", "f3", "f4",
    "f5", "f6", "f7", "f8",
    "f9", "f10", "f11", "f12",
    "scroll lock", "pause",
    "insert", "home",
]

# (#6) 視窗尺寸預設選項 (寬 x 高，寬 > 高)
SIZE_OPTIONS = [
    "480 x 360",
    "520 x 400",
    "580 x 440",
    "640 x 480",
    "720 x 520",
    "800 x 560",
    "900 x 600",
]

# (#4) 常用 Emoji 選擇列表
EMOJI_LIST = [
    "📋", "📝", "📌", "📎", "✏️", "🖊️", "📑", "📄",
    "👋", "😊", "😎", "🤝", "💬", "🗣️", "💡", "🔥",
    "⭐", "🎯", "✅", "❌", "⚠️", "❓", "💰", "🎉",
    "📧", "📞", "📱", "💻", "🖥️", "⌨️", "🔒", "🔑",
    "🛒", "📦", "🚚", "💳", "🏷️", "🎁", "🏪", "🛍️",
    "👍", "👎", "👏", "🙏", "💪", "🤔", "😂", "🤣",
    "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍",
    "🏠", "🏢", "📊", "📈", "📅", "⏰", "🔔", "🔧",
]

DEFAULT_SNIPPETS = [
    {"emoji": "👋", "title": "打招呼", "category": "聊天", "content": "嗨！你好嗎？最近過得怎麼樣？有空一起出來聊聊天吧～"},
    {"emoji": "📧", "title": "Email 開頭", "category": "上班族", "content": "您好，\n\n感謝您的來信，以下是我的回覆：\n\n"},
    {"emoji": "🛒", "title": "商品描述模板", "category": "電商", "content": "【商品名稱】\n【規格】\n【價格】\n【運費】\n【付款方式】\n【出貨時間】"},
    {"emoji": "🔥", "title": "嘴炮王", "category": "嘴炮", "content": "你說得對，但是你有沒有想過這個問題的另一面？讓我來給你分析分析..."},
    {"emoji": "📋", "title": "會議紀錄", "category": "上班族", "content": "會議日期：\n與會人員：\n討論事項：\n1. \n2. \n決議事項：\n"},
    {"emoji": "💬", "title": "感謝回覆", "category": "聊天", "content": "非常感謝你的回覆！你的建議對我很有幫助，我會好好考慮的 😊"},
    {"emoji": "📦", "title": "出貨通知", "category": "電商", "content": "親愛的顧客您好，您的訂單已出貨！\n物流編號：\n預計到貨時間：\n如有問題請隨時聯繫我們 🙏"},
    {"emoji": "😎", "title": "自我介紹", "category": "自訂", "content": "大家好！我是___，很高興認識大家。我的興趣是___，希望能和大家多多交流！"},
]

# ─────────────────── 色彩主題 (#8) ───────────────────

THEMES = {
    "dark": {
        "bg": "#12122a", "bar": "#1a1a3e", "card": "#1a1a3e",
        "card_hover": "#2a2a5a", "input_bg": "#1e1e3e",
        "border": "#3a3a6a", "cat_bg": "#2a2a4a", "cat_active": "#5c6bc0",
        "text": "#e0e0e0", "text2": "#ccc", "text3": "#aaa",
        "text_dim": "#777", "accent": "#5c6bc0",
        "green": "#00c853", "green_hover": "#00e676",
        "red": "#c62828", "red_hover": "#e53935",
        "scroll_btn": "#3a3a6a", "scroll_hover": "#5c6bc0",
    },
    "light": {
        "bg": "#f0f0f5", "bar": "#dddde8", "card": "#ffffff",
        "card_hover": "#e8e8f0", "input_bg": "#ffffff",
        "border": "#c0c0d0", "cat_bg": "#d0d0e0", "cat_active": "#5c6bc0",
        "text": "#1a1a2e", "text2": "#333", "text3": "#666",
        "text_dim": "#999", "accent": "#5c6bc0",
        "green": "#00a844", "green_hover": "#00c853",
        "red": "#d32f2f", "red_hover": "#e53935",
        "scroll_btn": "#c0c0d0", "scroll_hover": "#5c6bc0",
    },
}

def get_theme(dm):
    return THEMES.get(dm.settings.get("theme", "dark"), THEMES["dark"])

# ─────────────────────────── Win32 helpers ───────────────────────────

def enable_blur(hwnd):
    """為視窗啟用 Windows 10/11 壓克力/毛玻璃效果"""
    try:
        class ACCENT_POLICY(ctypes.Structure):
            _fields_ = [
                ("AccentState", ctypes.c_int),
                ("AccentFlags", ctypes.c_int),
                ("GradientColor", ctypes.c_uint),
                ("AnimationId", ctypes.c_int),
            ]
        class WINDOWCOMPOSITIONATTRIBDATA(ctypes.Structure):
            _fields_ = [
                ("Attribute", ctypes.c_int),
                ("Data", ctypes.POINTER(ACCENT_POLICY)),
                ("SizeOfData", ctypes.c_size_t),
            ]
        accent = ACCENT_POLICY()
        accent.AccentState = 3  # ACCENT_ENABLE_BLURBEHIND
        accent.GradientColor = 0x99000000
        accent.AccentFlags = 2
        data = WINDOWCOMPOSITIONATTRIBDATA()
        data.Attribute = 19  # WCA_ACCENT_POLICY
        data.SizeOfData = ctypes.sizeof(accent)
        data.Data = ctypes.pointer(accent)
        ctypes.windll.user32.SetWindowCompositionAttribute(hwnd, ctypes.byref(data))
    except Exception:
        pass

# (#2) 取得滑鼠所在螢幕的工作區域
def get_cursor_monitor_rect():
    """Return (x, y, w, h) of the monitor the cursor is currently on."""
    try:
        class POINT(ctypes.Structure):
            _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]
        pt = POINT()
        ctypes.windll.user32.GetCursorPos(ctypes.byref(pt))
        MONITOR_DEFAULTTONEAREST = 2
        hmon = ctypes.windll.user32.MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST)

        class RECT(ctypes.Structure):
            _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                        ("right", ctypes.c_long), ("bottom", ctypes.c_long)]
        class MONITORINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_uint), ("rcMonitor", RECT),
                        ("rcWork", RECT), ("dwFlags", ctypes.c_uint)]
        mi = MONITORINFO()
        mi.cbSize = ctypes.sizeof(MONITORINFO)
        ctypes.windll.user32.GetMonitorInfoW(hmon, ctypes.byref(mi))
        rc = mi.rcWork
        return rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top
    except Exception:
        return 0, 0, 1920, 1080

def parse_size(size_str):
    """Parse '580 x 440' → (580, 440)"""
    try:
        parts = size_str.split("x")
        return int(parts[0].strip()), int(parts[1].strip())
    except Exception:
        return 580, 440

# (#8) 圓角視窗
def set_rounded_corners(hwnd):
    """為視窗設定圓角 (Windows 11+ DWM, Win10 fallback)"""
    try:
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
        value = ctypes.c_int(2)  # DWMWCP_ROUND
        ctypes.windll.dwmapi.DwmSetWindowAttribute(
            hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
            ctypes.byref(value), ctypes.sizeof(value))
    except Exception:
        try:
            class RECT(ctypes.Structure):
                _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                            ("right", ctypes.c_long), ("bottom", ctypes.c_long)]
            rc = RECT()
            ctypes.windll.user32.GetClientRect(hwnd, ctypes.byref(rc))
            rgn = ctypes.windll.gdi32.CreateRoundRectRgn(
                0, 0, rc.right, rc.bottom, 18, 18)
            ctypes.windll.user32.SetWindowRgn(hwnd, rgn, True)
        except Exception:
            pass

# (#9) 開機自動啟動
_AUTOSTART_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
_AUTOSTART_NAME = APP_NAME

def set_autostart(enable):
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, _AUTOSTART_KEY,
                             0, winreg.KEY_SET_VALUE)
        if enable:
            if getattr(sys, 'frozen', False):
                exe = f'"{ sys.executable }"'
            else:
                exe = f'"{ sys.executable }" "{ os.path.abspath(__file__) }"'
            winreg.SetValueEx(key, _AUTOSTART_NAME, 0, winreg.REG_SZ, exe)
        else:
            try:
                winreg.DeleteValue(key, _AUTOSTART_NAME)
            except FileNotFoundError:
                pass
        winreg.CloseKey(key)
    except Exception:
        pass

def get_autostart():
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, _AUTOSTART_KEY,
                             0, winreg.KEY_READ)
        winreg.QueryValueEx(key, _AUTOSTART_NAME)
        winreg.CloseKey(key)
        return True
    except Exception:
        return False

# ─────────────────────────── 資料管理 ───────────────────────────

class DataManager:
    def __init__(self):
        self.snippets = []
        self.categories = list(DEFAULT_CATEGORIES)
        self.settings = dict(DEFAULT_SETTINGS)
        self.load_settings()
        self.load_data()

    def load_data(self):
        if DATA_FILE.exists():
            try:
                with open(DATA_FILE, "r", encoding="utf-8") as f:
                    obj = json.load(f)
                self.snippets = obj.get("snippets", [])
                saved_cats = obj.get("categories", [])
                if saved_cats:
                    # 以存檔的分類為主，只確保 All 和 Copied 存在
                    merged = []
                    if "All" not in saved_cats:
                        merged.append("All")
                    for c in saved_cats:
                        if c not in merged:
                            merged.append(c)
                    # 確保 Copied 始終在第二位
                    if "Copied" not in merged:
                        merged.insert(1, "Copied")
                    elif merged.index("Copied") != 1:
                        merged.remove("Copied")
                        merged.insert(1, "Copied")
                    self.categories = merged
            except Exception:
                self.snippets = list(DEFAULT_SNIPPETS)
                self.save_data()
        else:
            self.snippets = list(DEFAULT_SNIPPETS)
            self.save_data()

    def save_data(self):
        try:
            with open(DATA_FILE, "w", encoding="utf-8") as f:
                json.dump({"snippets": self.snippets, "categories": self.categories},
                          f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    def load_settings(self):
        if SETTINGS_FILE.exists():
            try:
                with open(SETTINGS_FILE, "r", encoding="utf-8") as f:
                    self.settings.update(json.load(f))
            except Exception:
                pass

    def save_settings(self):
        with open(SETTINGS_FILE, "w", encoding="utf-8") as f:
            json.dump(self.settings, f, ensure_ascii=False, indent=2)

    def add_snippet(self, emoji, title, category, content):
        self.snippets.insert(0, {"emoji": emoji, "title": title,
                                  "category": category, "content": content})
        self.save_data()

    def update_snippet(self, idx, emoji, title, category, content):
        if 0 <= idx < len(self.snippets):
            self.snippets[idx] = {"emoji": emoji, "title": title,
                                  "category": category, "content": content}
            self.save_data()

    def swap_snippets(self, idx_a, idx_b):
        """交換兩個片段的位置"""
        n = len(self.snippets)
        if 0 <= idx_a < n and 0 <= idx_b < n and idx_a != idx_b:
            self.snippets[idx_a], self.snippets[idx_b] = \
                self.snippets[idx_b], self.snippets[idx_a]
            self.save_data()

    def delete_snippet(self, idx):
        if 0 <= idx < len(self.snippets):
            self.snippets.pop(idx)
            self.save_data()

    def add_category(self, name):
        if name and name not in self.categories:
            self.categories.append(name)
            self.save_data()

    def add_clipboard_entry(self, content):
        """自動記錄剪貼簿內容到 Copied 分類（最多 COPIED_MAX 筆）"""
        content = content.strip()
        if not content:
            return
        # 避免重複：如果最新一筆內容相同就跳過
        copied = [s for s in self.snippets if s.get("category") == "Copied"]
        if copied and copied[0].get("content") == content:
            return
        preview = content.replace("\n", " ")[:30]
        entry = {"emoji": "📋", "title": preview,
                 "category": "Copied", "content": content}
        self.snippets.insert(0, entry)
        # 限制 Copied 數量
        copied_indices = [i for i, s in enumerate(self.snippets)
                          if s.get("category") == "Copied"]
        while len(copied_indices) > COPIED_MAX:
            self.snippets.pop(copied_indices[-1])
            copied_indices.pop()
        self.save_data()

    def rename_category(self, old_name, new_name):
        """重新命名分類，同時更新所有片段的分類欄位"""
        new_name = new_name.strip()
        if not new_name or old_name in ("All", "Copied") or old_name == new_name:
            return False
        if new_name in self.categories:
            return False
        idx = self.categories.index(old_name) if old_name in self.categories else -1
        if idx < 0:
            return False
        self.categories[idx] = new_name
        for s in self.snippets:
            if s.get("category") == old_name:
                s["category"] = new_name
        self.save_data()
        return True

    def move_category(self, name, direction):
        """移動分類順序 (direction: -1=上, +1=下)，All 和 Copied 固定不動"""
        if name in ("All", "Copied") or name not in self.categories:
            return
        idx = self.categories.index(name)
        new_idx = idx + direction
        # 不可移到 All(0) 或 Copied(1) 的位置
        if new_idx < 2 or new_idx >= len(self.categories):
            return
        self.categories[idx], self.categories[new_idx] = \
            self.categories[new_idx], self.categories[idx]
        self.save_data()

    def delete_category(self, name):
        """刪除分類（不可刪除 'All' 和 'Copied'），同時將該分類下的片段歸入 '自訂'"""
        if name in ("All", "Copied") or name not in self.categories:
            return
        self.categories.remove(name)
        for s in self.snippets:
            if s.get("category") == name:
                s["category"] = "自訂"
        self.save_data()

# ─────────────────────────── Toast 通知 ───────────────────────────

class Toast(ctk.CTkToplevel):
    def __init__(self, parent, message="已複製！", duration=1000):
        super().__init__(parent)
        self.overrideredirect(True)
        self.attributes("-topmost", True)
        self.attributes("-alpha", 0.0)
        self.configure(fg_color="#1a1a2e")
        lbl = ctk.CTkLabel(self, text=f"  ✅  {message}  ",
                           font=ctk.CTkFont(size=16, weight="bold"),
                           text_color="#00e676", fg_color="#1a1a2e",
                           corner_radius=12, padx=20, pady=10)
        lbl.pack(padx=4, pady=4)
        self.update_idletasks()
        px = parent.winfo_rootx() + (parent.winfo_width() - self.winfo_width()) // 2
        py = parent.winfo_rooty() + parent.winfo_height() - 80
        self.geometry(f"+{px}+{py}")
        self._alpha = 0.0
        self._duration = duration
        self._fade_in()

    def _fade_in(self):
        if self._alpha < 0.95:
            self._alpha += 0.15
            self.attributes("-alpha", self._alpha)
            self.after(18, self._fade_in)
        else:
            self.after(self._duration, self._fade_out)

    def _fade_out(self):
        if self._alpha > 0.05:
            self._alpha -= 0.12
            self.attributes("-alpha", self._alpha)
            self.after(18, self._fade_out)
        else:
            self.destroy()

# ──────────────────── Emoji 選擇器 (#4) ────────────────────

class EmojiPicker(ctk.CTkToplevel):
    def __init__(self, parent, on_pick):
        super().__init__(parent)
        self.overrideredirect(True)
        self.attributes("-topmost", True)
        self.configure(fg_color="#1e1e3e")
        self.on_pick = on_pick

        # 使用原生 tkinter Frame + Label 來正確渲染彩色 emoji
        inner = tk.Frame(self, bg="#1e1e3e")
        inner.pack(fill="both", expand=True, padx=4, pady=4)

        cols = 8
        for i, em in enumerate(EMOJI_LIST):
            r, c = divmod(i, cols)
            lbl = tk.Label(inner, text=em, font=("Segoe UI Emoji", 18),
                           bg="#1e1e3e", fg="white", cursor="hand2",
                           width=2, height=1, relief="flat")
            lbl.grid(row=r, column=c, padx=1, pady=1)
            lbl.bind("<Enter>", lambda e, w=lbl: w.configure(bg="#3a3a6a"))
            lbl.bind("<Leave>", lambda e, w=lbl: w.configure(bg="#1e1e3e"))
            lbl.bind("<Button-1>", lambda e, em=em: self._pick(em))

        self.update_idletasks()
        pw, ph = self.winfo_reqwidth(), self.winfo_reqheight()
        mx, my, mw, mh = get_cursor_monitor_rect()
        px = mx + (mw - pw) // 2
        py = my + (mh - ph) // 2
        self.geometry(f"+{px}+{py}")
        self.bind("<FocusOut>", lambda _: self.destroy())
        self.focus_force()

    def _pick(self, emoji):
        self.on_pick(emoji)
        self.destroy()

# ─────────────────────── 新增 / 編輯視窗 ───────────────────────

class SnippetEditor(ctk.CTkToplevel):
    def __init__(self, parent, dm: DataManager, on_save_cb, edit_idx=None):
        super().__init__(parent)
        self.dm = dm
        self.on_save_cb = on_save_cb
        # 儲存對物件的引用，而不是索引，避免剪貼簿監控插入導致索引失效
        self._edit_snippet_ref = dm.snippets[edit_idx] if edit_idx is not None else None
        self.edit_idx = edit_idx
        th = get_theme(dm)

        self.title("編輯片段" if edit_idx is not None else "新增片段")
        # (#3) 定位到游標所在螢幕中央
        ew, eh = 440, 520
        mx, my, mw, mh = get_cursor_monitor_rect()
        ex = mx + (mw - ew) // 2
        ey = my + (mh - eh) // 2
        self.geometry(f"{ew}x{eh}+{ex}+{ey}")
        self.resizable(False, False)
        self.attributes("-topmost", True)
        self.configure(fg_color=th["bg"])
        self.grab_set()

        pad = {"padx": 20, "pady": (10, 0)}

        # ── Emoji + 標題 ──
        ctk.CTkLabel(self, text="Emoji + 標題", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        frm_title = ctk.CTkFrame(self, fg_color="transparent")
        frm_title.pack(fill="x", padx=20, pady=(4, 0))

        self.emoji_var = ctk.StringVar(value="📋")
        # (#4) Emoji 按鈕 — 點擊彈出選擇器
        self.emoji_btn = ctk.CTkButton(frm_title, width=52, height=38,
                                       textvariable=self.emoji_var,
                                       font=ctk.CTkFont(size=22),
                                       fg_color=th["card"], hover_color=th["card_hover"],
                                       corner_radius=8,
                                       command=self._open_emoji_picker)
        self.emoji_btn.pack(side="left", padx=(0, 8))

        self.title_var = ctk.StringVar()
        self.title_entry = ctk.CTkEntry(frm_title, textvariable=self.title_var,
                                        placeholder_text="輸入標題...",
                                        font=ctk.CTkFont(size=14),
                                        fg_color=th["input_bg"],
                                        text_color=th["text"])
        self.title_entry.pack(side="left", fill="x", expand=True)

        # ── 分類 ──
        ctk.CTkLabel(self, text="分類", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        cat_frame = ctk.CTkFrame(self, fg_color="transparent")
        cat_frame.pack(fill="x", padx=20, pady=(4, 0))

        cats = [c for c in dm.categories if c != "All"]
        self.cat_var = ctk.StringVar(value=cats[0] if cats else "自訂")
        self.cat_menu = ctk.CTkOptionMenu(cat_frame, variable=self.cat_var,
                                          values=cats, width=200,
                                          fg_color=th["cat_bg"],
                                          button_color=th["accent"])
        self.cat_menu.pack(side="left")

        self.new_cat_entry = ctk.CTkEntry(cat_frame, placeholder_text="新分類...",
                                          width=120, font=ctk.CTkFont(size=13),
                                          fg_color=th["input_bg"],
                                          text_color=th["text"])
        self.new_cat_entry.pack(side="left", padx=(8, 0))
        ctk.CTkButton(cat_frame, text="+", width=32, height=32,
                      fg_color=th["green"], hover_color=th["green_hover"],
                      command=self._add_new_cat).pack(side="left", padx=(4, 0))

        # ── 內容 ──
        ctk.CTkLabel(self, text="內容", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        self.content_box = ctk.CTkTextbox(self, height=220, font=ctk.CTkFont(size=13),
                                          fg_color=th["input_bg"], corner_radius=8,
                                          text_color=th["text"])
        self.content_box.pack(fill="both", expand=True, padx=20, pady=(4, 10))

        # ── 按鈕列 ──
        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=20, pady=(0, 16))

        if edit_idx is not None:
            ctk.CTkButton(btn_frame, text="🗑  刪除", width=80,
                          fg_color=th["red"], hover_color=th["red_hover"],
                          command=self._delete).pack(side="left")

        ctk.CTkButton(btn_frame, text="💾  儲存", width=120,
                      fg_color=th["green"], hover_color=th["green_hover"],
                      font=ctk.CTkFont(size=14, weight="bold"),
                      command=self._save).pack(side="right")

        # 填入既有資料
        if edit_idx is not None:
            s = dm.snippets[edit_idx]
            self.emoji_var.set(s.get("emoji", "📋"))
            self.title_var.set(s.get("title", ""))
            self.cat_var.set(s.get("category", "自訂"))
            self.content_box.insert("1.0", s.get("content", ""))

    def _open_emoji_picker(self):
        EmojiPicker(self, self._on_emoji_picked)

    def _on_emoji_picked(self, emoji):
        self.emoji_var.set(emoji)

    def _add_new_cat(self):
        name = self.new_cat_entry.get().strip()
        if name:
            self.dm.add_category(name)
            cats = [c for c in self.dm.categories if c != "All"]
            self.cat_menu.configure(values=cats)
            self.cat_var.set(name)
            self.new_cat_entry.delete(0, "end")

    def _resolve_idx(self):
        """通過物件引用找到當前實際索引（使用 is 身份比對）"""
        if self._edit_snippet_ref is None:
            return None
        for i, s in enumerate(self.dm.snippets):
            if s is self._edit_snippet_ref:
                return i
        return None

    def _save(self):
        emoji = self.emoji_var.get().strip() or "📋"
        title = self.title_var.get().strip() or "未命名"
        cat = self.cat_var.get()
        content = self.content_box.get("1.0", "end-1c")
        real_idx = self._resolve_idx()
        if real_idx is not None:
            self.dm.update_snippet(real_idx, emoji, title, cat, content)
        else:
            self.dm.add_snippet(emoji, title, cat, content)
        self.on_save_cb()
        self.destroy()

    def _delete(self):
        real_idx = self._resolve_idx()
        if real_idx is not None:
            self.dm.delete_snippet(real_idx)
            self.on_save_cb()
            self.destroy()

# ─────────────────────── 設定視窗 (#5 #6 #8) ───────────────────────

class SettingsWindow(ctk.CTkToplevel):
    def __init__(self, parent, dm: DataManager, on_save_cb):
        super().__init__(parent)
        self.dm = dm
        self.on_save_cb = on_save_cb
        th = get_theme(dm)

        self.title("⚙  設定")
        sw, sh = 420, 620
        mx, my, mw, mh = get_cursor_monitor_rect()
        sx = mx + (mw - sw) // 2
        sy = my + (mh - sh) // 2
        self.geometry(f"{sw}x{sh}+{sx}+{sy}")
        self.resizable(False, False)
        self.attributes("-topmost", True)
        self.configure(fg_color=th["bg"])
        self.grab_set()

        pad = {"padx": 24, "pady": (14, 0)}

        # (#5) 快捷鍵 — 下拉選單
        ctk.CTkLabel(self, text="快捷鍵", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        cur_hk = dm.settings.get("hotkey", "right alt")
        self.hotkey_var = ctk.StringVar(value=cur_hk)
        ctk.CTkOptionMenu(self, variable=self.hotkey_var,
                          values=HOTKEY_OPTIONS,
                          fg_color=th["cat_bg"], button_color=th["accent"],
                          text_color=th["text"],
                          ).pack(fill="x", padx=24, pady=(4, 0))

        # (#6) 視窗尺寸 — 下拉選單
        ctk.CTkLabel(self, text="視窗尺寸 (寬 x 高)", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        cur_size = dm.settings.get("size", "580 x 440")
        self.size_var = ctk.StringVar(value=cur_size)
        ctk.CTkOptionMenu(self, variable=self.size_var,
                          values=SIZE_OPTIONS,
                          fg_color=th["cat_bg"], button_color=th["accent"],
                          text_color=th["text"],
                          ).pack(fill="x", padx=24, pady=(4, 0))

        # (#8) 外觀模式
        ctk.CTkLabel(self, text="外觀模式", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        self.theme_var = ctk.StringVar(value=dm.settings.get("theme", "dark"))
        ctk.CTkOptionMenu(self, variable=self.theme_var,
                          values=["dark", "light"],
                          fg_color=th["cat_bg"], button_color=th["accent"],
                          text_color=th["text"],
                          ).pack(fill="x", padx=24, pady=(4, 0))

        # (#9) 開機自動啟動
        ctk.CTkLabel(self, text="開機自動啟動", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)
        self.autostart_var = ctk.BooleanVar(
            value=dm.settings.get("autostart", False))
        ctk.CTkSwitch(self, text="啟用", variable=self.autostart_var,
                      onvalue=True, offvalue=False,
                      fg_color=th["cat_bg"], progress_color=th["accent"],
                      text_color=th["text"],
                      ).pack(anchor="w", padx=24, pady=(4, 0))

        # ── 分類管理（排序 + 刪除）──
        ctk.CTkLabel(self, text="分類管理（拖動排序 / 刪除）", text_color=th["text3"],
                     font=ctk.CTkFont(size=13)).pack(anchor="w", **pad)

        self._cat_mgmt_frame = ctk.CTkScrollableFrame(
            self, fg_color=th["input_bg"], corner_radius=8, height=140,
            scrollbar_button_color=th["scroll_btn"],
            scrollbar_button_hover_color=th["scroll_hover"])
        self._cat_mgmt_frame.pack(fill="x", padx=24, pady=(4, 0))

        self._build_cat_reorder(th)

        # 儲存
        ctk.CTkButton(self, text="💾  儲存設定", height=42,
                      fg_color=th["green"], hover_color=th["green_hover"],
                      font=ctk.CTkFont(size=14, weight="bold"),
                      command=self._save).pack(fill="x", padx=24, pady=(16, 16))

    def _build_cat_reorder(self, th):
        """建立分類排序 + 刪除列表"""
        for child in self._cat_mgmt_frame.winfo_children():
            child.destroy()

        cats = self.dm.categories
        for i, cat in enumerate(cats):
            row = ctk.CTkFrame(self._cat_mgmt_frame, fg_color="transparent", height=30)
            row.pack(fill="x", padx=4, pady=1)
            row.pack_propagate(False)

            lbl = ctk.CTkLabel(row, text=cat, font=ctk.CTkFont(size=12),
                               text_color=th["text"], anchor="w", width=140)
            lbl.pack(side="left", padx=(8, 4))

            # All 和 Copied 固定不可移動或刪除
            if cat in ("All", "Copied"):
                ctk.CTkLabel(row, text="(固定)", font=ctk.CTkFont(size=10),
                             text_color=th["text_dim"]).pack(side="right", padx=8)
                continue

            # 刪除按鈕
            if cat != "自訂":
                ctk.CTkButton(row, text="✕", width=24, height=24,
                              corner_radius=4, fg_color="transparent",
                              hover_color=th["red"], text_color=th["text_dim"],
                              font=ctk.CTkFont(size=10),
                              command=lambda c=cat: self._do_delete_cat(c, th)
                              ).pack(side="right", padx=(0, 4))

            # 重新命名按鈕
            ctk.CTkButton(row, text="✎", width=24, height=24,
                          corner_radius=4, fg_color="transparent",
                          hover_color=th["card_hover"], text_color=th["text_dim"],
                          font=ctk.CTkFont(size=11),
                          command=lambda c=cat: self._do_rename_cat(c, th)
                          ).pack(side="right", padx=(0, 2))

            # 下移
            if i < len(cats) - 1:
                ctk.CTkButton(row, text="▼", width=24, height=24,
                              corner_radius=4, fg_color="transparent",
                              hover_color=th["card_hover"], text_color=th["text2"],
                              font=ctk.CTkFont(size=10),
                              command=lambda c=cat: self._do_move_cat(c, 1, th)
                              ).pack(side="right", padx=1)

            # 上移（不可移到 All/Copied 的位置，即 index < 2）
            if i > 2:
                ctk.CTkButton(row, text="▲", width=24, height=24,
                              corner_radius=4, fg_color="transparent",
                              hover_color=th["card_hover"], text_color=th["text2"],
                              font=ctk.CTkFont(size=10),
                              command=lambda c=cat: self._do_move_cat(c, -1, th)
                              ).pack(side="right", padx=1)

    def _do_move_cat(self, name, direction, th):
        self.dm.move_category(name, direction)
        self._build_cat_reorder(th)

    def _do_rename_cat(self, old_name, th):
        dlg = ctk.CTkToplevel(self)
        dlg.title("重新命名分類")
        dw, dh = 340, 170
        dlg.geometry(f"{dw}x{dh}+{self.winfo_x()+50}+{self.winfo_y()+80}")
        dlg.resizable(False, False)
        dlg.attributes("-topmost", True)
        dlg.configure(fg_color=th["bg"])
        dlg.grab_set()

        ctk.CTkLabel(dlg, text=f"重新命名 \"{old_name}\"",
                     font=ctk.CTkFont(size=14, weight="bold"),
                     text_color=th["text"]).pack(pady=(18, 8))
        name_var = ctk.StringVar(value=old_name)
        entry = ctk.CTkEntry(dlg, textvariable=name_var, width=240,
                             font=ctk.CTkFont(size=13),
                             fg_color=th["input_bg"], text_color=th["text"])
        entry.pack(pady=(0, 12))
        entry.select_range(0, "end")
        entry.focus_set()

        btn_frame = ctk.CTkFrame(dlg, fg_color="transparent")
        btn_frame.pack(fill="x", padx=30)
        ctk.CTkButton(btn_frame, text="取消", width=100, height=34,
                      fg_color=th["cat_bg"], hover_color=th["card_hover"],
                      text_color=th["text"],
                      command=dlg.destroy).pack(side="left", expand=True, padx=4)

        def do_rename():
            new_name = name_var.get().strip()
            if self.dm.rename_category(old_name, new_name):
                dlg.destroy()
                self._build_cat_reorder(th)
            else:
                entry.configure(border_color="red")

        ctk.CTkButton(btn_frame, text="確認", width=100, height=34,
                      fg_color=th["green"], hover_color=th["green_hover"],
                      font=ctk.CTkFont(weight="bold"),
                      command=do_rename).pack(side="right", expand=True, padx=4)
        entry.bind("<Return>", lambda _: do_rename())

    def _do_delete_cat(self, name, th):
        self.dm.delete_category(name)
        self._build_cat_reorder(th)

    def _save(self):
        self.dm.settings["hotkey"] = self.hotkey_var.get()
        self.dm.settings["size"] = self.size_var.get()
        self.dm.settings["theme"] = self.theme_var.get()
        self.dm.settings["autostart"] = self.autostart_var.get()
        self.dm.save_settings()
        set_autostart(self.autostart_var.get())
        self.on_save_cb()
        self.destroy()

# ─────────────────────────── 主視窗 ───────────────────────────

class MainOverlay(ctk.CTkToplevel):
    def __init__(self, master, dm: DataManager):
        super().__init__(master)
        self.dm = dm
        self.visible = False
        self._current_category = "All"
        self._row_widgets = []
        self._filtered_indices = []
        self._dragging = False

        w, h = parse_size(dm.settings.get("size", "580 x 440"))

        self.overrideredirect(True)
        self.attributes("-topmost", True)
        self.attributes("-alpha", 0.0)

        # 置中到游標所在螢幕
        mx, my, mw, mh = get_cursor_monitor_rect()
        x = mx + (mw - w) // 2
        y = my + (mh - h) // 2
        self.geometry(f"{w}x{h}+{x}+{y}")

        self._build_ui()
        self.withdraw()
        self.after(100, self._apply_effects)

    def _apply_effects(self):
        try:
            hwnd = ctypes.windll.user32.GetParent(self.winfo_id())
            enable_blur(hwnd)
            set_rounded_corners(hwnd)
            # 設定工作列圖示
            self._set_overlay_icon(hwnd)
        except Exception:
            pass

    def _set_overlay_icon(self, hwnd):
        """用 Windows API 設定 overlay 視窗圖示（工作列）"""
        try:
            icon_dirs = []
            if getattr(sys, '_MEIPASS', None):
                icon_dirs.append(sys._MEIPASS)
            icon_dirs.append(str(_EXE_DIR))
            ico_path = None
            for bd in icon_dirs:
                p = os.path.join(bd, "assets", "00monstericon.ico")
                if os.path.exists(p):
                    ico_path = p
                    break
            if not ico_path:
                return
            WM_SETICON = 0x0080
            ICON_BIG = 1
            ICON_SMALL = 0
            LoadImage = ctypes.windll.user32.LoadImageW
            IMAGE_ICON = 1
            LR_LOADFROMFILE = 0x0010
            LR_DEFAULTSIZE = 0x0040
            hicon_big = LoadImage(0, ico_path, IMAGE_ICON, 48, 48,
                                  LR_LOADFROMFILE)
            hicon_small = LoadImage(0, ico_path, IMAGE_ICON, 16, 16,
                                    LR_LOADFROMFILE)
            if hicon_big:
                ctypes.windll.user32.SendMessageW(hwnd, WM_SETICON,
                                                   ICON_BIG, hicon_big)
            if hicon_small:
                ctypes.windll.user32.SendMessageW(hwnd, WM_SETICON,
                                                   ICON_SMALL, hicon_small)
        except Exception:
            pass

    def _get_th(self):
        return get_theme(self.dm)

    # ── 完整重建 UI ──
    def _build_ui(self):
        # 清除所有子元件
        for child in self.winfo_children():
            child.destroy()
        self._row_widgets.clear()
        self._cat_buttons = {}

        th = self._get_th()
        self.configure(fg_color=th["bg"])

        # ── 頂部標題列 (可拖曳) ──
        top_bar = ctk.CTkFrame(self, fg_color=th["bar"], height=36, corner_radius=0)
        top_bar.pack(fill="x")
        top_bar.pack_propagate(False)

        # 載入自訂圖示
        icon_widget = None
        try:
            from PIL import Image as PILImage
            icon_dirs = []
            if getattr(sys, '_MEIPASS', None):
                icon_dirs.append(sys._MEIPASS)
            icon_dirs.append(str(_EXE_DIR))
            for bd in icon_dirs:
                ip = os.path.join(bd, "assets", "00monstericon.png")
                if os.path.exists(ip):
                    pil_img = PILImage.open(ip).resize((22, 22))
                    self._title_icon = ctk.CTkImage(light_image=pil_img,
                                                    dark_image=pil_img, size=(22, 22))
                    icon_widget = ctk.CTkLabel(top_bar, image=self._title_icon, text="",
                                               width=22)
                    icon_widget.pack(side="left", padx=(10, 2))
                    break
        except Exception:
            pass

        title_lbl = ctk.CTkLabel(top_bar, text=APP_NAME,
                                 font=ctk.CTkFont(size=13, weight="bold"),
                                 text_color=th["text2"])
        title_lbl.pack(side="left", padx=(4 if icon_widget else 12, 0))

        drag_widgets = [top_bar, title_lbl]
        if icon_widget:
            drag_widgets.append(icon_widget)
        for w_drag in drag_widgets:
            w_drag.bind("<Button-1>", self._start_drag)
            w_drag.bind("<B1-Motion>", self._do_drag)
            w_drag.bind("<ButtonRelease-1>", self._end_drag)

        # 版本號
        ctk.CTkLabel(top_bar, text=f"v{APP_VERSION}",
                     font=ctk.CTkFont(size=10),
                     text_color=th["text_dim"]).pack(side="left", padx=(6, 0))

        ctk.CTkButton(top_bar, text="⚙", width=32, height=28,
                      fg_color="transparent", hover_color=th["card_hover"],
                      font=ctk.CTkFont(size=16), text_color=th["text2"],
                      command=self._open_settings).pack(side="right", padx=(0, 4))

        ctk.CTkButton(top_bar, text="✕", width=32, height=28,
                      fg_color="transparent", hover_color=th["red"],
                      font=ctk.CTkFont(size=14), text_color=th["text2"],
                      command=self.hide).pack(side="right", padx=(0, 2))

        ctk.CTkButton(top_bar, text="⬆ 更新", width=52, height=24,
                      corner_radius=6,
                      fg_color=th["accent"], hover_color=th["card_hover"],
                      font=ctk.CTkFont(size=10, weight="bold"),
                      text_color="white",
                      command=self._check_update).pack(side="right", padx=(0, 4))

        # ── 搜尋框 ──
        self.search_var = ctk.StringVar()
        self.search_var.trace_add("write", lambda *_: self._refresh_list())
        ctk.CTkEntry(self, textvariable=self.search_var,
                     placeholder_text="🔍  搜尋標題或內容...",
                     font=ctk.CTkFont(size=14), height=36, corner_radius=10,
                     fg_color=th["input_bg"], border_color=th["border"],
                     text_color=th["text"]
                     ).pack(fill="x", padx=12, pady=(10, 6))

        # ── 分類標籤列（橫向滾動）──
        cat_outer = ctk.CTkFrame(self, fg_color="transparent", height=36)
        cat_outer.pack(fill="x", padx=8, pady=(0, 6))
        cat_outer.pack_propagate(False)

        bg_hex = th["bg"]
        self._cat_canvas = tk.Canvas(cat_outer, height=34, bg=bg_hex,
                                     highlightthickness=0, bd=0)
        self._cat_canvas.pack(fill="both", expand=True)
        self.cat_frame = ctk.CTkFrame(self._cat_canvas, fg_color="transparent")
        self._cat_canvas_win = self._cat_canvas.create_window(
            (0, 0), window=self.cat_frame, anchor="nw")

        def _on_cat_frame_configure(_e=None):
            self._cat_canvas.configure(
                scrollregion=self._cat_canvas.bbox("all"))
        self.cat_frame.bind("<Configure>", _on_cat_frame_configure)

        def _on_cat_mousewheel(e):
            self._cat_canvas.xview_scroll(
                int(-1 * (e.delta / 120)), "units")
        self._cat_canvas.bind("<MouseWheel>", _on_cat_mousewheel)
        self.cat_frame.bind("<MouseWheel>", _on_cat_mousewheel)
        # 子按鈕也要綁定
        self._cat_mousewheel_cb = _on_cat_mousewheel

        self._populate_category_buttons()

        # ── 底部工具列（先 pack 以確保高度不被擠壓）──
        bottom_bar = ctk.CTkFrame(self, fg_color=th["bar"], height=56, corner_radius=0)
        bottom_bar.pack(fill="x", side="bottom")
        bottom_bar.pack_propagate(False)
        ctk.CTkButton(bottom_bar, text="＋  新增片段", height=42,
                      corner_radius=21, width=140,
                      fg_color=th["green"],
                      hover_color=th["green_hover"],
                      font=ctk.CTkFont(size=15, weight="bold"),
                      command=self._open_add).pack(side="right", padx=12, pady=7)

        # ── 可滾動列表 ──
        self.scroll_frame = ctk.CTkScrollableFrame(
            self, fg_color="transparent",
            scrollbar_button_color=th["scroll_btn"],
            scrollbar_button_hover_color=th["scroll_hover"])
        self.scroll_frame.pack(fill="both", expand=True, padx=8, pady=(0, 0))

        self._refresh_list()

    # (#9) 分類按鈕生成 — 獨立方法
    def _populate_category_buttons(self):
        for btn in self._cat_buttons.values():
            btn.destroy()
        self._cat_buttons.clear()

        th = self._get_th()
        for cat in self.dm.categories:
            is_active = (cat == self._current_category)
            btn = ctk.CTkButton(self.cat_frame, text=cat, height=28, width=0,
                                corner_radius=14,
                                fg_color=th["cat_active"] if is_active else th["cat_bg"],
                                hover_color=th["cat_active"],
                                text_color="#fff" if is_active else th["text2"],
                                font=ctk.CTkFont(size=12),
                                command=lambda c=cat: self._select_cat(c))
            btn.pack(side="left", padx=2)
            self._cat_buttons[cat] = btn
            # 綁定滾輪事件到按鈕
            if hasattr(self, '_cat_mousewheel_cb'):
                btn.bind("<MouseWheel>", self._cat_mousewheel_cb)

    # ── 拖曳（只移動位置，不碰大小）──
    def _start_drag(self, e):
        self._drag_x = e.x_root - self.winfo_x()
        self._drag_y = e.y_root - self.winfo_y()
        self._dragging = True

    def _do_drag(self, e):
        if not self._dragging:
            return
        nx = e.x_root - self._drag_x
        ny = e.y_root - self._drag_y
        self.geometry(f"+{nx}+{ny}")

    def _end_drag(self, e):
        self._dragging = False

    # ── 分類選取 ──
    def _select_cat(self, cat):
        self._current_category = cat
        th = self._get_th()
        for c, btn in self._cat_buttons.items():
            is_active = (c == cat)
            btn.configure(fg_color=th["cat_active"] if is_active else th["cat_bg"],
                          text_color="#fff" if is_active else th["text2"])
        self._refresh_list()

    # ── 刷新列表 ──
    def _refresh_list(self):
        for w_row in self._row_widgets:
            w_row.destroy()
        self._row_widgets.clear()
        self._filtered_indices.clear()

        query = self.search_var.get().strip().lower() if hasattr(self, 'search_var') else ""
        cat = self._current_category

        for idx, s in enumerate(self.dm.snippets):
            s_cat = s.get("category", "")
            # Copied 分類不顯示在 All 中
            if cat == "All" and s_cat == "Copied":
                continue
            if cat != "All" and s_cat != cat:
                continue
            if query:
                if query not in s.get("title", "").lower() and \
                   query not in s.get("content", "").lower():
                    continue
            self._filtered_indices.append(idx)

        for display_i, real_idx in enumerate(self._filtered_indices):
            s = self.dm.snippets[real_idx]
            self._create_row(display_i, real_idx, s)

    def _create_row(self, display_i, real_idx, snippet):
        th = self._get_th()
        row = ctk.CTkFrame(self.scroll_frame, fg_color=th["card"],
                           corner_radius=10, height=52)
        row.pack(fill="x", pady=2, padx=2)
        row.pack_propagate(False)

        num_text = str((display_i + 1) % 10) if display_i < 10 else ""
        num_lbl = ctk.CTkLabel(row, text="", width=22,
                               font=ctk.CTkFont(size=11, weight="bold"),
                               text_color=th["accent"])
        num_lbl.pack(side="left", padx=(6, 0))

        emoji = snippet.get("emoji", "📋")
        title = snippet.get("title", "")
        # 使用原生 tkinter Label 顯示彩色 emoji
        emoji_lbl = tk.Label(row, text=emoji, font=("Segoe UI Emoji", 16),
                             bg=th["card"], fg="white", bd=0, padx=0)
        emoji_lbl.pack(side="left", padx=(4, 2))
        title_lbl = ctk.CTkLabel(row, text=title,
                                 font=ctk.CTkFont(size=14, weight="bold"),
                                 text_color=th["text"], anchor="w")
        title_lbl.pack(side="left", padx=(0, 8))

        # ── 右側按鈕（先 pack 以確保空間）──
        del_btn = ctk.CTkButton(row, text="✕", width=28, height=28,
                                corner_radius=6,
                                fg_color="transparent",
                                hover_color=th["red"],
                                text_color=th["text_dim"],
                                font=ctk.CTkFont(size=12),
                                command=lambda ri=real_idx: self._confirm_delete(ri))
        del_btn.pack(side="right", padx=(0, 6))

        edit_btn = ctk.CTkButton(row, text="Edit", width=38, height=26,
                                 corner_radius=6,
                                 fg_color="transparent",
                                 hover_color=th["card_hover"],
                                 text_color=th["text_dim"],
                                 font=ctk.CTkFont(size=11),
                                 command=lambda ri=real_idx: self._open_edit(ri))
        edit_btn.pack(side="right", padx=(0, 2))

        # ── 上下箭頭排序按鈕 ──
        di = display_i
        down_btn = ctk.CTkButton(row, text="▼", width=22, height=22,
                                  corner_radius=4,
                                  fg_color="transparent",
                                  hover_color=th["card_hover"],
                                  text_color=th["text_dim"],
                                  font=ctk.CTkFont(size=10),
                                  command=lambda d=di: self._move_snippet(d, 1))
        down_btn.pack(side="right", padx=0)
        up_btn = ctk.CTkButton(row, text="▲", width=22, height=22,
                                corner_radius=4,
                                fg_color="transparent",
                                hover_color=th["card_hover"],
                                text_color=th["text_dim"],
                                font=ctk.CTkFont(size=10),
                                command=lambda d=di: self._move_snippet(d, -1))
        up_btn.pack(side="right", padx=0)

        content = snippet.get("content", "")
        preview = content.replace("\n", " ")[:50]
        if len(content) > 50:
            preview += "…"
        prev_lbl = ctk.CTkLabel(row, text=preview,
                                font=ctk.CTkFont(size=11),
                                text_color=th["text_dim"], anchor="w")
        prev_lbl.pack(side="left", fill="x", expand=True, padx=(0, 4))

        card_color = th["card"]
        hover_color = th["card_hover"]

        def on_enter(_):
            row.configure(fg_color=hover_color)
            num_lbl.configure(text=num_text)
        def on_leave(_):
            row.configure(fg_color=card_color)
            num_lbl.configure(text="")
        def on_click(e):
            # 避免點擊按鈕時觸發複製
            w = e.widget
            if isinstance(w, (tk.Frame, tk.Label, ctk.CTkLabel)):
                self._copy_snippet(real_idx)
        def on_right_click(e):
            w = e.widget
            if isinstance(w, (tk.Frame, tk.Label, ctk.CTkLabel)):
                self._open_edit(real_idx)

        for widget in [row, emoji_lbl, title_lbl, prev_lbl, num_lbl]:
            widget.bind("<Enter>", on_enter)
            widget.bind("<Leave>", on_leave)
            widget.bind("<Button-1>", on_click)
            widget.bind("<Button-3>", on_right_click)

        self._row_widgets.append(row)

    def _move_snippet(self, display_idx, direction):
        """在當前過濾列表中上下移動片段"""
        fi = self._filtered_indices
        new_di = display_idx + direction
        if new_di < 0 or new_di >= len(fi):
            return
        real_a = fi[display_idx]
        real_b = fi[new_di]
        self.dm.swap_snippets(real_a, real_b)
        self._refresh_list()

    # ── 檢查更新 ──
    def _check_update(self):
        th = self._get_th()
        dlg = ctk.CTkToplevel(self)
        dlg.title("檢查更新")
        dw, dh = 360, 180
        mx, my, mw, mh = get_cursor_monitor_rect()
        dlg.geometry(f"{dw}x{dh}+{mx + (mw - dw)//2}+{my + (mh - dh)//2}")
        dlg.resizable(False, False)
        dlg.attributes("-topmost", True)
        dlg.configure(fg_color=th["bg"])
        dlg.grab_set()

        status_lbl = ctk.CTkLabel(dlg, text="正在檢查更新...",
                                   font=ctk.CTkFont(size=14, weight="bold"),
                                   text_color=th["text"])
        status_lbl.pack(pady=(28, 8))
        sub_lbl = ctk.CTkLabel(dlg, text="", font=ctk.CTkFont(size=12),
                                text_color=th["text_dim"])
        sub_lbl.pack(pady=(0, 8))
        btn_frame = ctk.CTkFrame(dlg, fg_color="transparent")
        btn_frame.pack(fill="x", padx=30, pady=(8, 16))

        def do_check():
            try:
                import urllib.request, urllib.error
                req = urllib.request.Request(GITHUB_API_LATEST,
                                             headers={"Accept": "application/vnd.github+json",
                                                      "User-Agent": "ZeroClipboard"})
                try:
                    with urllib.request.urlopen(req, timeout=10) as resp:
                        data = json.loads(resp.read().decode("utf-8"))
                except urllib.error.HTTPError as he:
                    if he.code == 404:
                        self.after(0, lambda: _show_result("", "", None))
                        return
                    raise
                tag = data.get("tag_name", "").lstrip("vV")
                body = data.get("body", "")[:200]
                assets = data.get("assets", [])
                dl_url = None
                for a in assets:
                    name = a.get("name", "")
                    if name.endswith(".exe") or name.endswith(".zip"):
                        dl_url = a["browser_download_url"]
                        break
                self.after(0, lambda: _show_result(tag, body, dl_url))
            except Exception as e:
                self.after(0, lambda: _show_error(str(e)))

        def _show_result(remote_ver, body, exe_url):
            if not dlg.winfo_exists():
                return
            if remote_ver and remote_ver != APP_VERSION:
                status_lbl.configure(text=f"🎉 發現新版本 v{remote_ver}！")
                sub_lbl.configure(text=body[:100] if body else "")
                if exe_url:
                    ctk.CTkButton(btn_frame, text="⬇ 下載更新", width=130, height=36,
                                  fg_color=th["green"], hover_color=th["green_hover"],
                                  font=ctk.CTkFont(size=13, weight="bold"),
                                  command=lambda: self._do_download_update(exe_url, dlg)
                                  ).pack(side="right", expand=True, padx=4)
                else:
                    ctk.CTkButton(btn_frame, text="前往 GitHub", width=130, height=36,
                                  fg_color=th["accent"], hover_color=th["card_hover"],
                                  font=ctk.CTkFont(size=13, weight="bold"),
                                  command=lambda: os.startfile(
                                      f"https://github.com/{GITHUB_REPO}/releases/latest")
                                  ).pack(side="right", expand=True, padx=4)
                ctk.CTkButton(btn_frame, text="稍後", width=80, height=36,
                              fg_color=th["cat_bg"], hover_color=th["card_hover"],
                              text_color=th["text"],
                              command=dlg.destroy).pack(side="left", expand=True, padx=4)
            else:
                status_lbl.configure(text="✅ 已經是最新版本")
                sub_lbl.configure(text=f"目前版本 v{APP_VERSION}")
                ctk.CTkButton(btn_frame, text="確定", width=100, height=36,
                              fg_color=th["accent"], hover_color=th["card_hover"],
                              command=dlg.destroy).pack(expand=True)

        def _show_error(err):
            if not dlg.winfo_exists():
                return
            status_lbl.configure(text="❌ 檢查更新失敗")
            sub_lbl.configure(text="請檢查網路連線")
            ctk.CTkButton(btn_frame, text="確定", width=100, height=36,
                          fg_color=th["cat_bg"], hover_color=th["card_hover"],
                          text_color=th["text"],
                          command=dlg.destroy).pack(expand=True)

        threading.Thread(target=do_check, daemon=True).start()

    def _do_download_update(self, url, parent_dlg):
        """下載新版 exe 或 zip 到同一目錄並提示重啟"""
        th = self._get_th()
        parent_dlg.destroy()

        dlg = ctk.CTkToplevel(self)
        dlg.title("下載更新")
        dw, dh = 360, 150
        mx, my, mw, mh = get_cursor_monitor_rect()
        dlg.geometry(f"{dw}x{dh}+{mx + (mw - dw)//2}+{my + (mh - dh)//2}")
        dlg.resizable(False, False)
        dlg.attributes("-topmost", True)
        dlg.configure(fg_color=th["bg"])
        dlg.grab_set()

        status_lbl = ctk.CTkLabel(dlg, text="正在下載...",
                                   font=ctk.CTkFont(size=14, weight="bold"),
                                   text_color=th["text"])
        status_lbl.pack(pady=(24, 8))
        prog_lbl = ctk.CTkLabel(dlg, text="", font=ctk.CTkFont(size=11),
                                 text_color=th["text_dim"])
        prog_lbl.pack(pady=(0, 8))

        def do_download():
            try:
                import urllib.request, zipfile
                exe_name = "零零快捷剪貼板.exe"
                is_zip = url.lower().endswith(".zip")
                if getattr(sys, 'frozen', False):
                    current_exe = Path(sys.executable)
                    target_dir = current_exe.parent
                else:
                    target_dir = _EXE_DIR / "dist"
                    target_dir.mkdir(exist_ok=True)
                dl_path = target_dir / ("_update.zip" if is_zip else f"{exe_name}.new")
                req = urllib.request.Request(url, headers={"User-Agent": "ZeroClipboard"})
                with urllib.request.urlopen(req, timeout=120) as resp:
                    total = int(resp.headers.get("Content-Length", 0))
                    downloaded = 0
                    with open(dl_path, "wb") as f:
                        while True:
                            chunk = resp.read(65536)
                            if not chunk:
                                break
                            f.write(chunk)
                            downloaded += len(chunk)
                            if total > 0:
                                pct = int(downloaded / total * 100)
                                mb_dl = downloaded // 1024 // 1024
                                mb_tot = total // 1024 // 1024
                                self.after(0, lambda p=pct, d=mb_dl, t=mb_tot:
                                    prog_lbl.configure(text=f"{p}%  ({d}MB / {t}MB)"))

                new_path = target_dir / f"{exe_name}.new"
                if is_zip:
                    # 從 zip 中找到 .exe 並解壓
                    with zipfile.ZipFile(dl_path, 'r') as zf:
                        exe_in_zip = None
                        for name in zf.namelist():
                            if name.endswith(".exe"):
                                exe_in_zip = name
                                break
                        if exe_in_zip:
                            with zf.open(exe_in_zip) as src, open(new_path, 'wb') as dst:
                                dst.write(src.read())
                    try:
                        dl_path.unlink()
                    except Exception:
                        pass

                # 建立 bat 替換舊檔並重啟
                final_path = target_dir / exe_name
                bat_path = target_dir / "_update.bat"
                bat_content = f"""@echo off
timeout /t 2 /nobreak >nul
del "{final_path}"
move "{new_path}" "{final_path}"
start "" "{final_path}"
del "%~f0"
"""
                with open(bat_path, "w") as bf:
                    bf.write(bat_content)

                self.after(0, lambda: _download_done(str(bat_path)))
            except Exception as e:
                self.after(0, lambda: _download_fail(str(e)))

        def _download_done(bat_path):
            if not dlg.winfo_exists():
                return
            status_lbl.configure(text="✅ 下載完成！")
            prog_lbl.configure(text="點擊重啟以完成更新")
            ctk.CTkButton(dlg, text="🔄 重啟更新", width=140, height=38,
                          fg_color=th["green"], hover_color=th["green_hover"],
                          font=ctk.CTkFont(size=13, weight="bold"),
                          command=lambda: _restart(bat_path)).pack(pady=(4, 12))

        def _download_fail(err):
            if not dlg.winfo_exists():
                return
            status_lbl.configure(text="❌ 下載失敗")
            prog_lbl.configure(text="請手動前往 GitHub 下載")
            ctk.CTkButton(dlg, text="確定", width=100, height=36,
                          fg_color=th["cat_bg"], hover_color=th["card_hover"],
                          text_color=th["text"],
                          command=dlg.destroy).pack(pady=(4, 12))

        def _restart(bat_path):
            import subprocess
            subprocess.Popen(["cmd", "/c", bat_path],
                             creationflags=0x00000008)  # DETACHED_PROCESS
            self.after(200, lambda: os._exit(0))

        threading.Thread(target=do_download, daemon=True).start()

    # ── 確認刪除對話框 ──
    def _confirm_delete(self, idx):
        snippet = self.dm.snippets[idx]
        title = snippet.get('title', '未命名')
        dlg = ctk.CTkToplevel(self)
        dlg.title("確認刪除")
        dw, dh = 340, 180
        mx, my, mw, mh = get_cursor_monitor_rect()
        dx = mx + (mw - dw) // 2
        dy = my + (mh - dh) // 2
        dlg.geometry(f"{dw}x{dh}+{dx}+{dy}")
        dlg.resizable(False, False)
        dlg.attributes("-topmost", True)
        th = self._get_th()
        dlg.configure(fg_color=th["bg"])
        dlg.grab_set()

        ctk.CTkLabel(dlg, text="確定要刪除嗎？",
                     font=ctk.CTkFont(size=16, weight="bold"),
                     text_color=th["text"]).pack(pady=(24, 4))
        ctk.CTkLabel(dlg, text=f"\"{ title }\"",
                     font=ctk.CTkFont(size=13),
                     text_color=th["text_dim"]).pack(pady=(0, 16))

        btn_frame = ctk.CTkFrame(dlg, fg_color="transparent")
        btn_frame.pack(fill="x", padx=30, pady=(0, 16))
        ctk.CTkButton(btn_frame, text="取消", width=100, height=36,
                      fg_color=th["cat_bg"], hover_color=th["card_hover"],
                      text_color=th["text"],
                      command=dlg.destroy).pack(side="left", expand=True, padx=4)
        def do_delete():
            self.dm.delete_snippet(idx)
            dlg.destroy()
            self._on_data_changed()
        ctk.CTkButton(btn_frame, text="刪除", width=100, height=36,
                      fg_color=th["red"], hover_color=th["red_hover"],
                      font=ctk.CTkFont(weight="bold"),
                      command=do_delete).pack(side="right", expand=True, padx=4)

    # ── 複製 ──
    def _copy_snippet(self, idx):
        content = self.dm.snippets[idx].get("content", "")
        self._skip_next_clip = True
        pyperclip.copy(content)
        Toast(self, "已複製！")
        self.after(800, self.hide)

    # ── 新增 / 編輯 ──
    def _open_add(self):
        SnippetEditor(self, self.dm, self._on_data_changed)

    def _open_edit(self, idx):
        SnippetEditor(self, self.dm, self._on_data_changed, edit_idx=idx)

    def _on_data_changed(self):
        self._populate_category_buttons()
        self._refresh_list()

    def _open_settings(self):
        SettingsWindow(self, self.dm, self._on_settings_saved)

    def _on_settings_saved(self):
        ctk.set_appearance_mode(self.dm.settings.get("theme", "dark"))
        w, h = parse_size(self.dm.settings.get("size", "580 x 440"))
        mx, my, mw, mh = get_cursor_monitor_rect()
        x = mx + (mw - w) // 2
        y = my + (mh - h) // 2
        self.geometry(f"{w}x{h}+{x}+{y}")
        self._build_ui()
        self.after(50, self._apply_effects)
        if hasattr(self.master, '_register_hotkey'):
            self.master._register_hotkey()

    # ── 顯示 / 隱藏（無動畫，穩定即時）──
    def show(self):
        if self.visible:
            self.hide()
            return
        self.visible = True
        w, h = parse_size(self.dm.settings.get("size", "580 x 440"))
        mx, my, mw, mh = get_cursor_monitor_rect()
        x = mx + (mw - w) // 2
        y = my + (mh - h) // 2
        self.geometry(f"{w}x{h}+{x}+{y}")
        self.attributes("-alpha", 0.97)
        self.deiconify()
        self._refresh_list()
        if hasattr(self, 'search_var'):
            self.search_var.set("")
        self.focus_force()

    def hide(self):
        if not self.visible:
            return
        self.visible = False
        self.withdraw()
        self.attributes("-alpha", 0.0)

# ─────────────────────────── 應用程式主類 ───────────────────────────

class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.dm = DataManager()

        ctk.set_appearance_mode(self.dm.settings.get("theme", "dark"))
        ctk.set_default_color_theme("blue")

        self.title(APP_NAME)
        # 設定視窗圖示（工作列 / 標題列）— 必須在 withdraw 之前
        self._set_window_icon()
        self.withdraw()

        # (#3) 低資源佔用 — 降低空閒刷新
        self.after(500, self._idle_loop)

        self.overlay = MainOverlay(self, self.dm)

        # 剪貼簿監控
        self._last_clip = ""
        try:
            self._last_clip = pyperclip.paste() or ""
        except Exception:
            pass
        self.after(1500, self._poll_clipboard)

        # (#1) 熱鍵 — debounce
        self._hotkey_hook = None
        self._last_toggle_time = 0.0
        self._register_hotkey()

        self.protocol("WM_DELETE_WINDOW", self.quit_app)
        self._start_tray()

    def _set_window_icon(self):
        """設定視窗圖示（工作列 + 標題列）"""
        try:
            icon_dirs = []
            if getattr(sys, '_MEIPASS', None):
                icon_dirs.append(sys._MEIPASS)
            icon_dirs.append(os.path.dirname(os.path.abspath(
                sys.executable if getattr(sys, 'frozen', False) else __file__)))
            for bd in icon_dirs:
                ico = os.path.join(bd, "assets", "00monstericon.ico")
                if os.path.exists(ico):
                    self.iconbitmap(ico)
                    break
                png = os.path.join(bd, "assets", "00monstericon.png")
                if os.path.exists(png):
                    from PIL import Image, ImageTk
                    img = Image.open(png).resize((32, 32))
                    self._app_icon = ImageTk.PhotoImage(img)
                    self.iconphoto(True, self._app_icon)
                    break
        except Exception:
            pass

    def _idle_loop(self):
        """低頻率心跳，保持 app 活著但不佔 CPU"""
        self.after(2000, self._idle_loop)

    def _poll_clipboard(self):
        """每 1.5 秒檢查剪貼簿，自動記錄新內容到 Copied"""
        try:
            cur = pyperclip.paste() or ""
            if cur and cur != self._last_clip:
                self._last_clip = cur
                # 跳過由 app 自己複製的內容
                if getattr(self.overlay, '_skip_next_clip', False):
                    self.overlay._skip_next_clip = False
                else:
                    self.dm.add_clipboard_entry(cur)
                    # 如果正在看 Copied 分類，刷新列表
                    if self.overlay.visible and self.overlay._current_category == "Copied":
                        self.overlay._refresh_list()
        except Exception:
            pass
        self.after(1500, self._poll_clipboard)

    # 熱鍵處理：檢查按鍵名稱 + 防抖動 + 長按不重複觸發
    def _on_key_event(self, event):
        hk = self.dm.settings.get("hotkey", "right alt")
        # 嚴格比對按鍵名稱，避免 left alt 觸發 right alt
        if event.name != hk:
            return
        if event.event_type == 'up':
            self._key_held = False
            return
        if event.event_type != 'down':
            return
        # 長按不重複觸發
        if self._key_held:
            return
        self._key_held = True
        # 防抖動
        now = time.time()
        if now - self._last_toggle_time < 0.3:
            return
        self._last_toggle_time = now
        self.after(0, self.overlay.show)

    def _register_hotkey(self):
        if self._hotkey_hook is not None:
            try:
                keyboard.unhook(self._hotkey_hook)
            except Exception:
                pass
        hk = self.dm.settings.get("hotkey", "right alt")
        self._key_held = False
        try:
            self._hotkey_hook = keyboard.hook_key(
                hk, self._on_key_event)
        except Exception:
            self._hotkey_hook = keyboard.hook_key(
                "right alt", self._on_key_event)

    def _start_tray(self):
        try:
            import pystray
            from PIL import Image, ImageDraw

            def create_icon():
                # PyInstaller 解壓目錄 → exe 同層目錄 → 腳本目錄
                base_dirs = []
                if getattr(sys, '_MEIPASS', None):
                    base_dirs.append(sys._MEIPASS)
                base_dirs.append(os.path.dirname(os.path.abspath(
                    sys.executable if getattr(sys, 'frozen', False) else __file__)))
                icon_path = None
                for bd in base_dirs:
                    p = os.path.join(bd, "assets", "00monstericon.png")
                    if os.path.exists(p):
                        icon_path = p
                        break
                if icon_path:
                    return Image.open(icon_path).resize((64, 64))
                img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
                d = ImageDraw.Draw(img)
                d.rounded_rectangle([4, 4, 60, 60], radius=12, fill="#5c6bc0")
                d.text((16, 12), "00", fill="white")
                return img

            def on_show(icon, item):
                self.after(0, self._tray_show)
            def on_quit(icon, item):
                icon.stop()
                self.after(0, self.quit_app)

            menu = pystray.Menu(
                pystray.MenuItem("顯示剪貼板", on_show, default=True),
                pystray.MenuItem("結束", on_quit))
            self._tray_icon = pystray.Icon(APP_NAME, create_icon(), APP_NAME, menu)
            threading.Thread(target=self._tray_icon.run, daemon=True).start()
        except Exception:
            pass

    def _tray_show(self):
        """專門處理從系統托盤顯示，確保狀態正確"""
        if self.overlay.visible:
            self.overlay.hide()
        else:
            self.overlay.visible = False
            self.overlay.attributes("-alpha", 0.0)
            self.overlay.show()

    def quit_app(self):
        try:
            keyboard.unhook_all()
        except Exception:
            pass
        try:
            if hasattr(self, '_tray_icon'):
                self._tray_icon.stop()
        except Exception:
            pass
        self.destroy()
        os._exit(0)

# ─────────────────────────── 入口 ───────────────────────────

if __name__ == "__main__":
    # (#3) DPI 感知
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)  # PER_MONITOR_DPI_AWARE_V2
    except Exception:
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            pass

    # (#1) 單實例保護
    _mutex = acquire_single_instance_lock()
    if _mutex is None:
        ctypes.windll.user32.MessageBoxW(
            0, "零零快捷剪貼板已經在執行中！", APP_NAME, 0x40)
        sys.exit(0)

    app = App()
    app.mainloop()
