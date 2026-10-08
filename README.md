# 零零快捷剪貼板 v1.0.1

Windows 專用快捷剪貼板管理工具。單一 exe，免安裝即可使用。
使用 Python + CustomTkinter 開發，PyInstaller 打包。

- **GitHub**: https://github.com/00vchannel/TseroShortClipBoard
- **開發者**: Deep Frame Studio Limited

---

## 專案結構

```
零零快捷剪貼板/
├── app.py                 # 主程式（所有邏輯，單檔案架構）
├── build.py               # PyInstaller 打包腳本
├── version_info.txt       # EXE 版本資訊（防毒誤報用）
├── requirements.txt       # Python 依賴
├── README.md              # 本文件
├── assets/
│   ├── 00monstericon.png  # App 圖示 PNG（托盤、標題列）
│   └── 00monstericon.ico  # App 圖示 ICO（EXE 圖示，含 16/32/48/64/128/256px 多尺寸）
├── build/                 # [自動生成] PyInstaller 暫存
└── dist/
    └── 零零快捷剪貼板.exe  # [自動生成] 打包後的執行檔
```

### 使用者資料位置

資料存放在 `%APPDATA%\零零快捷剪貼板\`（重裝 exe 後資料保留）：
- `clipboard_data.json` — 片段 & 分類資料
- `settings.json` — 使用者設定

---

## 重要常數（app.py 頂部）

```python
APP_NAME = "零零快捷剪貼板"
APP_VERSION = "1.0.1"                    # ⬅ 發佈新版時改這裡
GITHUB_REPO = "00vchannel/TseroShortClipBoard"
GITHUB_API_LATEST = f"https://api.github.com/repos/{GITHUB_REPO}/releases/latest"
COPIED_MAX = 50                          # Copied 分類最多保留筆數
```

---

## 功能清單

### 熱鍵呼出
- **預設**: `Right Alt`（可在設定更換，支援 18 種快捷鍵）
- 按下後在**游標所在螢幕**中央顯示毛玻璃 overlay
- 再按一次關閉。400ms 防抖動，按住不會重複觸發
- 左 Alt 與右 Alt 獨立判斷，不會互相觸發

### 主視窗（MainOverlay）
- 預設 580×440，可在設定調整（7 種預設尺寸）
- 深色 / 淺色主題切換
- 無邊框、圓角（`DWM_WINDOW_CORNER_PREFERENCE`）、always-on-top
- Win32 毛玻璃效果（`SetWindowCompositionAttribute`）
- 頂部標題列：自訂圖示 + App 名稱 + **版本號** + **⬆ 更新**按鈕 + ⚙ 設定 + ✕ 關閉
- 頂部標題列可拖曳移動（拖曳時鎖定尺寸避免閃爍）

### 搜尋
- 即時模糊搜尋（標題 + 內容），每次輸入即刻篩選

### 分類系統
- **All**：顯示所有片段（不含 Copied）— 固定第一位，不可刪除
- **Copied**：自動記錄最近 50 筆複製內容 — 固定第二位，不可刪除
- **自訂分類**：使用者可新增、刪除、重新命名（✎）、排序（▲▼）
- 分類列支援**水平滾動**（滑鼠滾輪 / 觸控板），分類過多時不會消失
- 使用者刪除的分類不會在重啟後復活（載入時只確保 All 和 Copied 存在）

### 片段列表
- `CTkScrollableFrame` 可滾動
- 每列：彩色 Emoji（`tk.Label` + `Segoe UI Emoji` 字體）+ 標題 + 內容預覽
- Hover 時顯示數字提示（1~0）
- **左鍵點擊** → 複製到剪貼簿 → Toast「已複製！」→ 自動關閉
- **右鍵點擊** → 開啟編輯視窗
- 每列右側按鈕：**▲▼**（排序）、**Edit**（編輯）、**✕**（刪除，有確認對話框）
- 新增的片段會出現在列表**最頂部**

### 新增 / 編輯視窗（SnippetEditor）
- Emoji 按鈕 → 64 個常用 emoji 彩色選擇器
- 標題、分類（下拉 + 新增）、多行內容
- 編輯模式有「刪除」按鈕
- **重要**：編輯器用物件引用（`is` 身份比對）追蹤目標片段，不受剪貼簿監控插入新項目的影響

### 設定頁面（SettingsWindow）
- **快捷鍵**：18 種預設選項
- **視窗尺寸**：7 種預設
- **外觀模式**：dark / light
- **自動啟動**：開機自動啟動（Windows Registry）
- **分類管理**（可滾動）：每個分類可 ▲▼ 排序、✎ 重新命名、✕ 刪除

### 一鍵更新
- 頂部「⬆ 更新」按鈕 → 呼叫 GitHub Releases API 檢查最新版本
- 有新版 → 自動下載 .exe 或 .zip（從 zip 自動解壓 exe）→ 顯示進度
- 下載完成 → 建立 bat 腳本替換舊 exe → 重啟 app
- 無新版 → 顯示「已是最新版本」
- 無 Release / 404 → 顯示「已是最新版本」（不報錯）

### 系統托盤
- `pystray` 系統托盤圖示（使用自訂 `00monstericon.png`）
- 右鍵選單：顯示 / 結束

### 單實例鎖
- 使用 Win32 Named Mutex（`Global\ZeroZeroClipboard_SingleInstance`）
- 第二個實例啟動時自動退出

### 剪貼簿監控
- 每 1.5 秒輪詢剪貼簿（`pyperclip.paste()`）
- 新內容自動加入 Copied 分類（最多 50 筆）
- `_skip_next_clip` 旗標避免自身複製被重複記錄

---

## 類別架構（app.py）

| 類別 | 用途 |
|------|------|
| `DataManager` | 資料層：讀寫 JSON、CRUD 片段與分類、swap/rename/move 操作 |
| `Toast` | 浮動通知（淡入 → 停留 → 淡出 → 自毀） |
| `EmojiPicker` | 8×8 彩色 emoji 選擇器（原生 `tk.Label` + `Segoe UI Emoji`） |
| `SnippetEditor` | 新增/編輯視窗（用 `_edit_snippet_ref` + `is` 追蹤物件） |
| `SettingsWindow` | 設定頁面（熱鍵、尺寸、主題、自動啟動、分類管理含排序/重命名/刪除） |
| `MainOverlay` | 主 overlay（搜尋、分類、片段列表、更新檢查、排序箭頭） |
| `App` | 入口：熱鍵管理、托盤、剪貼簿監控、生命週期 |

### Win32 API 使用

| 函數 | 用途 |
|------|------|
| `enable_blur(hwnd)` | `SetWindowCompositionAttribute` 毛玻璃效果 |
| `set_rounded_corners(hwnd)` | `DwmSetWindowAttribute` 圓角視窗 |
| `get_cursor_monitor_rect()` | `GetCursorPos` + `MonitorFromPoint` + `GetMonitorInfoW` 多螢幕定位 |
| `acquire_single_instance_lock()` | `CreateMutexW` 單實例鎖 |
| `set_autostart()` | `winreg` 註冊表寫入開機自動啟動 |
| `_set_overlay_icon()` | `LoadImageW` + `SendMessageW(WM_SETICON)` 設定工作列圖示 |

### 資料檔案格式

**clipboard_data.json**
```json
{
  "snippets": [
    {"emoji": "👋", "title": "打招呼", "category": "聊天", "content": "嗨！你好嗎？"}
  ],
  "categories": ["All", "Copied", "自訂", "聊天", "上班族"]
}
```

**settings.json**
```json
{
  "hotkey": "right alt",
  "size": "580 x 440",
  "theme": "dark",
  "autostart": false
}
```

### 主題系統

`THEMES` 字典定義 `dark` / `light` 兩套完整配色，包含：
- `bg`, `bar`, `card`, `card_hover`, `input_bg` — 背景色
- `border`, `cat_bg`, `cat_active` — 邊框與分類
- `text`, `text2`, `text3`, `text_dim` — 文字色階
- `accent`, `green`, `red` + hover 色 — 強調色
- `scroll_btn`, `scroll_hover` — 捲軸按鈕色

切換主題時 `_build_ui()` 會銷毀所有子元件並重建。

---

## 已知限制

| 問題 | 狀態 | 說明 |
|------|------|------|
| D3D 獨佔全螢幕 | ⚠️ 部分支援 | `TOPMOST` 在無邊框/視窗化模式正常，D3D 獨佔模式可能無法覆蓋 |
| JSON 損壞 | ✅ 已處理 | `load_data` try/except，損壞時重設預設值 |
| 熱鍵註冊失敗 | ✅ 已處理 | fallback 到 `right alt` |
| 單實例 | ✅ 已處理 | Win32 Mutex 防止重複開啟 |
| DPI 縮放 | ✅ 已處理 | `SetProcessDpiAwareness(2)` Per-Monitor V2 |
| 大量片段 | ⚠️ 可能慢 | 超過 ~200 片段時 `_refresh_list` 可能卡頓 |

---

## 打包為 exe

```bash
pip install -r requirements.txt
python build.py
```

> `build.py` 現在預設會在打包後執行 **Windows 程式碼簽章**。若未設定簽章憑證會直接失敗，避免發佈未簽章檔案。

### 簽章前置設定（必要）

至少提供一種憑證來源：

1. PFX 憑證檔（建議）
2. 已安裝在憑證存放區的憑證（主體名稱或 SHA1）

```powershell
# 必填（擇一）
$env:SIGN_PFX_PATH = "C:\\certs\\codesign.pfx"
$env:SIGN_PFX_PASSWORD = "<your-password>"
# 或
$env:SIGN_CERT_SHA1 = "0123456789ABCDEF0123456789ABCDEF01234567"
# 或
$env:SIGN_CERT_SUBJECT = "Deep Frame Studio Limited"

