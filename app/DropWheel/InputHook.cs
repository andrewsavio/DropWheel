using System;
using System.Runtime.InteropServices;

namespace DropWheel;

public enum Mode { None, Convert, Tools, Actions }

/// <summary>
/// Global left-button drag detector. Fires Triggered once per drag, the first time a trigger key
/// is held while a drag that started on Explorer or the desktop has moved past a small threshold.
/// The callback only records state and raises events; slow work happens elsewhere (a hook that
/// takes too long is silently removed by Windows).
/// </summary>
public sealed class InputHook : IDisposable
{
    const int Threshold = 8;
    static readonly string[] ShellClasses = { "CabinetWClass", "ExploreWClass", "Progman", "WorkerW" };

    readonly Native.HookProc _proc; // kept in a field so the GC cannot collect the delegate
    IntPtr _hook;
    bool _down, _moved, _shown, _announced;
    int _n;
    POINT _start;
    IntPtr _root;

    public Func<Mode, int> KeyFor = _ => 0; // virtual-key per mode, from settings
    public bool Paused;
    public event Action<Mode, POINT, POINT, IntPtr>? Triggered; // mode, start, now, source root window
    public event Action<POINT, IntPtr>? DragStarted; // a drag began on Explorer or the desktop; no trigger key needed yet
    public event Action? Released;

    public InputHook() { _proc = Callback; }

    public void Start() =>
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);

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
                        if (++_n % 5 == 0) Trace.Log($"move {ms.pt.X},{ms.pt.Y} mode={mode} shift={Native.KeyDown(0x10)} ctrl={Native.KeyDown(0x11)} vk={KeyFor(Mode.Convert)}");
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
            if (vk != 0 && Native.KeyDown(vk)) return m;
        }
        return Mode.None;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
