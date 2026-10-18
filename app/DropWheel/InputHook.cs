using System;
using System.Runtime.InteropServices;

namespace DropWheel;

public enum Mode { None, Convert, Tools, Actions }

/// <summary>
/// Global left-button drag detector. Fires Triggered once per drag, the first time a trigger key
/// is held while a drag that started on Explorer or the desktop has moved past a small threshold.
/// A keyboard hook tracks the trigger keys too, so pressing Shift mid-drag opens the wheel at once,
/// without waiting for the mouse to move again.
/// The callback only records state and raises events; slow work happens elsewhere (a hook that
/// takes too long is silently removed by Windows).
/// </summary>
public sealed class InputHook : IDisposable
{
    const int Threshold = 8;
    static readonly string[] ShellClasses = { "CabinetWClass", "ExploreWClass", "Progman", "WorkerW" };

    readonly Native.HookProc _proc, _kproc; // kept in fields so the GC cannot collect the delegates
    IntPtr _hook, _khook;
    readonly System.Collections.Generic.HashSet<int> _held = new(); // generic VKs (Shift 0x10, Ctrl 0x11, Alt 0x12) seen down by the keyboard hook
    bool _down, _moved, _shown, _announced;
    int _n;
    POINT _start;
    IntPtr _root;

    public Func<Mode, int> KeyFor = _ => 0; // virtual-key per mode, from settings
    public bool Paused;
    public event Action<Mode, POINT, POINT, IntPtr>? Triggered; // mode, start, now, source root window
    public event Action<POINT, IntPtr>? DragStarted; // a drag began on Explorer or the desktop; no trigger key needed yet
    public event Action? Released;

    public InputHook() { _proc = Callback; _kproc = KeyCallback; }

    public void Start()
    {
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
        _khook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kproc, Native.GetModuleHandle(null), 0);
        Trace.Log($"hooks mouse={_hook != IntPtr.Zero} keyboard={_khook != IntPtr.Zero}");
    }

    /// <summary>Left/right modifier keys arrive as their own codes; settings store the generic one.</summary>
    static int Generic(int vk) => vk switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, _ => vk };

    IntPtr KeyCallback(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
            int vk = Generic((int)k.vkCode), msg = (int)w;
            if (msg is 0x100 or 0x104) // WM_KEYDOWN, WM_SYSKEYDOWN (auto-repeat arrives here too)
            {
                if (_held.Add(vk) && vk is 0x10 or 0x11 or 0x12) Trace.Log($"key down {vk:X2} dragging={_down && _moved && _root != IntPtr.Zero}");
                if (!Paused && _down && _moved && _root != IntPtr.Zero && !_shown)
                {
                    var mode = HeldMode();
                    if (mode != Mode.None && Native.GetCursorPos(out var now))
                    {
                        _shown = true; Trace.Log($"trigger {mode} (key)");
                        Triggered?.Invoke(mode, _start, now, _root);
                    }
                }
            }
            else if (msg is 0x101 or 0x105) _held.Remove(vk);
        }
        return Native.CallNextHookEx(_khook, code, w, l);
    }

    public bool Running => _hook != IntPtr.Zero;

    IntPtr Callback(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && !Paused)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
            switch ((int)w)
            {
                case Native.WM_LBUTTONDOWN:
                    _down = true; _moved = false; _shown = false; _announced = false; _start = ms.pt;
                    _held.RemoveWhere(k => !Native.KeyDown(k)); // a key-up missed (e.g. during a UAC prompt) must not stick; auto-repeat re-adds a key really held
                    _root = Native.GetAncestor(Native.WindowFromPoint(ms.pt), Native.GA_ROOT);
                    var cls = Native.ClassOf(_root);
                    if (Array.IndexOf(ShellClasses, cls) < 0) _root = IntPtr.Zero;
                    Trace.Log($"down at {ms.pt.X},{ms.pt.Y} root class '{cls}' armed={_root != IntPtr.Zero}");
                    break;
                case Native.WM_MOUSEMOVE when _down && _root != IntPtr.Zero && !_shown:
                    if (!_moved)
                        _moved = Math.Abs(ms.pt.X - _start.X) > Threshold || Math.Abs(ms.pt.Y - _start.Y) > Threshold;
                    if (_moved)
                    {
                        if (!_announced) { _announced = true; DragStarted?.Invoke(_start, _root); }
                        var mode = HeldMode();
                        if (++_n % 5 == 0) Trace.Log($"move {ms.pt.X},{ms.pt.Y} mode={mode} shift={_held.Contains(0x10)}/{Native.KeyDown(0x10)} ctrl={Native.KeyDown(0x11)} vk={KeyFor(Mode.Convert)}");
                        if (mode != Mode.None) { _shown = true; Trace.Log($"trigger {mode}"); Triggered?.Invoke(mode, _start, ms.pt, _root); }
                    }
                    break;
                case Native.WM_LBUTTONUP when _down:
                    _down = false;
                    if (_shown) { _shown = false; Released?.Invoke(); }
                    break;
            }
        }
        return Native.CallNextHookEx(_hook, code, w, l);
    }

    Mode HeldMode()
    {
        foreach (var m in new[] { Mode.Convert, Mode.Tools, Mode.Actions })
        {
            var vk = KeyFor(m);
            if (vk != 0 && (_held.Contains(vk) || Native.KeyDown(vk))) return m;
        }
        return Mode.None;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (_khook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_khook); _khook = IntPtr.Zero; }
    }
}