# 選填
$env:SIGNTOOL_PATH = "C:\\Program Files (x86)\\Windows Kits\\10\\bin\\10.0.22621.0\\x64\\signtool.exe"
$env:SIGN_TIMESTAMP_URL = "http://timestamp.digicert.com"
```

僅本機開發測試可用（禁止發佈）：

```powershell
python build.py --unsigned
```

`build.py` 會自動執行：
```
PyInstaller --onefile --windowed --noupx
  --name "零零快捷剪貼板"
  --icon assets/00monstericon.ico
  --version-file version_info.txt
  --add-data "assets;assets"
  --hidden-import pystray PIL PIL._tkinter_finder
  --collect-all customtkinter
  + signtool sign / verify（SHA256 + timestamp）
```

輸出：`dist/零零快捷剪貼板.exe`

---

## 發佈新版更新（完整步驟）

### 前置需求（只需做一次）
1. 安裝 GitHub CLI：`winget install GitHub.cli`
2. 登入：`gh auth login --web`（瀏覽器授權）

### 每次發佈流程

**步驟 1：改版本號**
打開 `app.py`，修改第 37 行：
```python
APP_VERSION = "1.1.0"   # 改成新版本號
```

**步驟 2：打包 exe**
```powershell
python build.py
```

**步驟 3：壓縮 zip**
（GitHub 不接受中文檔名和直接上傳 exe，必須用英文檔名 zip）
```powershell
Compress-Archive -Path "dist\零零快捷剪貼板.exe" -DestinationPath "dist\TseroShortClipBoard_v1.1.0.zip" -Force
```

**步驟 4：發佈 Release**
```powershell
gh release create v1.1.0 "dist\TseroShortClipBoard_v1.1.0.zip" --repo 00vchannel/TseroShortClipBoard --title "v1.1.0" --notes "更新內容描述"
```

完成！所有使用者在 app 內點「⬆ 更新」就能一鍵下載新版。

### 更新流程原理

```
使用者點「⬆ 更新」
  → app 呼叫 GitHub API: /repos/.../releases/latest
  → 比對 tag (v1.1.0) vs APP_VERSION (1.0.0)
  → 不同 → 顯示「發現新版本」+ 下載按鈕
  → 下載 .zip asset → 解壓出 .exe → 存為 .exe.new
  → 建立 _update.bat（等 app 關閉後替換舊 exe 並重啟）
  → 使用者點「重啟更新」→ app 關閉 → bat 執行替換 → 新版啟動
```

### 注意事項
- GitHub Release 的 **tag 名稱**必須是 `vX.X.X` 格式（如 `v1.1.0`）
- zip 檔案名稱**不能有中文**，否則 GitHub 會拒絕上傳
- zip 裡面的 exe 檔名必須是 `零零快捷剪貼板.exe`
- `version_info.txt` 裡的版本號也建議同步更新（影響 exe 檔案屬性）

---

## 依賴

| 套件 | 用途 |
|------|------|
| `customtkinter` | 現代化 Tkinter UI 框架 |
| `keyboard` | 全域熱鍵監聽 |
| `pyperclip` | 剪貼簿操作 |
| `pystray` | 系統托盤圖示 |
| `pillow` | 圖片處理（圖示載入） |
| `pyinstaller` | 打包為 exe |
