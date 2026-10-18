using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace DropWheel;

/// <summary>
/// `DropWheel.exe --selftest log.txt [--shot wheel.png]` runs every engine-free operation against real files,
/// checks the trial counter, and optionally renders the wheel to a PNG. Exit code = number of failures.
/// The drag hook itself needs a human and a mouse; this does not cover it.
/// </summary>
public static class SelfTest
{
    static readonly StringBuilder Log = new();
    static int _fails;

    static void Check(string name, bool ok, string? info = null)
    {
        if (!ok) _fails++;
        Log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(info != null ? "  " + info : "")}");
    }

    static async Task<string> Run(string id, params string[] src)
    {
        var op = Registry.All().First(o => o.Id == id);
        var dst = Output.Reserve(op, src[0]);
        await op.Run(string.Join('\n', src), dst, _ => { }, default);
        return dst;
    }

    public static async Task<int> Run(string[] args)
    {
        var logPath = args.SkipWhile(a => a != "--selftest").Skip(1).FirstOrDefault() ?? "selftest.log";
        var shot = args.SkipWhile(a => a != "--shot").Skip(1).FirstOrDefault();
        var dir = Directory.CreateTempSubdirectory("dw-test-").FullName;
        try
        {
            Db.Open(Path.Combine(dir, "db"));
            Settings.Reload();
            Settings.OutputDir = "";
            var png = MakePng(Path.Combine(dir, "pic.png"));

            // images, native WIC
            foreach (var o in new[] { "jpg", "bmp", "tiff", "gif" })
            {
                try
                {
                    var outp = await Run("img>" + o, png);
                    Check("image png>" + o, Width(outp) == 64, Path.GetFileName(outp));
                    if (o == "jpg") Check("transparent corner becomes white, not black", Corner(outp) > 230, "pixel " + Corner(outp));
                }
                catch (Exception e) { Check("image png>" + o, false, e.Message); }
            }
            var jpg = Directory.GetFiles(dir, "pic*.jpg").First();
            Check("compress image", Width(await Run("t.img.compress", png)) == 64);
            var clean = await Run("t.img.strip", jpg);
            Check("strip metadata keeps extension", Path.GetExtension(clean) == ".jpg", Path.GetFileName(clean));
            var again = await Run("img>bmp", png);
            Check("output never overwrites", Path.GetFileName(again) == "pic (1).bmp" && File.Exists(Path.Combine(dir, "pic.bmp")), Path.GetFileName(again));

            // archives, built in
            var folder = Directory.CreateDirectory(Path.Combine(dir, "stuff")).FullName;
            File.WriteAllText(Path.Combine(folder, "a.txt"), "alpha");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "beta");
            var zip = await Run("t.zip", folder);
            var tgz = await Run("arc>tar.gz", zip);
            var unpacked = await Run("t.unpack", tgz);
            Check("zip -> tar.gz -> unpack keeps files",
                File.Exists(Path.Combine(unpacked, "stuff", "a.txt")) && File.ReadAllText(Path.Combine(unpacked, "stuff", "sub", "b.txt")) == "beta", Path.GetFileName(unpacked));

            // PDF, PdfSharp
            string P(string n)
            {
                var b = new PdfDocumentBuilder();
                b.DocumentInformation.Author = "someone";
                b.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
                var p = Path.Combine(dir, n); File.WriteAllBytes(p, b.Build()); return p;
            }
            var p1 = P("one.pdf"); var p2 = P("two.pdf");
            var merged = await Run("t.pdf.merge", p1, p2);
            using (var m = PdfDocument.Open(merged)) Check("merge pdf", m.NumberOfPages == 2, m.NumberOfPages + " pages");
            var split = await Run("t.pdf.split", merged);
            Check("split pdf", Directory.GetFiles(split, "*.pdf").Length == 2);
            var stripped = await Run("t.pdf.strip", p1);
            using (var s = PdfDocument.Open(stripped)) Check("strip pdf author", string.IsNullOrEmpty(s.Information.Author), "author='" + s.Information.Author + "'");
            using (var s = PdfDocument.Open(p1)) Check("test pdf really had an author", s.Information.Author == "someone");

            // subtitles
            var srt = Path.Combine(dir, "s.srt");
            File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:02,000\n<i>Hello</i>\n\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n");
            Check("srt to txt", File.ReadAllText(await Run("sub>txt", srt)).Replace("\r\n", "|") == "Hello|World");

            // wheel logic
            Check("png offers jpg", Registry.For(new[] { png }, OpKind.Convert).Any(o => o.Id == "img>jpg"));
            Check("mixed png + pdf offers no conversion", Registry.For(new[] { png, p1 }, OpKind.Convert).Count == 0);
            Check("one pdf cannot merge", Registry.For(new[] { p1 }, OpKind.Tool).All(o => o.Id != "t.pdf.merge"));
            Check("two pdfs can merge", Registry.For(new[] { p1, p2 }, OpKind.Tool).Any(o => o.Id == "t.pdf.merge"));
            Check("actions work on any selection", Registry.For(new[] { folder }, OpKind.Action).Count >= 5);

            // trial counter
            Check("fresh install allows 20 files", Db.Allows(20) && !Db.Allows(21));
            Db.AddUsed(20);
            Check("21st file blocked", !Db.Allows(1));
            Check("bad key rejected", !Db.TryUnlock("nope"));

            Log.AppendLine("engines: " + string.Join(", ", new[] { "ffmpeg", "magick", "pandoc", "soffice", "7z", "pdftoppm", "gswin64c" }.Select(e => $"{e}={(Engines.Has(e) ? "yes" : "no")}")));
            Log.AppendLine("ops available: " + Registry.All().Count(o => o.Available) + " of " + Registry.All().Count);

            if (shot != null) RenderWheel(png, shot);
        }
        catch (Exception e) { Check("self-test crashed", false, e.ToString()); }
        File.WriteAllText(logPath, Log.ToString());
        return _fails;
    }

    static string MakePng(string path)
    {
        const int w = 64, h = 48;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool corner = x < 8 && y < 8; // fully transparent corner
                px[i] = (byte)(x * 4); px[i + 1] = (byte)(y * 5); px[i + 2] = 40; px[i + 3] = (byte)(corner ? 0 : 255);
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var f = File.Create(path);
        enc.Save(f);
        return path;
    }

    static BitmapSource Open(string path)
    {
        using var f = File.OpenRead(path);
        return BitmapDecoder.Create(f, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    }

    static int Width(string path) => Open(path).PixelWidth;

    static int Corner(string path)
    {
        var b = new FormatConvertedBitmap(Open(path), PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        b.CopyPixels(new Int32Rect(1, 1, 1, 1), px, 4, 0);
        return (px[0] + px[1] + px[2]) / 3;
    }

    static void RenderWheel(string sample, string outPng)
    {
        var w = new WheelWindow(Mode.Convert, new[] { sample }, keyboard: true);
        w.Show();
        w.UpdateLayout();
        var rtb = new RenderTargetBitmap(420, 420, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x6b, 0x72, 0x80)), null, new Rect(0, 0, 420, 420));
        rtb.Render(bg);
        rtb.Render(w.Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(outPng);
        enc.Save(f);
        w.Close();
    }
}
