using System.Runtime.InteropServices;
using System.Text;

namespace ClipboardCore;

internal sealed class SqliteDb : IDisposable
{
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private IntPtr _db;

    public SqliteDb(string path, bool readOnly = false)
    {
        var flags = readOnly ? 0x00000001 : 0x00000002 | 0x00000004;
        var result = Native.sqlite3_open_v2(Utf8(path), out _db, flags, IntPtr.Zero);
        if (result != Ok)
        {
            var message = Error();
            Dispose();
            throw new IOException($"無法開啟資料庫：{message}");
        }
        try
        {
            Execute("PRAGMA busy_timeout=5000");
            if (!readOnly)
            {
                Execute("PRAGMA foreign_keys=ON");
                Execute("PRAGMA journal_mode=WAL");
                Execute("PRAGMA synchronous=FULL");
            }
        }
        catch { Dispose(); throw; }
    }

    public void Execute(string sql, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        var result = Native.sqlite3_step(statement.Handle);
        if (result != Done && result != Row) throw new IOException($"SQLite 寫入失敗：{Error()}");
    }

    public List<T> Query<T>(string sql, Func<RowReader, T> read, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        var rows = new List<T>();
        while (true)
        {
            var result = Native.sqlite3_step(statement.Handle);
            if (result == Done) return rows;
            if (result != Row) throw new IOException($"SQLite 讀取失敗：{Error()}");
            rows.Add(read(new RowReader(statement.Handle)));
        }
    }

    public T? Scalar<T>(string sql, Func<RowReader, T> read, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        var result = Native.sqlite3_step(statement.Handle);
        if (result == Done) return default;
        if (result != Row) throw new IOException($"SQLite 讀取失敗：{Error()}");
        return read(new RowReader(statement.Handle));
    }

    public void Transaction(Action action)
    {
        Execute("BEGIN IMMEDIATE");
        try
        {
            action();
            Execute("COMMIT");
        }
        catch
        {
            try { Execute("ROLLBACK"); } catch { /* Preserve original exception. */ }
            throw;
        }
    }

    public void BackupTo(SqliteDb destination)
    {
        var backup = Native.sqlite3_backup_init(destination._db, Utf8("main"), _db, Utf8("main"));
        if (backup == IntPtr.Zero) throw new IOException($"SQLite 備份啟動失敗：{destination.Error()}");
        try
        {
            var result = Native.sqlite3_backup_step(backup, -1);
            if (result != Done) throw new IOException($"SQLite 備份失敗：{destination.Error()}");
        }
        finally
        {
            var result = Native.sqlite3_backup_finish(backup);
            if (result != Ok) throw new IOException($"SQLite 備份完成失敗：{destination.Error()}");
        }
    }

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    private Statement Prepare(string sql, object?[] args)
    {
        var result = Native.sqlite3_prepare_v2(_db, Utf8(sql), -1, out var handle, IntPtr.Zero);
        if (result != Ok) throw new IOException($"SQLite 指令失敗：{Error()}");
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                result = args[i] switch
                {
                    null => Native.sqlite3_bind_null(handle, i + 1),
                    bool value => Native.sqlite3_bind_int64(handle, i + 1, value ? 1 : 0),
                    int value => Native.sqlite3_bind_int64(handle, i + 1, value),
                    long value => Native.sqlite3_bind_int64(handle, i + 1, value),
                    Guid value => BindText(handle, i + 1, value.ToString("D")),
                    DateTimeOffset value => BindText(handle, i + 1, value.ToUniversalTime().ToString("O")),
                    string value => BindText(handle, i + 1, value),
                    _ => throw new ArgumentException($"不支援的 SQLite 參數型別：{args[i]!.GetType().Name}")
                };
                if (result != Ok) throw new IOException($"SQLite 參數失敗：{Error()}");
            }
            return new Statement(handle);
        }
        catch
        {
            Native.sqlite3_finalize(handle);
            throw;
        }
    }

    private string Error() => _db == IntPtr.Zero ? "無法取得錯誤資訊" : Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)) ?? "未知錯誤";
    private static int BindText(IntPtr statement, int index, string value)
    {
        var bytes = Utf8(value);
        return Native.sqlite3_bind_text(statement, index, bytes, bytes.Length - 1, new IntPtr(-1));
    }
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

    private sealed class Statement(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => Native.sqlite3_finalize(Handle);
    }

    internal readonly struct RowReader(IntPtr statement)
    {
        public string? Text(int column)
        {
            if (Native.sqlite3_column_type(statement, column) == 5) return null;
            var pointer = Native.sqlite3_column_text(statement, column);
            var count = Native.sqlite3_column_bytes(statement, column);
            if (count == 0) return "";
            var bytes = new byte[count];
            Marshal.Copy(pointer, bytes, 0, count);
            return Encoding.UTF8.GetString(bytes);
        }
        public long Number(int column) => Native.sqlite3_column_int64(statement, column);
        public bool IsNull(int column) => Native.sqlite3_column_type(statement, column) == 5;
        public Guid Guid(int column) => System.Guid.Parse(Text(column)!);
        public Guid? NullableGuid(int column) => IsNull(column) ? null : System.Guid.Parse(Text(column)!);
    }

    private static class Native
    {
        private const string Library = "winsqlite3.dll";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_null(IntPtr statement, int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_type(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern long sqlite3_column_int64(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_backup_init(IntPtr destination, byte[] destinationName, IntPtr source, byte[] sourceName);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_backup_step(IntPtr backup, int pages);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_backup_finish(IntPtr backup);
    }
}
