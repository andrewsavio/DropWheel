using System.Windows;

namespace DropWheel;

public partial class PaywallWindow : Window
{
    public PaywallWindow()
    {
        InitializeComponent();
        Body.Text = $"{Db.FilesUsed} of {Db.FreeFiles} free files are used. Enter a license key to keep converting. File actions such as Copy path and Terminal always work.";
    }

    void Unlock_Click(object s, RoutedEventArgs e)
    {
        if (Db.TryUnlock(KeyBox.Text)) { Close(); return; }
        Error.Text = "That key was not accepted.";
    }
}
