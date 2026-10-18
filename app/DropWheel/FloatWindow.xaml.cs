using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DropWheel;

/// <summary>A small always-on-top tile for a file you need while working elsewhere. Drag the picture out to use the file.</summary>
public partial class FloatWindow : Window
{
    readonly string _path;

    public FloatWindow(string path)
    {
        InitializeComponent();
        _path = path;
        NameText.Text = Path.GetFileName(path.TrimEnd('\\', '/'));
        Thumb.Source = Thumbnail(path);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - 180; Top = wa.Top + 80;
    }

    static BitmapSource? Thumbnail(string path)
    {
        try
        {
            if (File.Exists(path) && Registry.Ext(path) is "jpg" or "jpeg" or "png" or "bmp" or "gif" or "tif" or "tiff")
            {
                var b = new BitmapImage();
                b.BeginInit(); b.UriSource = new Uri(path); b.DecodePixelWidth = 192; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                return b;
            }
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return icon == null ? null : Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { return null; }
    }

    void Thumb_MouseMove(object s, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragDrop.DoDragDrop(Thumb, new DataObject(DataFormats.FileDrop, new[] { _path }), DragDropEffects.Copy | DragDropEffects.Move);
    }

    void Name_MouseDown(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    void Close_Click(object s, RoutedEventArgs e) => Close();
}
