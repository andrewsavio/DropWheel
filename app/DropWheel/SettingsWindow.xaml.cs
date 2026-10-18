using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DropWheel;

public partial class SettingsWindow : Window
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        foreach (var cb in new[] { CbConvert, CbTools, CbActions }) cb.ItemsSource = Settings.KeyNames;
        CbConvert.SelectedItem = Settings.KeyName(Mode.Convert);
        CbTools.SelectedItem = Settings.KeyName(Mode.Tools);
        CbActions.SelectedItem = Settings.KeyName(Mode.Actions);
        OutBox.Text = Settings.OutputDir;
        using (var k = Registry_Run()) StartBox.IsChecked = k?.GetValue("DropWheel") != null;
        Refresh();
        _loading = false;
    }

    static RegistryKey? Registry_Run() => Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);

    void Refresh()
    {
        LicenseText.Text = Db.Unlocked ? "Unlocked. Thank you."
            : $"{Db.FilesUsed} of {Db.FreeFiles} free files used.";
        Engines.Refresh();
        var missing = Registry.Missing();
        EngineText.Text = missing.Count == 0
            ? "All engines found."
            : "Not found, so these conversions are hidden: " + string.Join(", ", missing.Select(m => $"{m.engine} ({m.ops})"))
              + ". Put them in the engines folder next to the app, or on PATH.";
    }

    /// <summary>Two modes on one key could never be told apart, so picking a taken key swaps the two.</summary>
    void Key_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox changed || changed.SelectedItem is not string key) return;
        var pairs = new[] { (Mode.Convert, CbConvert), (Mode.Tools, CbTools), (Mode.Actions, CbActions) };
        var other = pairs.FirstOrDefault(p => p.Item2 != changed && (string?)p.Item2.SelectedItem == key);
        if (other.Item2 != null && e.RemovedItems.Count > 0)
        {
            _loading = true; other.Item2.SelectedItem = e.RemovedItems[0]; _loading = false;
        }
        foreach (var (mode, cb) in pairs) Settings.SetKeyName(mode, (string)cb.SelectedItem);
        Settings.Reload();
    }

    void BrowseOut_Click(object s, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Save converted files in" };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Settings.OutputDir = dlg.SelectedPath; OutBox.Text = dlg.SelectedPath;
    }

    void ClearOut_Click(object s, RoutedEventArgs e) { Settings.OutputDir = ""; OutBox.Text = ""; }

    void Start_Changed(object s, RoutedEventArgs e)
    {
        if (_loading) return;
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartBox.IsChecked == true) k.SetValue("DropWheel", "\"" + Environment.ProcessPath + "\"");
        else k.DeleteValue("DropWheel", false);
    }

    void Recheck_Click(object s, RoutedEventArgs e) => Refresh();
}
