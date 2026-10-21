# 零零快捷剪貼板：功能與 AI 接手說明

這份文件讓新聊天中的 AI 快速了解目前功能、核心檔案，以及修改時要保護的行為。一般使用者請看 [README.md](README.md)；操作與升級說明請看 [V2-使用說明.md](V2-使用說明.md)。不要把這份接手資訊全部搬進公開首頁。

本文於 2026-10-21 依目前程式整理。原始碼版本為 **2.1.4**，使用 **C#、.NET 10、WPF 和 SQLite**，供 Windows x64 使用。版本會繼續變動，接手時以 `ClipboardApp/ClipboardApp.csproj` 的 `Version` 為準。`app.py` 等 Python 檔案屬於舊版，並非現行執行入口。

**接手狀態：**一鍵更新、背景下載進度、資料保護與正常退出交接已實作；2.1.1 修正啟動提示視窗的 Owner 錯誤；2.1.2 加入左上角實際版本顯示及發布版本同步。目前工作區另已加入「官方提示語」唯讀分類，通過本機建置與隔離介面測試。官方分類已於本機安裝 2.1.3，原 2.0 入口、開始選單及開機啟動已切換；2.1.4 更新提示語用詞與標題，已打包、通過官方功能隔離測試並於本機替換及重開。實際視窗標題為 v2.1.4；切換前後及重開後六張資料表筆數與內容雜湊完全一致。上一版完整程式與一致性資料備份位於 `backup/pre-official-2.1.4-20261021/`。本次發布目標為 v2.1.4；發布結果與公開下載核對見下方驗收紀錄，不要把舊 ZIP 當成目前原始碼的成果。

## 目前功能怎麼運作

### 1. 首次啟動與舊版匯入

- 程式先檢查是否已經開啟；重複開啟會叫出原本的管理視窗。偵測到舊版仍在執行時，會提示先正常關閉舊版。
- 已有 2.0 資料庫時，先確認資料完整、初始化或匯入已完成，再開啟介面。資料損壞或初始化不完整時會停止，不會用空資料庫蓋過問題。
- 偵測到舊版資料時，先保存來源快照，再匯入。沒有既有資料時，讓使用者選擇「開始使用」、「匯入舊版備份」或取消；取消不應建立空資料庫。
- 匯入保留文字、分類、順序與歷史。舊版 `All` 轉為未分類，`Copied` 轉入歷史；舊表情符號資料保留，但 2.0 不提供表情符號編輯介面。匯入不會沿用舊版執行檔的開機啟動設定。

### 2. 快捷面板：找文字、複製、貼上

- 預設按 **右 Alt** 開關面板，也能在設定改為右 Ctrl 或 F1～F12。面板出現在滑鼠所在螢幕。
- 分類方便瀏覽；個人文字搜尋會跨個人分類比對標題與內容，也包含未分類項目。「官方提示語」只搜尋內建內容，不混入個人搜尋或管理視窗的「所有分類」。多個以空白分隔的關鍵字必須全部符合。
- 點選項目或按 Enter 會複製完整文字並收起面板，之後由使用者自行貼上。上下鍵可選取，Esc 可關閉；中文輸入法組字中的 Enter 不應誤觸複製。
- 更新、取消或重試按鈕取得焦點時，Enter 必須操作按鈕，不能被面板攔截成複製文字。
- 列表只顯示截短預覽，不能因此裁掉實際儲存或複製的內容。

### 3. 管理視窗：草稿與正式文字分開

- 管理視窗採兩欄：左邊選取文字或切換列表，右邊編輯完整內容。
- 停止輸入約一秒後會自動保存**草稿**；按「儲存」或 Ctrl+S 才更新正式文字並清除該草稿。快捷面板使用正式文字。
- 切換編輯項目或正常結束程式前，會先保存尚未寫入的草稿。保存失敗時應保留編輯內容並阻止切換或結束，不能假裝成功。
- 一般關閉管理視窗會收回系統匣；從系統匣結束程式才會進入正常關閉流程。強制終止或突然斷電仍可能失去尚未保存的輸入。
- 可多選移動或刪除文字。刪除先進垃圾桶，可還原；永久刪除才真正移除。刪除分類會把文字移到未分類，不會連文字一起刪除。

