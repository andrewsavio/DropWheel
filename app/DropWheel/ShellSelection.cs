using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace DropWheel;

/// <summary>
/// Which files is the user dragging (or has selected)? The drag payload is not readable before the
/// drop, so the selection of the source Explorer window is read instead. Explorer windows come from
/// Shell.Application; the desktop is not in that list, so desktop icons come from UI Automation.
/// All COM/UIA work runs on its own STA thread, never inside the hook callback.
/// </summary>
public static class ShellSelection
{
    public static Task<List<string>> ForDragAsync(IntPtr root, POINT start) => RunSta(() =>
    {
        var (found, name, el) = HitItem(start);
        if (found && name == null) return new List<string>(); // pressed on empty space: a marquee, not a file drag
        return SelectionOf(root, name, el);
    });

    public static Task<List<string>> ForForegroundAsync() => RunSta(() =>
    {
        var root = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
        AutomationElement? focused = null;
        try { focused = AutomationElement.FocusedElement; } catch { }
        return SelectionOf(root, null, focused);
    });

    static List<string> SelectionOf(IntPtr root, string? hitName, AutomationElement? el)
    {
        var cls = Native.ClassOf(root);
        return cls is "Progman" or "WorkerW" ? DesktopSelection(el) : ExplorerSelection(root, hitName);
    }

    static List<string> ExplorerSelection(IntPtr root, string? hitName)
    {
        var result = new List<string>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            List<string>? fallback = null;
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    if (Convert.ToInt64(w.HWND) != root.ToInt64()) continue;
                    var items = new List<(string path, string name)>();
                    foreach (dynamic it in w.Document.SelectedItems())
                    {
                        string p = it.Path;
                        if (File.Exists(p) || Directory.Exists(p)) items.Add((p, (string)it.Name));
                    }
                    if (items.Count == 0) continue;
                    var paths = items.Select(i => i.path).ToList();
                    // Explorer tabs share one top window; the tab whose selection holds the pressed item is the source.
                    if (hitName == null || items.Any(i => string.Equals(i.name, hitName, StringComparison.OrdinalIgnoreCase)))
                        return paths;
                    fallback ??= paths;
                }
                catch { }
            }
            if (fallback != null) result = fallback;
        }
        catch { }
        return result;
    }

    static List<string> DesktopSelection(AutomationElement? el)
    {
        var names = new List<string>();
        try
        {
            for (int i = 0; el != null && i < 5; i++, el = TreeWalker.ControlViewWalker.GetParent(el))
            {
                if (el.TryGetCurrentPattern(SelectionPattern.Pattern, out var pat))
                {
                    names = ((SelectionPattern)pat).Current.GetSelection().Select(e => e.Current.Name).ToList();
                    break;
                }
            }
        }
        catch { }
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var found = new List<string>();
        foreach (var n in names)
            foreach (var d in dirs)
            {
                var hit = Directory.EnumerateFileSystemEntries(d)
                    .FirstOrDefault(p => string.Equals(Path.GetFileName(p), n, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(Path.GetFileNameWithoutExtension(p), n, StringComparison.OrdinalIgnoreCase));
                if (hit != null) { found.Add(hit); break; }
            }
        return found;
    }

    /// <summary>found=false means UI Automation failed, so the caller should not reject the drag.</summary>
    static (bool found, string? name, AutomationElement? el) HitItem(POINT p)
    {
        try
        {
            var el = AutomationElement.FromPoint(new System.Windows.Point(p.X, p.Y));
            var cur = el;
            for (int i = 0; cur != null && i < 4; i++, cur = TreeWalker.ControlViewWalker.GetParent(cur))
            {
                var t = cur.Current.ControlType;
                if (t == ControlType.ListItem || t == ControlType.DataItem) return (true, cur.Current.Name, cur);
            }
            return (true, null, el);
        }
        catch { return (false, null, null); }
    }

    static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); } catch (Exception e) { tcs.SetException(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }
}
