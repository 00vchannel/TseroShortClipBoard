namespace ClipboardApp.Services;

// Shipped with the app, never seeded into the user's database. Keep IDs stable across releases.
internal static class OfficialPrompts
{
    public const string CategoryName = "官方提示語";
    public static readonly Guid CategoryId = new("bf33aeac-9358-4f61-a91d-302f38453d00");

    public static IReadOnlyList<SnippetView> Items { get; } = Array.AsReadOnly(new[]
    {
        Item("bf33aeac-9358-4f61-a91d-302f38453d01", "和 AI 一起討論計劃", 0,
            "@READMEGameIntro\n先不要給我程式碼,我想要先討論和計劃一下如何以AI生成代碼為前提如何最好地實現這個功能。避免過度設計,並建議採用更專業的方式來設計和實現這個功能。然後像我12歲一樣來解釋它。另外你要事先分析和閱讀腳本或檔案讓你更好地理解遊戲的運作以更精準和專業的方式來建立這個功能。\n\n關於我的要求如果你實際上有什麼地方是含糊或不是很清楚，我命令你要主動問我問題來完善整個計畫，確保這是最專業的產品級計畫書。"),
        Item("bf33aeac-9358-4f61-a91d-302f38453d02", "要求 AI 主動問你問題來完善計劃", 1,
            "關於我的要求如果你實際上有什麼地方是含糊或不是很清楚，我命令你要主動問我問題來完善整個計畫，確保這是最專業的產品級計畫書。"),
        Item("bf33aeac-9358-4f61-a91d-302f38453d03", "叫 AI 使用 Multi-agent 和建立Goal來實行計劃", 2,
            "你現在可以實行計劃，並考慮使用Multi-Agent 來最大程度提升效率來完成並且把計劃設為 Goal。"),
        Item("bf33aeac-9358-4f61-a91d-302f38453d04", "為專案建立 README", 3,
            "建立一個名為 README().md 的新檔案，說明新建立的功能如何運作，列出此功能的核心腳本，並記錄 AI 在開始新對話時應知道的重要資訊。請使用繁體中文撰寫。"),
        Item("bf33aeac-9358-4f61-a91d-302f38453d05", "為專案更新 README", 4,
            "現在請更新 README 檔案，以反映腳本的變更，並移除已被取代的過時功能。重點是協助 AI 在新的聊天工作階段中理解新功能，同時確保內容足夠詳盡涵蓋重要資訊。")
    });

    public static bool IsOfficial(Guid? id) => id.HasValue &&
        (id.Value == CategoryId || Items.Any(item => item.Id == id.Value));

    public static void RequirePersonal(params Guid?[] ids)
    {
        if (ids.Any(IsOfficial)) throw new InvalidOperationException("官方提示語為內建唯讀內容，無法修改。");
    }

    public static Guid[] RequirePersonal(IEnumerable<Guid> ids)
    {
        var result = ids.ToArray();
        foreach (var id in result) RequirePersonal((Guid?)id);
        return result;
    }

    private static SnippetView Item(string id, string title, int order, string content) =>
        new(new Guid(id), title, "", content, CategoryId, order, DateTimeOffset.MinValue);
}
