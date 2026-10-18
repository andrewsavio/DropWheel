using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DropWheel;

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

public static class Native
{
    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_HOTKEY = 0x312;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    public const uint GA_ROOT = 2;

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint tid);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Device-independent pixels per physical pixel for the monitor under <paramref name="p"/>.</summary>
    public static double DipScale(POINT p)
    {
        var mon = MonitorFromPoint(p, 2);
        return GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0 ? 96.0 / dx : 1.0;
    }

    public static void MakeNoActivate(IntPtr hwnd) =>
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
}

/// <summary>Set DROPWHEEL_TRACE=1 to append a trace of the drag pipeline to %LOCALAPPDATA%\DropWheel\trace.log.</summary>
public static class Trace
{
    static readonly bool On = Environment.GetEnvironmentVariable("DROPWHEEL_TRACE") == "1";
    static readonly object Gate = new();

    public static void Log(string message)
    {
        if (!On) return;
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropWheel");
            System.IO.Directory.CreateDirectory(dir);
            lock (Gate) System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
}
