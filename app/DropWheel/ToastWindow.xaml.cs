using System;
using System.Windows;
using System.Windows.Interop;

namespace DropWheel;

/// <summary>Bottom-right stack of job rows. Never takes focus, hides itself when the list is empty.</summary>
public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
        List.ItemsSource = JobRunner.Jobs;
        SourceInitialized += (_, _) => Native.MakeNoActivate(new WindowInteropHelper(this).Handle);
        SizeChanged += (_, _) => Place();
        JobRunner.Changed += () => Dispatcher.BeginInvoke(Sync);
    }

    void Sync()
    {
        if (JobRunner.Jobs.Count == 0) { Hide(); return; }
        if (!IsVisible) Show();
        Place();
    }

    void Place()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth; Top = wa.Bottom - ActualHeight;
    }

    static JobVM Vm(object sender) => (JobVM)((FrameworkElement)sender).DataContext;
    void Show_Click(object s, RoutedEventArgs e) => JobRunner.Reveal(Vm(s));
    void Close_Click(object s, RoutedEventArgs e) => JobRunner.Dismiss(Vm(s));
}