### 4. 拖曳排序

- 從文字列左側的六點把手拖曳，顯示整列預覽及目標列外框。
- 排序只處理同一分類的文字；管理視窗也支援未分類。搜尋結果、歷史、草稿與垃圾桶不提供這種排序。
- 更新一個分類的順序時，不能打亂其他分類。底層會檢查項目是否重複、缺少、已刪除或來自其他分類。

### 5. 複製歷史

- 記錄系統剪貼簿的文字，忽略空白內容，最多保留 **50 筆**，最新在前。
- 相同文字再次出現會移到最前面；程式自己複製的內容會避免重複記錄。可暫停記錄、清空歷史，或把歷史轉成常用文字。
- 剪貼簿被其他程式短暫占用時會有限次重試，失敗時應回報，不能讓整個程式崩潰。

### 6. 外觀、開機啟動與備份

- 支援深色／淺色主題，以及 **75%～175%** 文字縮放。文字列表維持一致列高，長內容在列內截斷。
- 開機啟動設定只操作本程式的 Windows 使用者啟動項目；讀取失敗時顯示無法確認並停用切換。登錄設定與資料庫儲存不能留下互相矛盾的狀態。
- 更新版的開機啟動指向發布資料夾最外層的固定入口，不指向會被替換的 `current` 內程式。相容處理只遷移可辨識的既有本程式啟動項目，保留啟用／停用意願，不因資料庫設定而自行啟用。
- 每天第一次成功保存變更時觸發每日備份，保留最近 30 份。可手動備份及選擇備份位置；不是每隔 24 小時固定執行的背景排程。
- 還原前先驗證來源，保存目前資料的還原前備份，再準備與驗證替換資料。完成後重新整理資料、外觀及快捷鍵設定。
- 每日備份清理只處理本程式符合命名規則的每日備份，不應清掉手動備份、匯入快照或還原前備份。

### 7. 一鍵更新與非阻擋下載

- 只有快捷面板右上角、「管理」旁提供「更新並重啟」。沒有已確認的正式新版時隱藏且不占空位；不接受測試版、同版本或降版。
- 更新服務獨立於視窗，啟動約 3 秒後檢查，之後每六小時檢查。開關面板不重複查詢，也不預先下載。只有 Velopack 能辨識的正式打包環境才啟動排程；直接執行開發輸出不能據此驗收更新按鈕。
- 點擊後在標題與搜尋框之間顯示細進度條、百分比及取消按鈕；沒有可靠百分比時顯示移動式進度條。下載期間仍可搜尋、複製及編輯；收起面板不取消，再開啟顯示目前狀態。
- 下載期間停用重複更新操作。取消後回到可更新狀態；下載或準備失敗在進度區提示並提供重試。背景檢查失敗保持安靜，狀態仍為「未知」，不能冒稱最新版；已確認的更新可保留顯示。
- 從系統匣正常結束會取消下載，下一次啟動不偷偷套用未完成更新。下載完成則自動進入保存與重啟，不要求第二次確認。
- 重開後恢復快捷面板的搜尋、分類、歷史／常用文字模式，以及仍存在的選取項目。一般手動開啟面板仍沿用原本的重設搜尋行為。
- 右側操作區在窄視窗可移到下一列；更新控制項沿用共用主題與文字縮放，不另開下載視窗。

### 8. 更新時的資料保護與失敗處理

1. 等輸入組字、拖曳、近期編輯及其他對話框結束，再短暫停用編輯與剪貼簿歷史寫入。
2. 沿用草稿保存流程；失敗則停止更新並恢復操作。草稿不能自動變成正式文字。
3. 建立一致性 SQLite 備份，核對六張資料表的筆數與 SHA256 內容雜湊，保留完整上一版程式及復原說明，並檢查下載套件大小與 SHA256。
4. 啟動位於資料目錄的獨立交接程序，再正常結束主程式。交接程序最多等待原程序 90 秒，要求正常退出標記與成功退出碼才呼叫 Velopack 替換；逾時中止，不能強制終止原程序。
5. 新版啟動先核對資料完整性及原先筆數／內容雜湊，再開啟介面與歷史記錄。核對失敗保留現況與備份並提示，不自動覆寫資料庫。

