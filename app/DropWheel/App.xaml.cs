using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace DropWheel;

public partial class App : Application
{
    Mutex? _single;
    InputHook? _hook;
    System.Windows.Forms.NotifyIcon? _tray;
    HwndSource? _msgWindow;
    ToastWindow? _toast;
    WheelWindow? _wheel;
    SettingsWindow? _settings;
    bool _dragging;
    Task<List<string>>? _pending; // read as soon as a drag starts, so the wheel is ready when the key is pressed

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--selftest"))
        {
            Shutdown(await SelfTest.Run(e.Args));
            return;
        }

        _single = new Mutex(true, "DropWheel.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }

        Db.Open();
        Settings.Reload();
        _toast = new ToastWindow();
        JobRunner.LimitReached += () => Dispatcher.BeginInvoke(() => new PaywallWindow().Show());

        _hook = new InputHook { KeyFor = Settings.KeyFor };
        _hook.DragStarted += (start, root) => _pending = ShellSelection.ForDragAsync(root, start);
        _hook.Triggered += OnDragTrigger;
        _hook.Released += OnDragRelease;
        _hook.Start();

        RegisterHotkey();
        BuildTray();
        if (!_hook.Running) Fail("Could not start the drag hook. Use the keyboard hotkey (Ctrl+Alt+T) instead.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        if (_msgWindow != null) Native.UnregisterHotKey(_msgWindow.Handle, 1);
        base.OnExit(e);
    }

    // ---------- drag trigger

    async void OnDragTrigger(Mode mode, POINT start, POINT now, IntPtr root)
    {
        _dragging = true;
        List<string> paths;
        try { paths = await (_pending ?? ShellSelection.ForDragAsync(root, start)); } catch { paths = new(); }
        Trace.Log($"paths {paths.Count}, still dragging={_dragging}");
        if (!_dragging || paths.Count == 0) return; // released meanwhile, or a marquee on empty space
        CloseWheel();
        var w = new WheelWindow(mode, paths, keyboard: false);
        w.Closed += (_, _) => { if (_wheel == w) _wheel = null; };
        _wheel = w;
        w.Show();
    }

    void OnDragRelease()
    {
        _dragging = false;
        var w = _wheel;
        if (w == null) return;
        // The drop is delivered just after the button-up the hook sees; give it time before treating this as a cancel.
        Dispatcher.InvokeAsync(async () => { await Task.Delay(400); if (!w.Dropped) CloseWheel(w); });
    }

    void CloseWheel(WheelWindow? w = null)
    {
        w ??= _wheel;
        try { w?.Close(); } catch (InvalidOperationException) { /* already closing */ }
    }

    // ---------- keyboard trigger (Ctrl+Alt+T)

    void RegisterHotkey()
    {
        _msgWindow = new HwndSource(new HwndSourceParameters("DropWheelMessages") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _msgWindow.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == Native.WM_HOTKEY) { handled = true; OnHotkey(); }
            return IntPtr.Zero;
        });
        const uint Alt = 1, Ctrl = 2, NoRepeat = 0x4000;
        if (!Native.RegisterHotKey(_msgWindow.Handle, 1, Ctrl | Alt | NoRepeat, 0x54 /* T */))
            Fail("Ctrl+Alt+T is already used by another app, so the keyboard shortcut is off.");
    }

    async void OnHotkey()
    {
        List<string> paths;
        try { paths = await ShellSelection.ForForegroundAsync(); } catch { paths = new(); }
        if (paths.Count == 0) { Fail("Select a file in Explorer first, then press Ctrl+Alt+T."); return; }
        CloseWheel();
        var w = new WheelWindow(Mode.Convert, paths, keyboard: true);
        w.Closed += (_, _) => { if (_wheel == w) _wheel = null; };
        _wheel = w;
        w.Show();
    }

    // ---------- tray

    void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        var pause = new System.Windows.Forms.ToolStripMenuItem("Pause") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => { if (_hook != null) _hook.Paused = pause.Checked; _tray!.Text = pause.Checked ? "DropWheel (paused)" : "DropWheel"; };
        menu.Items.Add(pause);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());
        _tray = new System.Windows.Forms.NotifyIcon { Icon = MakeIcon(), Text = "DropWheel", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, ev) => { if (ev.Button == System.Windows.Forms.MouseButtons.Left) ShowSettings(); };
    }

    void ShowSettings()
    {
        if (_settings is { IsLoaded: true }) { _settings.Activate(); return; }
        _settings = new SettingsWindow();
        _settings.Show();
    }

    /// <summary>A ring of dots in the accent colour: the wheel in miniature.</summary>
    Icon MakeIcon()
    {
        var accent = (System.Windows.Media.Color)FindResource("AccentColor");
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(accent.R, accent.G, accent.B))) g.FillEllipse(b, 1, 1, 30, 30);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3 - Math.PI / 2;
                g.FillEllipse(Brushes.White, (float)(16 + Math.Cos(a) * 9 - 3), (float)(16 + Math.Sin(a) * 9 - 3), 6, 6);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    void Fail(string message)
    {
        var vm = new JobVM { Title = "DropWheel", Message = message, State = JobState.Failed };
        JobRunner.Jobs.Add(vm);
        JobRunner.RaiseChanged();
    }
}
