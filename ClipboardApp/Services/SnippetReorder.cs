namespace ClipboardApp.Services;

internal static class SnippetReorder
{
    public static List<Guid>? MoveRelativeToTarget(IReadOnlyList<Guid> current, Guid moving, Guid target)
    {
        var from = current.IndexOf(moving);
        var to = current.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return null;

        var next = current.ToList();
        next.RemoveAt(from);
        var targetIndex = next.IndexOf(target);
        next.Insert(from < to ? targetIndex + 1 : targetIndex, moving);
        return next;
    }

    private static int IndexOf(this IReadOnlyList<Guid> items, Guid id)
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i] == id) return i;
        return -1;
    }
}