Velopack 的啟動自動套用功能已明確關閉；不要改回 SDK 預設的自動安裝或逾時強制結束流程。交接期間以鎖避免另一個程式啟動與替換互相干擾。現行保護只支援資料格式 1，不包含資料格式遷移或自動資料回復。

### 9. 左上角版本顯示

- 快捷面板顯示「快捷取用 v版本」，管理視窗左上角與標題顯示產品名稱及版本，統一由 `AppVersion` 讀取目前執行組件的版本；不在介面寫死號碼，也不拿遠端尚未安裝的版本冒充目前版本。
- 正式發布腳本預設讀取 `.csproj`，正式打包若指定不同版本會停止；只有隔離測試套件可覆寫版本。GitHub 發布標籤也必須與程式及套件一致。
- [AGENTS.md](AGENTS.md) 已要求：每次推送包含程式行為或介面變更的提交前遞增版本，同批變更共用版本，純文件變更不必遞增。使用者安裝新版並重開後才會看到新版號，單純推送原始碼不會改變正在執行的程式。

## 核心程式檔案

### 官方提示語：內容維護與唯讀規則

- `ClipboardApp/Services/OfficialPrompts.cs` 是唯一內容來源，內建五筆：討論計劃、主動提問、Multi-agent 實行計劃、建立 README、更新 README。第一筆以 `@READMEGameIntro` 開頭並獨立換行，要求像 12 歲一樣解釋，不要求使用 Unity-MCP；所有正文都是可複製的字面文字，不替換檔名、不執行提示語。
- 分類及每筆內容的 GUID 必須跨版本穩定。維護時在同一份清單修改標題、正文或順序，隨程式更新發布；不新增遠端內容下載、不把官方項目寫入 SQLite 或混入 `ClipboardSnapshot` 的個人資料。
- 快捷面板把官方入口放在分類區右上側，左側個人分類可換行及捲動；官方入口不跟著捲動。無自訂分類仍可使用，歷史模式隱藏整個分類區。點文字列或按 Enter 複製全文後收起，複製失敗保留面板並提示重試。
- 管理視窗分類下拉選單最後顯示「官方提示語 · 內建唯讀」，右側可捲動、選取全文並按「複製全文」，複製後不收起。個人同名分類繼續存在，以識別碼區分，不能靠名稱判定唯讀。
- 官方分類與內容不可新增、修改、刪除、搬移或排序，也不可作為個人文字的目的地；介面事件與控制器寫入前均有保護，混合批次若包含官方識別碼會整批拒絕。官方閱讀不啟動草稿；切入官方分類前仍須成功保存待存的個人草稿。
- `MainWindow` 的兩欄保留預設比例並隨視窗縮放，確保窄視窗的全文與複製按鈕可用。官方列表維持固定列高並沿用主題及縮放。
- 官方搜尋與個人搜尋完全分開，但切換分類保留搜尋字。更新重開透過既有 `PanelResumeState` 恢復官方分類、搜尋及選取；一般手動開啟維持預設個人分類與清除搜尋。還原個人備份不會改變官方內容。
- 不提供官方內容編輯器、另存副本按鈕、資料庫遷移或額外公開操作介面。使用者可自行透過既有複製與新增文字流程製作個人版本。

### 現有模組

整體流程是：啟動程式準備資料 → 視窗透過控制器提出操作 → 資料層保存 → 通知介面更新。Windows 剪貼簿與快捷鍵事件也透過這套流程接入。

