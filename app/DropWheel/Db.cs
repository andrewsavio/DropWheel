using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace DropWheel;

/// <summary>Local SQLite store: settings, job history, trial counter. Schema is replica/schema.sql.</summary>
public static class Db
{
    static SqliteConnection _c = null!;
    static readonly object _lock = new();

    public static string DataDir { get; private set; } = "";

    public static void Open(string? dir = null)
    {
        DataDir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropWheel");
        Directory.CreateDirectory(DataDir);
        _c = new SqliteConnection($"Data Source={Path.Combine(DataDir, "dropwheel.db")}");
        _c.Open();
        if (Scalar("select count(*) from sqlite_master where name = 'jobs'") == 0)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("schema.sql")!;
            Exec(new StreamReader(s).ReadToEnd());
        }
        Exec("pragma foreign_keys = on");
    }

    static long Scalar(string sql, params (string, object)[] p)
    {
        lock (_lock)
        {
            using var cmd = Cmd(sql, p);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }
    }

    static void Exec(string sql, params (string, object)[] p)
    {
        lock (_lock) { using var cmd = Cmd(sql, p); cmd.ExecuteNonQuery(); }
    }

    static SqliteCommand Cmd(string sql, (string, object)[] p)
    {
        var cmd = _c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
        return cmd;
    }

    public static string Get(string key, string fallback)
    {
        lock (_lock)
        {
            using var cmd = Cmd("select value from settings where key = $k", new (string, object)[] { ("$k", key) });
            return cmd.ExecuteScalar() as string ?? fallback;
        }
    }

    public static void Set(string key, string value) =>
        Exec("insert into settings(key, value) values ($k, $v) on conflict(key) do update set value = $v",
             ("$k", key), ("$v", value));

    public static long NewBatch()
    {
        Exec("insert into batches default values");
        return Scalar("select last_insert_rowid()");
    }

    public static long NewJob(long batch, OpKind kind, string opId, string source, bool counted)
    {
        Exec("insert into jobs(batch_id, kind, op_id, source_path, counted) values ($b, $k, $o, $s, $c)",
             ("$b", batch), ("$k", kind.ToString().ToLowerInvariant()), ("$o", opId), ("$s", source), ("$c", counted ? 1 : 0));
        return Scalar("select last_insert_rowid()");
    }

    public static void FinishJob(long id, string status, string? output, string? error) =>
        Exec("update jobs set status = $s, output_path = $o, error = $e, finished_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') where id = $i",
             ("$s", status), ("$o", (object?)output ?? DBNull.Value), ("$e", (object?)error ?? DBNull.Value), ("$i", id));
}

public static class Settings
{
    static readonly (string name, int vk)[] Keys = { ("Shift", 0x10), ("Ctrl", 0x11), ("Alt", 0x12) };
    public static string[] KeyNames => Array.ConvertAll(Keys, k => k.name);

    static string Defaults(Mode m) => m switch { Mode.Convert => "Shift", Mode.Tools => "Ctrl", _ => "Alt" };
    public static string KeyName(Mode m) => Db.Get("modifier_" + m.ToString().ToLowerInvariant(), Defaults(m));
    public static void SetKeyName(Mode m, string name) => Db.Set("modifier_" + m.ToString().ToLowerInvariant(), name);

    // The mouse hook asks on every mouse move, so this must never touch the database.
    static readonly int[] Cached = new int[4];

    public static void Reload()
    {
        foreach (var m in new[] { Mode.Convert, Mode.Tools, Mode.Actions })
        {
            var n = KeyName(m);
            Cached[(int)m] = Keys.FirstOrDefault(k => k.name == n).vk;
        }
    }

    public static int KeyFor(Mode m) => Cached[(int)m];

    /// <summary>"beside" writes next to the source; any other value is a folder path.</summary>
    public static string OutputDir { get => Db.Get("output_dir", ""); set => Db.Set("output_dir", value); }
}
