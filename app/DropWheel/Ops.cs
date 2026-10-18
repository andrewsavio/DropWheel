using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DropWheel;

public enum OpKind { Convert, Tool, Action }

/// <summary>One thing the wheel can offer for a file. Run gets (source, destination, progress 0..1, cancel).</summary>
public sealed record Op(
    string Id, OpKind Kind, string Label, string Group, string Detail,
    Func<string, bool> Accepts, string OutExt,
    Func<string, string, Action<double>, CancellationToken, Task> Run,
    bool Available = true, string? Needs = null)
{
    public bool IsFolderOut { get; init; } // output is a directory of files (e.g. one image per PDF page)
    public bool Batch { get; init; }       // consumes all selected files at once (merge, zip); source arrives newline-joined
    public bool SameExt { get; init; }     // output keeps the source extension
    public int MinFiles { get; init; } = 1;
    public string Suffix { get; init; } = ""; // appended to the output name: "photo-compressed.jpg"
}

/// <summary>Finds bundled engines first (engines\ next to the exe), then PATH.</summary>
public static class Engines
{
    static readonly Dictionary<string, string?> Cache = new();

    public static string? Find(string exe)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(exe, out var hit)) return hit;
            string? found = null;
            var local = Path.Combine(AppContext.BaseDirectory, "engines");
            if (Directory.Exists(local))
                found = Directory.EnumerateFiles(local, exe + ".exe", SearchOption.AllDirectories).FirstOrDefault();
            found ??= (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => { try { return Path.Combine(d.Trim('"'), exe + ".exe"); } catch { return ""; } })
                .FirstOrDefault(File.Exists);
            if (found == null && exe == "soffice")
                found = new[] { @"C:\Program Files\LibreOffice\program\soffice.exe", @"C:\Program Files (x86)\LibreOffice\program\soffice.exe" }
                    .FirstOrDefault(File.Exists);
            return Cache[exe] = found;
        }
    }

    public static bool Has(string exe) => Find(exe) != null;

    public static void Refresh() { lock (Cache) Cache.Clear(); }

    /// <summary>Runs an engine, feeding each stderr/stdout line to onLine. Throws with the tail of its output on failure.</summary>
    public static async Task Run(string exe, IEnumerable<string> args, CancellationToken ct, Action<string>? onLine = null, string? workDir = null)
    {
        var path = Find(exe) ?? throw new InvalidOperationException($"{exe} is not installed");
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        if (workDir != null) psi.WorkingDirectory = workDir;
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var tail = new Queue<string>();
        void Line(string? s)
        {
            if (s == null) return;
            lock (tail) { tail.Enqueue(s); if (tail.Count > 8) tail.Dequeue(); }
            onLine?.Invoke(s);
        }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { } });
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{exe} failed ({p.ExitCode}): {string.Join(" | ", tail.Where(s => s.Length > 0))}");
    }
}

public static class Output
{
    static readonly object Gate = new();

    static string Dir(string source) =>
        Settings.OutputDir is { Length: > 0 } d && Directory.Exists(d) ? d : Path.GetDirectoryName(source.TrimEnd('\\', '/'))!;

    /// <summary>
    /// Beside the source (or in the chosen folder), never overwriting: "a.jpg", then "a (1).jpg".
    /// The name is reserved on disk before returning, so parallel jobs cannot pick the same one.
    /// </summary>
    public static string Reserve(Op op, string source)
    {
        var file = Path.GetFileName(source.TrimEnd('\\', '/'));
        var stem = (Directory.Exists(source) ? file
                  : Registry.Ext(source) == "tar.gz" ? file[..^7] // "a.tar.gz" is "a", not "a.tar"
                  : Path.GetFileNameWithoutExtension(file)) + op.Suffix;
        var ext = op.IsFolderOut ? "" : op.SameExt ? Registry.Ext(source) : op.OutExt;
        var dir = Dir(source);
        lock (Gate)
        {
            for (int i = 0; ; i++)
            {
                var name = (i == 0 ? stem : $"{stem} ({i})") + (ext.Length > 0 ? "." + ext : "");
                var path = Path.Combine(dir, name);
                if (File.Exists(path) || Directory.Exists(path)) continue;
                if (op.IsFolderOut) Directory.CreateDirectory(path);
                else using (File.Create(path)) { }
                return path;
            }
        }
    }
}