| 檔案 | 負責內容 |
| --- | --- |
| [Program.cs](ClipboardApp/Program.cs) | 最初執行入口，檢查交接鎖、關閉 Velopack 自動套用，再啟動 WPF；集中定義資料目錄。 |
| [App.xaml.cs](ClipboardApp/App.xaml.cs) | 啟動資料核對、單一執行個體、首次設定、匯入、視窗生命週期、更新前暫停寫入及正常結束交接。 |
| [AppVersion.cs](ClipboardApp/AppVersion.cs) | 從實際執行組件取得版本，供快捷面板及管理視窗共用。 |
| [AppUpdateService.cs](ClipboardApp/Services/AppUpdateService.cs) | 更新查詢排程、正式版篩選、下載進度、取消及錯誤狀態；不依附面板顯示狀態。 |
| [UpdatePreparation.cs](ClipboardApp/Services/UpdatePreparation.cs) | 更新前備份與套件核對、完整舊程式保存、固定入口、交接啟動及重開資料核對。 |
| [ClipboardUpdateHandoff/Program.cs](ClipboardUpdateHandoff/Program.cs) | 獨立程序，等待主程式正常退出後才呼叫更新工具，記錄結果並重新啟動。 |
| [ClipboardController.cs](ClipboardApp/Services/ClipboardController.cs) | 連接介面與資料層，處理複製、歷史、設定、備份，以及資料／設定更新通知。 |
| [IClipboardController.cs](ClipboardApp/Services/IClipboardController.cs) | 介面使用的操作契約、資料快照與 `ClipboardSettings`。 |
| [OfficialPrompts.cs](ClipboardApp/Services/OfficialPrompts.cs) | 五筆內建提示語、穩定識別碼與寫入保護；兩個視窗共用，與個人資料庫分開。 |
| [MainWindow.xaml](ClipboardApp/MainWindow.xaml)、[MainWindow.xaml.cs](ClipboardApp/MainWindow.xaml.cs) | 管理視窗、編輯、草稿、多選、分類與垃圾桶。共用提示視窗 **`UiPrompt` 也在這個 `.xaml.cs` 裡**。 |
| [QuickPanel.xaml](ClipboardApp/QuickPanel.xaml)、[QuickPanel.xaml.cs](ClipboardApp/QuickPanel.xaml.cs) | 快捷面板、搜尋、鍵盤選取、複製與收起。 |
| [SettingsWindow.xaml](ClipboardApp/SettingsWindow.xaml)、[SettingsWindow.xaml.cs](ClipboardApp/SettingsWindow.xaml.cs) | 快捷鍵、主題、縮放、開機啟動及備份設定介面。 |
| [WindowsIntegration.cs](ClipboardApp/Services/WindowsIntegration.cs) | `SingleInstanceGuard`、`GlobalHotkeyHook`、`ClipboardWatcher` 與系統匣整合。 |
| [LegacyImportSource.cs](ClipboardApp/Services/LegacyImportSource.cs) | 取得舊版來源與快照，檢查複製前後內容是否一致，避免匯入變動中的資料。 |
| [SnippetReorder.cs](ClipboardApp/Services/SnippetReorder.cs) | 依拖曳目標計算文字的新順序。 |
| [DragRowPreview.cs](ClipboardApp/Styles/DragRowPreview.cs)、[DropTargetIndicator.cs](ClipboardApp/Styles/DropTargetIndicator.cs) | 拖曳整列預覽與目標外框。 |
| [Theme.xaml](ClipboardApp/Styles/Theme.xaml)、[UiPreferences.cs](ClipboardApp/Styles/UiPreferences.cs) | 共用視覺樣式、主題及文字縮放。 |
| [ClipboardRepository.cs](ClipboardCore/ClipboardRepository.cs) | 資料庫結構與驗證、文字／分類／草稿／歷史的讀寫及排序。 |
| [ClipboardRepository.ImportBackup.cs](ClipboardCore/ClipboardRepository.ImportBackup.cs) | 同一資料類別的另一部分，負責舊版匯入、一致性備份、每日備份與還原。 |
| [ClipboardRepository.Update.cs](ClipboardCore/ClipboardRepository.Update.cs)、[UpdateJournal.cs](ClipboardCore/UpdateJournal.cs) | 六張資料表筆數與內容雜湊、套件核對、交接紀錄、路徑驗證、原子寫入及面板恢復狀態。 |
| [Models.cs](ClipboardCore/Models.cs)、[SqliteDb.cs](ClipboardCore/SqliteDb.cs) | 資料模型及 SQLite 存取；透過 Windows `winsqlite3.dll`，不是 `Microsoft.Data.Sqlite` 套件。 |
| [ClipboardApp.csproj](ClipboardApp/ClipboardApp.csproj)、[Portable.pubxml](ClipboardApp/Properties/PublishProfiles/Portable.pubxml) | 產品版本、固定 Velopack 1.2.161 及 Windows x64 自帶執行環境的資料夾發布設定。單用 publish profile 不等於完成更新套件。 |
| [Build-UpdateRelease.ps1](tools/Build-UpdateRelease.ps1) | 建置主程式及交接程序，以固定版本 vpk 打包免安裝 ZIP、完整更新套件及版本索引；不執行上傳。 |
| [ClipboardCore.Tests/Program.cs](ClipboardCore.Tests/Program.cs) | 核心功能的合成資料測試及效能檢查。私人備份測試預設關閉，不可隨意啟用。 |
| [ClipboardApp.Smoke/Program.cs](ClipboardApp.Smoke/Program.cs)、[ProbeController.cs](ClipboardApp.Smoke/ProbeController.cs) | 隔離資料下的啟動、介面與失敗情境檢查。 |
| [UpdateSmoke.cs](ClipboardApp.Smoke/UpdateSmoke.cs)、[PromptSmoke.cs](ClipboardApp.Smoke/PromptSmoke.cs) | 更新服務／介面模擬情境，以及提示視窗擁有者生命週期回歸檢查。 |
| [OfficialPromptsSmoke.cs](ClipboardApp.Smoke/OfficialPromptsSmoke.cs) | 官方內容、唯讀、獨立搜尋、草稿切換、備份還原、更新狀態恢復及兩種主題／三種縮放的隔離驗證。 |
| [UpdateTestHarness.cs](ClipboardApp/UpdateTestHarness.cs)、[Test-UpdatePackages.ps1](tools/Test-UpdatePackages.ps1) | 隔離套件實際升級與重開驗收。測試入口僅在 `UpdateTestBuild=true` 編入；測試包不可發布。 |

