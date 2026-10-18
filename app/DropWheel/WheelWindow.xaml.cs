using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DropWheel;

/// <summary>
/// The radial menu. Shown under the cursor during a drag (never takes focus, or the drag would cancel),
/// or in the middle of the screen from the keyboard hotkey (takes focus, arrows + Enter).
/// Each slot is a drop target; more than 12 options collapse into groups that open on a short hover.
/// </summary>
public partial class WheelWindow : Window
{
    const double Size = 420, Radius = 134, SlotW = 74, SlotH = 56;
    const int MaxSlots = 12;

    readonly IReadOnlyList<string> _paths;
    readonly bool _keyboard;
    Mode _mode;
    string? _group;
    DispatcherTimer? _dwell;

    public bool Dropped { get; private set; }
    public POINT? Anchor { get; init; }

    public WheelWindow(Mode mode, IReadOnlyList<string> paths, bool keyboard)
    {
        InitializeComponent();
        _paths = paths; _mode = mode; _keyboard = keyboard;
        SourceInitialized += (_, _) => PlaceWindow();
        Loaded += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation)
            {
                var a = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            if (_keyboard) { Activate(); (Ring.Children.OfType<UIElement>().FirstOrDefault() ?? Hole).Focus(); }
        };
        Hole.DragEnter += (_, e) => { e.Effects = DragDropEffects.Copy; if (_group != null) Dwell(Back); e.Handled = true; };
        Hole.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        Hole.DragLeave += (_, _) => StopDwell();
        Hole.MouseLeftButtonUp += (_, _) => { if (_group != null) Back(); else Close(); };
        Hole.Drop += (_, e) => { e.Handled = true; if (_group != null) Back(); };
        Build();
        if (_keyboard)
        {
            ShowActivated = true;
            Hint.Text = "1 Convert  ·  2 Tools  ·  3 Actions  ·  Esc close";
            PreviewKeyDown += OnKey;
        }
    }

    static OpKind KindOf(Mode m) => m switch { Mode.Tools => OpKind.Tool, Mode.Actions => OpKind.Action, _ => OpKind.Convert };
    Brush Res(string key) => (Brush)FindResource(key);

    void OnKey(object s, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.D1 or Key.NumPad1: SwitchTo(Mode.Convert); break;
            case Key.D2 or Key.NumPad2: SwitchTo(Mode.Tools); break;
            case Key.D3 or Key.NumPad3: SwitchTo(Mode.Actions); break;
            default: return;
        }
        e.Handled = true;
    }

    void SwitchTo(Mode m) { _mode = m; _group = null; Build(); (Ring.Children.OfType<UIElement>().FirstOrDefault() ?? Hole).Focus(); }

    void Back() { StopDwell(); _group = null; Build(); }

    void Build()
    {
        Ring.Children.Clear();
        var ops = Registry.For(_paths, KindOf(_mode));
        HoleTitle.Text = _paths.Count == 1 ? Path.GetFileName(_paths[0].TrimEnd('\\', '/')) : $"{_paths.Count} files";
        HoleSub.Text = _group ?? _mode.ToString();

        var entries = new List<(string label, string tip, Action<IReadOnlyList<string>> run, bool isGroup)>();
        if (_group != null)
            ops = ops.Where(o => o.Group == _group).ToList();
        if (_group == null && ops.Count > MaxSlots)
            foreach (var g in ops.GroupBy(o => o.Group))
            {
                var name = g.Key;
                entries.Add((name, $"{g.Count()} options", _ => { _group = name; Build(); }, true));
            }
        else
            foreach (var o in ops)
            {
                var op = o;
                entries.Add((op.Label, op.Detail, files => Choose(op, files), false));
            }

        if (entries.Count == 0) { HoleSub.Text = "Nothing for this file"; return; }

        for (int i = 0; i < entries.Count; i++)
        {
            var (label, tip, run, isGroup) = entries[i];
            var slot = MakeSlot(label, tip, run, isGroup);
            double ang = i * 2 * Math.PI / entries.Count - Math.PI / 2, c = Ring.Width / 2;
            Canvas.SetLeft(slot, c + Math.Cos(ang) * Radius - SlotW / 2);
            Canvas.SetTop(slot, c + Math.Sin(ang) * Radius - SlotH / 2);
            Ring.Children.Add(slot);
        }
    }

    Border MakeSlot(string label, string tip, Action<IReadOnlyList<string>> run, bool isGroup)
    {
        var text = new TextBlock
        {
            Text = label, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0),
        };
        var slot = new Border
        {
            Width = SlotW, Height = SlotH, CornerRadius = (CornerRadius)FindResource("RadiusMd"), BorderThickness = new Thickness(1),
            Background = Res("OverlaySurface"), BorderBrush = Res("OverlayBorder"), Child = text,
            AllowDrop = true, Focusable = true, ToolTip = tip, FocusVisualStyle = null, Cursor = Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetName(slot, tip.Length > 0 ? $"{label}: {tip}" : label);
        void Hover(bool on)
        {
            slot.Background = Res(on ? "OverlayAccent" : "OverlaySurface");
            slot.BorderBrush = Res(on ? "OverlayAccent" : "OverlayBorder");
            text.Foreground = Res(on ? "OnOverlayAccent" : "OverlayText");
        }
        Hover(false);
        slot.MouseEnter += (_, _) => Hover(true);
        slot.MouseLeave += (_, _) => { if (!slot.IsKeyboardFocused) Hover(false); };
        slot.GotKeyboardFocus += (_, _) => Hover(true);
        slot.LostKeyboardFocus += (_, _) => Hover(false);
        slot.DragEnter += (_, e) => { e.Effects = DragDropEffects.Copy; Hover(true); if (isGroup) Dwell(() => run(_paths)); e.Handled = true; };
        slot.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        slot.DragLeave += (_, _) => { Hover(false); StopDwell(); };
        slot.Drop += (_, e) =>
        {
            e.Handled = true; StopDwell();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            run(files is { Length: > 0 } ? files : _paths);
        };
        slot.MouseLeftButtonUp += (_, _) => run(_paths);
        slot.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { run(_paths); e.Handled = true; } };
        return slot;
    }

    void Choose(Op op, IReadOnlyList<string> files)
    {
        Dropped = true;
        Close();
        JobRunner.Start(op, files);
    }

    void Dwell(Action then)
    {
        StopDwell();
        _dwell = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _dwell.Tick += (_, _) => { StopDwell(); then(); };
        _dwell.Start();
    }

    void StopDwell() { _dwell?.Stop(); _dwell = null; }

    /// <summary>Position in physical pixels, so it is right on mixed-DPI monitors. Clamped to the monitor's work area.</summary>
    void PlaceWindow()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (!_keyboard) Native.MakeNoActivate(h);
        Native.GetCursorPos(out var cur);
        var at = Anchor ?? cur;
        double px = Size / Native.DipScale(at);
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(at.X, at.Y)).WorkingArea;
        // keyboard mode centres on the monitor; a drag opens under the cursor
        double cx = _keyboard ? wa.Left + wa.Width / 2.0 : at.X, cy = _keyboard ? wa.Top + wa.Height / 2.0 : at.Y;
        int x = (int)Math.Clamp(cx - px / 2, wa.Left, Math.Max(wa.Left, wa.Right - px));
        int y = (int)Math.Clamp(cy - px / 2, wa.Top, Math.Max(wa.Top, wa.Bottom - px));
        Native.SetWindowPos(h, new IntPtr(-1), x, y, (int)px, (int)px, 0x10 /* SWP_NOACTIVATE */);
    }
}
