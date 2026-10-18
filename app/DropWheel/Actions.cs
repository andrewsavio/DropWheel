using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DropWheel;

/// <summary>
/// The Actions wheel: things done to a file rather than a conversion. None of them use the trial allowance.
/// Each receives every selected path, newline-joined, because they act on the selection as a whole.
/// </summary>
public static class Actions
{
    static Op A(string id, string label, string detail, Func<string[], Task> run) =>
        new(id, OpKind.Action, label, "Action", detail, _ => true, "",
            (s, _, _, _) => run(s.Split('\n'))) { Batch = true };

    public static IEnumerable<Op> All()
    {
        yield return A("a.copypath", "Copy path", "Copy full path to the clipboard", paths =>
        {
            Ui(() => Clipboard.SetText(string.Join(Environment.NewLine, paths)));
            return Task.CompletedTask;
        });
        yield return A("a.copy", "Copy to…", "Copy into a folder you pick", paths => Transfer(paths, move: false));
        yield return A("a.move", "Move to…", "Move into a folder you pick", paths => Transfer(paths, move: true));
        yield return A("a.terminal", "Terminal", "Open a terminal in its folder", paths =>
        {
            var dir = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0])!;
            var psi = Engines.Find("wt") is { } wt
                ? new ProcessStartInfo(wt) { ArgumentList = { "-d", dir } }
                : new ProcessStartInfo("cmd.exe") { ArgumentList = { "/k" }, WorkingDirectory = dir };
            psi.UseShellExecute = false;
            Process.Start(psi);
            return Task.CompletedTask;
        });
        yield return A("a.reveal", "Show in folder", "Select it in Explorer", paths =>
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + paths[0] }, UseShellExecute = false });
            return Task.CompletedTask;
        });
        yield return A("a.float", "Float", "Keep a tile on top of other windows", paths =>
        {
            Ui(() => { foreach (var p in paths) new FloatWindow(p).Show(); });
            return Task.CompletedTask;
        });
        // ponytail: no AirDrop equivalent. Windows Share (Nearby Share) needs a packaged-app helper; add when the installer is MSIX.
    }

    static void Ui(Action a) => Application.Current.Dispatcher.Invoke(a);

    static Task Transfer(string[] paths, bool move) => Task.Run(() =>
    {
        string? target = null;
        Ui(() =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = move ? "Move to folder" : "Copy to folder",
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) target = dlg.SelectedPath;
        });
        if (target == null) return;
        foreach (var p in paths)
        {
            var dst = Unique(target, Path.GetFileName(p.TrimEnd('\\', '/')));
            if (Directory.Exists(p))
            {
                if (move) { try { Directory.Move(p, dst); continue; } catch (IOException) { /* across drives */ } }
                CopyDir(p, dst);
                if (move) Directory.Delete(p, true);
            }
            else if (move) File.Move(p, dst);
            else File.Copy(p, dst);
        }
    });

    static string Unique(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        for (int i = 1; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        return path;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