## 資料放在哪裡

- 現行資料目錄仍為 `%APPDATA%\零零快捷剪貼板 2.0\`。名稱保留是為了沿用資料，不隨產品升到 2.1.x 而改名。
- `clipboard.db` 保存文字、分類、草稿、歷史與設定；`backups\` 是預設備份位置，`original-import\` 保存匯入來源快照。
- 舊版自動偵測來源：`%APPDATA%\零零快捷剪貼板\clipboard_data.json`。
- 資料庫主要資料表為 `meta`、`categories`、`snippets`、`drafts`、`history`、`settings`。目前 `schema_version` 是 **1**，與產品版本不同，不可只因版本名稱而修改。
- `updates/<作業編號>/` 保存 `before-update.sqlite`、完整上一版 `previous/`、交接程序、`job.json`、結果與復原說明；失敗時可能有 `handoff-error.txt`，更新工具寫入 `installer.log`。`updates/active.json` 指向待核對作業，交接鎖為 `updates/update.lock`。這些復原資料不會自動清理。
- 安裝資料夾的 `packages/` 保存更新套件；使用者資料及上一版備份位於上述 AppData 目錄，在程式替換範圍之外。交接紀錄含本機路徑與搜尋狀態，也屬私人資料，不可提交或上傳。
- 文字保存在本機，並未加密。不要把真實剪貼簿內容、資料庫或私人備份提交到 GitHub。
- 使用中的 SQLite 可能有 WAL／SHM 檔案；只複製 `clipboard.db` 不保證取得完整資料，應沿用既有的一致性備份流程。

## 新聊天接手時，AI 先確認這些事

1. 先讀 [AGENTS.md](AGENTS.md)，再看目前 Git 狀態與相關程式。保留使用者未提交的修改；只處理這個專案。
2. 以目前實作為準。[IMPLEMENTATION_TODO.md](IMPLEMENTATION_TODO.md) 是歷史紀錄，可能保留舊版規劃；`app.py`、`build.py` 與 Python 打包設定不能當成 2.0 的入口。
3. 草稿與正式文字是兩份不同狀態。`draftDirty` 才是待保存判斷，不要靠介面提示文字推斷。切換及結束前必須尊重 `FlushDraft()`／`TryPrepareToClose()` 的失敗結果。
4. 草稿的 `CategoryId = null` 表示使用者選了未分類，不代表「缺資料，改用正式文字的分類」。移動文字時也要同步處理草稿分類。
5. 匯入先完整驗證，再以交易寫入；完成標記與資料一起提交。不要恢復成只依賴外部標記檔，也不要在匯入已提交後，因後續檔案錯誤而刪除成功匯入的資料。
6. 歷史去重後要整理順序再限制 50 筆，避免順序空洞造成誤刪。永久刪除只能影響垃圾桶中的項目，不能順便刪掉一般文字的草稿。
7. 新增程式內提示使用 `UiPrompt`，沿用 `Theme.xaml` 與 `UiPreferences`。不要加入未套用風格的 WPF `MessageBox`；縮放時避免把繼承字級再乘一次。原生檔案選擇器與系統匣選單可維持 Windows 外觀。
8. 修改介面時兼顧深／淺主題、鍵盤操作、窄螢幕與縮放；列表保持固定列高。實際執行測試仍依當次使用者授權，不要自行操作正式資料。
9. 共用 `UiPrompt` 只能把可見且已有原生視窗控制代碼的視窗設為 Owner；未顯示、隱藏或已關閉的視窗只提供主題／字級，提示獨立置中並顯示於工作列。不要為了設 Owner 強行建立視窗控制代碼。這曾造成使用者啟動失敗，不能只修單一呼叫處。
10. 交接程序必須在主程式仍存活時保留程序控制代碼，退出後向 Windows 核對退出碼；曾經直接對非自己啟動的程序讀取 `ExitCode` 而失敗。不得省略正常退出核對或改成強制結束。
11. 更新服務與介面狀態維持分離；不要把下載、備份與替換全部塞進按鈕事件。更新準備失敗時必須恢復介面與歷史寫入，即使寫入中止紀錄失敗也不能留下永久凍結。

## 發布與接手邊界

GitHub 專案是 [00vchannel/TseroShortClipBoard](https://github.com/00vchannel/TseroShortClipBoard)。本機保留的發布紀錄顯示，2026-10-08 已發布 [v2.0.0](https://github.com/00vchannel/TseroShortClipBoard/releases/tag/v2.0.0)，對應提交 `cecb5bd`；當時已核對公開下載 ZIP 與其中執行檔的雜湊。這是既有紀錄，不代表本文建立時重新查證了線上狀態。

接手時必須分清楚三件事：

- **推送原始碼**：GitHub 上的程式檔已更新。
- **發布下載版本**：使用者下載的 Release 附件已重新打包並上傳。
- **替換本機程式**：這台電腦實際執行的檔案已切換。

三者不會自動一起完成。先前發布時保留了原本本機執行檔；不能只看原始碼版本，就認定使用者正在執行同一版本。需要替換正式程式時，遵守 `AGENTS.md`：正常結束、建立一致性備份、保留舊執行檔，切換後再核對資料。

### 現行發布格式

- 使用 **Velopack 1.2.161**，更新來源為上述 GitHub 專案的正式 Releases，通道固定 `win-x64`，只下載完整套件，不使用差異套件，不放入私人 GitHub 憑證。
- 發布附件為 `ZeroZeroClipboard-win-x64-Portable.zip`、該版 `*-full.nupkg` 及 `releases.win-x64.json`。工具另外產生的 Setup 不是本次產品的發行入口。
- 使用者完整解壓 ZIP，從最外層 `零零快捷剪貼板.exe` 開啟；保留 `Update.exe`、`current/` 及其他檔案，搬動時搬整個資料夾。舊版單一 EXE 發布已被取代，不能只複製 `current` 內的主程式。
- 原本 2.0 使用者第一次仍需手動取得具更新能力的新版，之後才能一鍵更新。沒有新增強制更新、帳號、雲端同步、圖片／檔案歷史或常駐下載通知。
- `tools/Build-UpdateRelease.ps1` 不指定版本時讀取目前專案版本；輸出資料夾已存在會停止，避免覆蓋或混版。腳本不推送、不上傳，也不替換現用程式。
- [更新發布與驗收.md](tools/更新發布與驗收.md) 提供正式打包與隔離驗收流程；正式版本以目前專案和 AGENTS 的規則為準，不照搬隔離測試的版本參數。

### 既有驗證證據與尚未完成事項

詳細歷史見 [更新驗收紀錄.md](tools/更新驗收紀錄.md)。以下是先前執行紀錄，不表示新聊天已重新測試：

| 範圍 | 已知結果與限制 |
| --- | --- |
| 本機套件實際升級 | `artifacts/update-acceptance/run3/data/acceptance.json` 記錄 2.1.0 → 2.1.1.0，搜尋、草稿、正式文字保留；同作業有安裝成功紀錄及上一版／資料庫備份。這次發生於最新共用提示修正之前，不能當成現有 2.1.1 ZIP 或 2.1.2 的完整驗收。 |
| 隔離測試限制 | 真正使用更新服務、交接程序與 Velopack 替換，但套件來源為本機目錄；測試入口停用真實快捷鍵、剪貼簿監聽、系統匣及開機啟動遷移，因此未涵蓋正式環境所有啟動路徑。 |
| 下載失敗及介面 | 先前已測取消、重試、面板隱藏、鍵盤、深／淺色與 75%／100%／175% 縮放；下載失敗等情境使用模擬來源，不等於實際網路故障驗證。 |
| 提示視窗修正 | 2.1.1 已實際開啟並檢查未顯示、可見、隱藏、已關閉擁有者情境，完整介面檢查通過；使用者原環境最初是哪則提醒觸發錯誤仍未確認。 |
| 2026-10-21 本機工作區 | 包含官方提示語的程式與測試專案建置通過（零警告／錯誤）；官方功能及完整介面回歸測試通過。涵蓋控制器寫入拒絕、個人草稿失敗阻止切換、備份還原與六表筆數／雜湊一致，以及兩個視窗深／淺色、75%／100%／175% 縮放。官方快捷面板以 360 寬、管理視窗以 780 寬檢查並保存畫面至 `ui-preview/official-*.png`。 |
| 本次驗證限制 | 複製成功／失敗以模擬剪貼簿驗證全文及介面行為，不覆寫使用者系統剪貼簿；中文組字採模擬狀態與鍵盤事件。更新只驗證狀態恢復及既有模擬更新測試，未重新執行套件實際升級、真實 IME 或外部 App 貼上驗收。 |
| 現有輸出 | `artifacts/releases/2.1.4/releases/` 為本次正式套件，版本、完整套件大小與 SHA256／SHA1 已核對，未含私人資料與隔離測試入口。舊包仍保留；含 `DO-NOT-PUBLISH.txt` 的隔離測試包絕不可公開。 |
| 公開發布與正式環境 | 2026-10-21 發布前確認公開最新版為 v2.0.0，使用者已授權推送並發布 v2.1.4；結果見更新驗收紀錄。本機已切換 v2.1.4 並核對六表資料一致、啟動入口與開機啟動路徑。未實測另一台乾淨 Windows、實際重開機、斷電、磁碟耗盡或防毒鎖檔。 |

執行檔尚未簽章，套件雜湊不等於發行者簽章。更新失敗優先保留原程式與資料，不承諾所有故障都能自動恢復。後續回報必須區分原始碼修改、靜態檢查、實際執行、打包、公開發布及本機切換；未達成的項目明確列出。

官方功能可單獨驗證：先執行 `dotnet build ClipboardApp.Smoke/ClipboardApp.Smoke.csproj --no-restore -m:1`，再執行 `dotnet run --project ClipboardApp.Smoke/ClipboardApp.Smoke.csproj --no-build -- --official-only`。移除 `-- --official-only` 可執行完整介面回歸。測試使用工作區隔離資料及模擬剪貼簿，不啟動正式資料的監聽或開機啟動寫入。
