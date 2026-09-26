using System.Text.Json;
using System.Diagnostics;
using ClipboardCore;

var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var isolatedRoot = Path.Combine(workspace, "ClipboardCore.Tests", ".test-output", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(isolatedRoot);
try
{
    var legacyPath = Path.Combine(isolatedRoot, "legacy.json");
    var settingsPath = Path.Combine(isolatedRoot, "settings.json");
    var original = new
    {
        categories = new[] { "All", "Copied", "工作", "閒聊" },
        snippets = new[]
        {
            new { emoji = "📌", title = "測試", category = "工作", content = "第一行\r\n第二行  " },
            new { emoji = "🙂", title = "測試", category = "閒聊", content = "emoji 👩🏽‍💻" },
            new { emoji = "📋", title = "歷史", category = "Copied", content = "複製文字\n" }
        }
    };
    File.WriteAllText(legacyPath, JsonSerializer.Serialize(original));
    File.WriteAllText(settingsPath, "{\"hotkey\":\"right alt\",\"autostart\":true}");
    var repository = new ClipboardRepository(Path.Combine(isolatedRoot, "data"));
    var imported = repository.ImportLegacy(legacyPath, settingsPath);
    Check(imported == new ImportResult(2, 4, 1), "匯入數量");
    Check(repository.ListSnippets().Select(s => s.Content).SequenceEqual(original.snippets.Take(2).Select(s => s.content)), "文字內容與順序");
    Check(repository.ListHistory().Single().Content == original.snippets[2].content, "歷史內容");
    Check(repository.GetSetting("hotkey") == "right alt", "設定內容");
    Check(repository.GetSetting("autostart") == "false", "新版開機啟動不承接舊狀態");
    Expect<InvalidOperationException>(() => repository.ImportLegacy(legacyPath, settingsPath), "不得重複匯入");

    var first = repository.ListSnippets()[0];
    repository.SaveDraft(new SnippetDraft { SnippetId = first.Id, Title = "草稿", Content = "未正式儲存", CategoryId = first.CategoryId });
    Check(repository.GetSnippet(first.Id)!.Content == original.snippets[0].content, "草稿不改正式內容");
    repository.SaveSnippet(first with { Title = "已儲存", Content = "新正文" });
    Check(repository.GetDraft(first.Id) is null, "儲存後清除草稿");
    repository.DeleteSnippets([first.Id]);
    Check(repository.ListSnippets(includeDeleted: true).Count == 1, "垃圾桶");
    repository.RestoreSnippets([first.Id]);
    Check(repository.ListSnippets().Count == 2, "垃圾桶復原");
    var newSnippet = repository.SaveSnippet(new Snippet { Title = "新增", Content = "置頂" });
    Check(repository.ListSnippets()[0].Id == newSnippet.Id, "新增常用文字置頂");
    repository.AddHistory("新歷史");
    repository.AddHistory(original.snippets[2].content);
    Check(repository.ListHistory().Count == 2 && repository.ListHistory()[0].Content == original.snippets[2].content, "歷史去重置頂");
    repository.SetHistoryPaused(true);
    Check(repository.AddHistory("暫停資料") is null, "暫停記錄");

    var backupPath = Path.Combine(isolatedRoot, "verified.sqlite");
    repository.CreateBackup(backupPath);
    repository.ClearHistory();
    var safetyPath = repository.RestoreBackup(backupPath);
    Check(File.Exists(safetyPath) && repository.ListHistory().Count == 2, "備份還原與還原前保護");
    repository.SetHistoryPaused(false);
    for (var i = 0; i < 52; i++) repository.AddHistory($"歷史 {i}");
    Check(repository.ListHistory().Count == 50 && repository.ListHistory()[0].Content == "歷史 51", "歷史保留最近 50 筆");
    foreach (var repeated in new[] { "歷史 20", "歷史 49", "歷史 3" })
    {
        var expected = repository.ListHistory().Select(item => item.Content)
            .Where(content => content != repeated).Prepend(repeated).ToList();
        repository.AddHistory(repeated);
        Check(repository.ListHistory().Select(item => item.Content).SequenceEqual(expected),
              "重複複製後不應提早刪除其他歷史");
        Check(repository.ListHistory().Count == 50, "重複複製後仍保留 50 筆");
    }

    var reorderDirectory = Path.Combine(isolatedRoot, "reorder");
    var orderedRepository = new ClipboardRepository(reorderDirectory);
    orderedRepository.Initialize();
    var categoryA = orderedRepository.AddCategory("甲");
    var categoryB = orderedRepository.AddCategory("乙");
    var a1 = orderedRepository.SaveSnippet(new Snippet { Title = "甲 1", Content = "甲原文 1", CategoryId = categoryA.Id });
    var b1 = orderedRepository.SaveSnippet(new Snippet { Title = "乙 1", Content = "乙原文 1", CategoryId = categoryB.Id });
    var a2 = orderedRepository.SaveSnippet(new Snippet { Title = "甲 2", Content = "甲原文 2", CategoryId = categoryA.Id });
    var u1 = orderedRepository.SaveSnippet(new Snippet { Title = "未分類 1", Content = "未分類原文 1" });
    var b2 = orderedRepository.SaveSnippet(new Snippet { Title = "乙 2", Content = "乙原文 2", CategoryId = categoryB.Id });
    var a3 = orderedRepository.SaveSnippet(new Snippet { Title = "甲 3", Content = "甲原文 3", CategoryId = categoryA.Id });
    var u2 = orderedRepository.SaveSnippet(new Snippet { Title = "未分類 2", Content = "未分類原文 2" });
    var removed = orderedRepository.SaveSnippet(new Snippet { Title = "已刪除", Content = "垃圾桶內容", CategoryId = categoryA.Id });
    orderedRepository.DeleteSnippets([removed.Id]);
    var originalOrder = orderedRepository.ListSnippets().Select(s => s.Id).ToArray();
    var originalContents = orderedRepository.ListSnippets().ToDictionary(s => s.Id, s => (s.Title, s.Content, s.CategoryId));
    var targetOrder = new[] { a1.Id, a2.Id, a3.Id };
    orderedRepository.ReorderSnippets(categoryA.Id, targetOrder);
    Check(orderedRepository.ListSnippets(categoryA.Id).Select(s => s.Id).SequenceEqual(targetOrder), "分類內拖曳排序");
    Check(orderedRepository.ListSnippets().Select(s => s.Id).SequenceEqual(new[] { u2.Id, a1.Id, b2.Id, u1.Id, a2.Id, b1.Id, a3.Id }), "交錯分類原有槽位與其他分類順序");
    Check(orderedRepository.ListSnippets().Select(s => s.SortOrder).SequenceEqual(Enumerable.Range(0, 7)), "全體有效文字排序值連續且唯一");
    Check(orderedRepository.ListSnippets().All(s => originalContents[s.Id] == (s.Title, s.Content, s.CategoryId)), "排序不更動正文或分類");
    Check(orderedRepository.GetSnippet(removed.Id)!.IsDeleted, "排序不更動垃圾桶狀態");
    var afterValidOrder = orderedRepository.ListSnippets().Select(s => s.Id).ToArray();
    Expect<ArgumentException>(() => orderedRepository.ReorderSnippets(categoryA.Id, [a1.Id, a2.Id, b1.Id]), "不得混入其他分類 ID");
    Expect<ArgumentException>(() => orderedRepository.ReorderSnippets(categoryA.Id, [a1.Id, a2.Id, Guid.NewGuid()]), "不得混入不存在的 ID");
    Expect<ArgumentException>(() => orderedRepository.ReorderSnippets(categoryA.Id, [a1.Id, a1.Id, a3.Id]), "不得重複 ID");
    Expect<ArgumentException>(() => orderedRepository.ReorderSnippets(categoryA.Id, [a1.Id, a2.Id]), "不得遺漏 ID");
    Expect<ArgumentException>(() => orderedRepository.ReorderSnippets(categoryA.Id, [a1.Id, a2.Id, removed.Id]), "不得混入已刪除 ID");
    Check(orderedRepository.ListSnippets().Select(s => s.Id).SequenceEqual(afterValidOrder), "錯誤排序資料不改變既有順序");
    orderedRepository.ReorderSnippets(null, [u1.Id, u2.Id]);
    Check(orderedRepository.ListSnippets().Select(s => s.Id).SequenceEqual(new[] { u1.Id, a1.Id, b2.Id, u2.Id, a2.Id, b1.Id, a3.Id }), "未分類拖曳只替換未分類槽位");
    var reopened = new ClipboardRepository(reorderDirectory);
    Check(reopened.ListSnippets().Select(s => s.Id).SequenceEqual(orderedRepository.ListSnippets().Select(s => s.Id)), "重新開啟後排序仍保存");
    Check(!reopened.ListSnippets().Select(s => s.Id).SequenceEqual(originalOrder), "排序確實產生變更");

    var damagedPath = Path.Combine(isolatedRoot, "damaged.json");
    File.WriteAllText(damagedPath, "{\"categories\":[\"All\"],\"snippets\":[{}]}");
    var empty = new ClipboardRepository(Path.Combine(isolatedRoot, "empty"));
    Expect<InvalidDataException>(() => empty.ImportLegacy(damagedPath), "損壞匯入必須失敗");
    Check(!File.Exists(Path.Combine(isolatedRoot, "empty", "clipboard.db")), "損壞匯入不建立空白庫");

    var actualSources = new[]
    {
        (Data: Path.Combine(workspace, "backup", "pre-v2-20260926-215928", "live-data.json"),
         Settings: Path.Combine(workspace, "backup", "pre-v2-20260926-215928", "live-settings.json")),
        (Data: Path.Combine(workspace, "backup", "pre-v2-handoff-20260926-222646", "clipboard_data.json"),
         Settings: Path.Combine(workspace, "backup", "pre-v2-handoff-20260926-222646", "settings.json"))
    };
    foreach (var (actualBackup, actualSettings) in actualSources.Where(source => File.Exists(source.Data)))
    {
        var actualRepository = new ClipboardRepository(Path.Combine(isolatedRoot, "actual-" + Guid.NewGuid().ToString("N")));
        var actualResult = actualRepository.ImportLegacy(actualBackup, actualSettings);
        using var document = JsonDocument.Parse(File.ReadAllText(actualBackup));
        var sourceItems = document.RootElement.GetProperty("snippets").EnumerateArray().ToList();
        var sourceSnippets = sourceItems.Where(e => e.GetProperty("category").GetString() != "Copied").ToList();
        var sourceHistory = sourceItems.Where(e => e.GetProperty("category").GetString() == "Copied").ToList();
        Check(actualResult.SnippetCount == sourceSnippets.Count && actualResult.HistoryCount == sourceHistory.Count, "實際備份數量");
        Check(actualRepository.ListSnippets().Select(s => s.Content).SequenceEqual(sourceSnippets.Select(e => e.GetProperty("content").GetString()!)), "實際備份正文逐筆核對");
        Check(actualRepository.ListHistory().Select(s => s.Content).SequenceEqual(sourceHistory.Select(e => e.GetProperty("content").GetString()!)), "實際歷史逐筆核對");
    }

    var benchmarkPath = Path.Combine(isolatedRoot, "benchmark.json");
    var synthetic = new
    {
        categories = new[] { "All", "Copied", "測試" },
        snippets = Enumerable.Range(0, 5000).Select(i => new
        {
            emoji = "📌",
            title = $"測試 {i:D5}",
            category = "測試",
            content = $"第 {i:D5} 筆合成文字\n{new string('x', 300)}"
        }).ToArray()
    };
    File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(synthetic));
    var benchmarkRepository = new ClipboardRepository(Path.Combine(isolatedRoot, "benchmark"));
    var stopwatch = Stopwatch.StartNew();
    benchmarkRepository.ImportLegacy(benchmarkPath);
    stopwatch.Stop();
    var importMs = stopwatch.Elapsed.TotalMilliseconds;
    var listingSamples = new List<double>();
    var searchingSamples = new List<double>();
    for (var i = 0; i < 20; i++)
    {
        stopwatch.Restart();
        Check(benchmarkRepository.ListSnippets().Count == 5000, "效能資料完整性");
        stopwatch.Stop();
        listingSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        stopwatch.Restart();
        Check(benchmarkRepository.ListSnippets(query: "測試 04217").Count == 1, "效能搜尋結果");
        stopwatch.Stop();
        searchingSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
    }
    var benchmarkBackup = Path.Combine(isolatedRoot, "benchmark.sqlite");
    stopwatch.Restart();
    benchmarkRepository.CreateBackup(benchmarkBackup);
    stopwatch.Stop();
    Console.WriteLine($"5000 synthetic snippets: import={importMs:F1}ms, list p95={Percentile95(listingSamples):F1}ms, search p95={Percentile95(searchingSamples):F1}ms, backup={stopwatch.Elapsed.TotalMilliseconds:F1}ms, db={new FileInfo(Path.Combine(isolatedRoot, "benchmark", "clipboard.db")).Length / 1024.0 / 1024.0:F2}MiB");
    Console.WriteLine("ClipboardCore smoke checks passed.");
}
finally
{
    if (isolatedRoot.StartsWith(Path.Combine(workspace, "ClipboardCore.Tests", ".test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        Directory.Delete(isolatedRoot, recursive: true);
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"失敗：{label}");
}
static void Expect<T>(Action action, string label) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"未發生預期錯誤：{label}");
}
static double Percentile95(List<double> samples) => samples.Order().ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);
