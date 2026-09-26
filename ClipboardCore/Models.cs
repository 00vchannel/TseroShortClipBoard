namespace ClipboardCore;

public sealed record Category
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
}

public sealed record Snippet
{
    public Guid Id { get; init; }
    public string Emoji { get; init; } = "";
    public string Title { get; init; } = "";
    public Guid? CategoryId { get; init; }
    public string Content { get; init; } = "";
    public int SortOrder { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed record HistoryEntry
{
    public Guid Id { get; init; }
    public string Content { get; init; } = "";
    public DateTimeOffset CopiedAtUtc { get; init; }
}

public sealed record SnippetDraft
{
    public Guid SnippetId { get; init; }
    public string Emoji { get; init; } = "";
    public string Title { get; init; } = "";
    public Guid? CategoryId { get; init; }
    public string Content { get; init; } = "";
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record ImportResult(int SnippetCount, int CategoryCount, int HistoryCount);
