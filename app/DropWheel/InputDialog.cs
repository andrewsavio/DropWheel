using System.Windows;
using System.Windows.Controls;

namespace DropWheel;

/// <summary>One-question box for tools that need a value. Returns null on cancel.</summary>
public static class InputDialog
{
    public static string? Ask(string title, string question, System.Func<string, string?>? check = null, bool secret = false, bool allowBlank = false)
    {
        string? result = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var text = new TextBox { Margin = new Thickness(0, 8, 0, 4), MinWidth = 320 };
            var pass = new PasswordBox { Margin = new Thickness(0, 8, 0, 4), MinWidth = 320 };
            Control box = secret ? pass : text;
            string Value() => secret ? pass.Password : text.Text;
            var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, Margin = new Thickness(0, 0, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetName(box, question);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Assertive);
            text.TextChanged += (_, _) => error.Text = "";
            pass.PasswordChanged += (_, _) => error.Text = "";
            var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var no = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, no } };
            var w = new Window
            {
                Title = title, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = true,
                Content = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 }, box, error, row } },
            };
            ok.Click += (_, _) =>
            {
                var problem = string.IsNullOrWhiteSpace(Value()) && !allowBlank ? "Type a value first." : string.IsNullOrEmpty(Value()) ? null : check?.Invoke(Value());
                if (problem != null) { error.Text = problem; box.Focus(); return; }
                result = Value(); w.Close();
            };
            w.Loaded += (_, _) => box.Focus();
            w.ShowDialog();
        });
        return result;
    }
}
