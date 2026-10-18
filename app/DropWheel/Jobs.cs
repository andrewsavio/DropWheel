using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DropWheel;

public enum JobState { Queued, Running, Done, Failed, Cancelled }

/// <summary>What a toast row shows. Properties change from worker threads, so setters raise events on the UI thread.</summary>
public sealed class JobVM : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title { get; init; } = "";
    public string? Output { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    JobState _state; double _progress = -1; string _message = "Waiting";
    public JobState State { get => _state; set { _state = value; Raise(nameof(State), nameof(CanCancel), nameof(CanShow), nameof(IsFailed)); } }
    public double Progress { get => _progress; set { _progress = value; Raise(nameof(Progress), nameof(Indeterminate)); } }
    public string Message { get => _message; set { _message = value; Raise(nameof(Message)); } }
    public bool CanCancel => State is JobState.Queued or JobState.Running;
    public bool CanShow => State == JobState.Done && Output != null;
    public bool IsFailed => State == JobState.Failed;
    public bool Indeterminate => State == JobState.Running && Progress < 0;

    void Raise(params string[] names)
    {
        void Fire() { foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) Fire(); else d.BeginInvoke((Action)Fire);
    }
}

public static class JobRunner
{
    static readonly SemaphoreSlim Slots = new(Math.Max(2, Environment.ProcessorCount / 2));

    public static ObservableCollection<JobVM> Jobs { get; } = new();
    /// <summary>Raised on the UI thread when something needs the user's attention (a new job, a failure).</summary>
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>Queue an operation over the selection. Actions run immediately.</summary>
    public static void Start(Op op, IReadOnlyList<string> paths)
    {
        if (op.Kind == OpKind.Action) { _ = RunAction(op, paths); return; }
        if (op.Prompt != null && op.RunWith != null)
        {
            var answer = InputDialog.Ask(op.Label, op.Prompt, op.Check, op.Secret, op.AllowBlank);
            if (answer == null) return; // cancelled
            var with = op.RunWith;
            op = op with { Run = (s, d, p, ct) => with(s, d, answer, p, ct) };
        }

        var batch = Db.NewBatch();
        var units = op.Batch ? new List<string> { string.Join('\n', paths) } : paths.ToList();
        foreach (var src in units)
        {
            var first = src.Split('\n')[0];
            var vm = new JobVM { Title = Path.GetFileName(first.TrimEnd('\\', '/')) + (op.Batch && paths.Count > 1 ? $" +{paths.Count - 1}" : "") + "  →  " + op.Label };
            Application.Current.Dispatcher.Invoke(() => Jobs.Add(vm));
            Changed?.Invoke();
            _ = Execute(vm, op, src, op.Batch ? paths.Count : 1, batch);
        }
    }

    static async Task Execute(JobVM vm, Op op, string source, int files, long batch)
    {
        string? dst = null;
        long id = 0;
        try
        {
            await Slots.WaitAsync(vm.Cts.Token);
            try
            {
                vm.State = JobState.Running; vm.Message = "Working";
                dst = Output.Reserve(op, source.Split('\n')[0]);
                id = Db.NewJob(batch, op.Kind, op.Id, source.Replace('\n', ';'), counted: true);
                await op.Run(source, dst, p => { vm.Progress = p; vm.Message = p >= 0 ? $"{p:P0}" : "Working"; }, vm.Cts.Token);
                Db.FinishJob(id, "done", dst, null);
                vm.Output = dst; vm.Progress = 1; vm.Message = "Done"; vm.State = JobState.Done;
                _ = AutoDismiss(vm, 12);
            }
            finally { Slots.Release(); }
        }
        catch (OperationCanceledException)
        {
            Discard(dst); if (id > 0) Db.FinishJob(id, "cancelled", null, null);
            vm.Message = "Cancelled"; vm.State = JobState.Cancelled; _ = AutoDismiss(vm, 3);
        }
        catch (Exception e)
        {
            Discard(dst); if (id > 0) Db.FinishJob(id, "failed", null, e.Message);
            vm.Message = e.Message; vm.State = JobState.Failed; // stays until dismissed: the user needs to read it
        }
        Changed?.Invoke();
    }

    static async Task RunAction(Op op, IReadOnlyList<string> paths)
    {
        try { await op.Run(string.Join('\n', paths), "", _ => { }, CancellationToken.None); }
        catch (Exception e)
        {
            var vm = new JobVM { Title = op.Label, Message = e.Message, State = JobState.Failed };
            Application.Current.Dispatcher.Invoke(() => Jobs.Add(vm));
            Changed?.Invoke();
        }
    }

    static void Discard(string? path)
    {
        try { if (path != null) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); } } catch { }
    }

    public static void Dismiss(JobVM vm)
    {
        vm.Cts.Cancel();
        Application.Current.Dispatcher.BeginInvoke(() => { Jobs.Remove(vm); Changed?.Invoke(); });
    }

    static async Task AutoDismiss(JobVM vm, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        Dismiss(vm);
    }

    public static void Reveal(JobVM vm)
    {
        if (vm.Output == null) return;
        var arg = Directory.Exists(vm.Output) ? vm.Output : "/select," + vm.Output;
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { arg }, UseShellExecute = false });
    }
}
