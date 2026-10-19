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
            var split = await Ask("t.pdf.split", "all", merged);
            Check("split pdf", Directory.GetFiles(split, "*.pdf").Length == 2);
            var stripped = await Run("t.pdf.strip", p1);
            using (var s = PdfDocument.Open(stripped)) Check("strip pdf author", string.IsNullOrEmpty(s.Information.Author), "author='" + s.Information.Author + "'");
            using (var s = PdfDocument.Open(p1)) Check("test pdf really had an author", s.Information.Author == "someone");

            // redaction
            var rb = new PdfDocumentBuilder();
            rb.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Client Acme owes 500 dollars", 14,
                new UglyToad.PdfPig.Core.PdfPoint(50, 700), rb.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));
            var rsrc = Path.Combine(dir, "secret.pdf"); File.WriteAllBytes(rsrc, rb.Build());
            var rdst = Output.Reserve(Registry.All().First(o => o.Id == "t.pdf.redact"), rsrc);
            await Registry.PdfRedact(rsrc, rdst, "Acme", _ => { }, default);
            using (var r = PdfDocument.Open(rdst))
                Check("redacted pdf has no text", r.NumberOfPages == 1 && r.GetPage(1).Text.Length == 0, "text='" + r.GetPage(1).Text + "'");
            using (var r = PdfDocument.Open(rdst))
            {
                var im = r.GetPage(1).GetImages().Single();
                var bmp = BitmapDecoder.Create(new MemoryStream(im.RawBytes.ToArray()), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 3];
                new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgr24, null, 0).CopyPixels(px, bmp.PixelWidth * 3, 0);
                int dark = 0, light = 0; for (int i = 0; i < px.Length; i += 3) { if (px[i] < 30) dark++; else if (px[i] > 225) light++; }
                Check("redacted page keeps its look and has a black box", light > px.Length / 6 && dark > 200, $"dark {dark}, light {light}");
            }
            try { await Registry.PdfRedact(rsrc, rdst, "zzz", _ => { }, default); Check("redact with no match refuses", false); }
            catch (InvalidOperationException) { Check("redact with no match refuses", true); }

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

            // ---- edge cases through the real job queue
            await EdgeCases(dir, png, folder);


            await EngineChecks(dir, png);
            await ToolChecks(dir, png);
            await ImageChecks(dir, png);
            await PdfChecks(dir);
            await OfficeChecks(dir);

            Log.AppendLine("engines: " + string.Join(", ", new[] { "ffmpeg", "magick", "pandoc", "soffice", "7z", "pdftoppm", "gswin64c" }.Select(e => $"{e}={(Engines.Has(e) ? "yes" : "no")}")));
            Log.AppendLine("ops available: " + Registry.All().Count(o => o.Available) + " of " + Registry.All().Count);

            if (shot != null)
            {
                // --shot-file picks the dragged file, --shot-scale renders sharper, --shot-clear leaves the background transparent (for compositing)
                var file = args.SkipWhile(a => a != "--shot-file").Skip(1).FirstOrDefault() ?? png;
                var scale = double.TryParse(args.SkipWhile(a => a != "--shot-scale").Skip(1).FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 1;
                RenderWheel(file, shot, scale, args.Contains("--shot-clear"));
            }
        }
        catch (Exception e) { Check("self-test crashed", false, e.ToString()); }
        File.WriteAllText(logPath, Log.ToString());
        return _fails;
    }

    // Only runs for engines that are installed; each check feeds a real file through the real op and looks at the output.
    /// <summary>Runs an engine and returns its stdout, for checking that an output really decodes.</summary>
    static string Cap(string exe, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Engines.Find(exe)!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000);
        return o.Trim();
    }

    /// <summary>ffprobe reads the file and reports about 2 s of media (the test clips are 2 s long).</summary>
    static bool Plays(string path)
    {
        if (!Engines.Has("ffprobe")) return true;
        var d = Cap("ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path);
        return double.TryParse(d, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 1.5 && s < 2.6;
    }

    /// <summary>ImageMagick decodes the image and finds the expected width.</summary>
    static bool ImageWidth(string path, int w) => Cap("magick", "identify", "-format", "%w", path + "[0]") == w.ToString();

    /// <summary>Runs a prompting tool the way the wheel does: answer checked first, then RunWith.</summary>
    static async Task<string> Ask(string id, string answer, params string[] src)
    {
        var op = Registry.All().First(o => o.Id == id);
        if (op.Check?.Invoke(answer) is string bad) throw new InvalidOperationException("rejected: " + bad);
        var dst = Output.Reserve(op, src[0]);
        await op.RunWith!(string.Join('\n', src), dst, answer, _ => { }, default);
        return dst;
    }

    static int Height(string path) => Open(path).PixelHeight;

    static string Noise(string path, int w, int h)
    {
        var px = new byte[w * h * 3];
        new Random(7).NextBytes(px);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, px, w * 3)));
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }

    static async Task ToolChecks(string dir, string png)
    {
        async Task T(string name, Func<Task<(bool ok, string info)>> body)
        {
            try { var (ok, info) = await body(); Check(name, ok, info); }
            catch (Exception e) { Check(name, false, e.Message); }
        }

        // the size language
        var cases = new (string text, int w, int h)[] { ("32", 32, 24), ("x24", 32, 24), ("32 px", 32, 24), ("200%", 128, 96), ("2x", 128, 96), ("100x30", 100, 30), ("100 × 30", 100, 30), ("1in@100dpi", 100, 75), ("h12", 16, 12), ("800x", 800, 600) };
        foreach (var (text, w, h) in cases)
        {
            var spec = Sizes.Parse(text);
            Check($"size '{text}' on 64x48 is {w}x{h}", spec?.For(64, 48) == (w, h), spec is { } v ? $"{v.For(64, 48)}" : "not understood");
        }
        Check("nonsense sizes are refused", new[] { "abc", "", "0x0", "-5", "99999x2", "200%%" }.All(t => Sizes.Parse(t) == null));
        Check("targets: 200 KB, 1.5mb, 70%", Sizes.Target("200 KB") == (204800L, null) && Sizes.Target("1.5mb") == (1572864L, null) && Sizes.Target("70%") == (null, 70));
        Check("bad targets are refused", Sizes.CheckTarget("big") != null && Sizes.CheckTarget("150%") != null && Sizes.CheckBytes("70%") != null);

        // images
        foreach (var (answer, w, h) in new[] { ("32", 32, 24), ("200%", 128, 96), ("100x30", 100, 30) })
            await T($"resize png to {answer}", async () => { var o = await Ask("t.img.resize", answer, png); return (Width(o) == w && Height(o) == h && o.EndsWith(".png"), $"{Width(o)}x{Height(o)} {Path.GetFileName(o)}"); });
        await T("rotate turns 64x48 into 48x64", async () => { var o = await Run("t.img.rotate", png); return (Width(o) == 48 && Height(o) == 64, $"{Width(o)}x{Height(o)}"); });
        var big = Noise(Path.Combine(dir, "noise.png"), 1200, 900);
        await T("compress to 60 KB fits and still opens", async () => { var o = await Ask("t.img.target", "60 KB", big); var n = new FileInfo(o).Length; return (n <= 60 * 1024 && Width(o) > 0, $"{n / 1024} KB, {Width(o)}x{Height(o)}"); });
        await T("compress at 50% quality", async () => { var o = await Ask("t.img.target", "50%", big); return (new FileInfo(o).Length < new FileInfo(big).Length && Width(o) == 1200, $"{new FileInfo(o).Length / 1024} KB"); });
        string twoPages = "";
        await T("two pictures become a 2-page PDF", async () => { twoPages = await Run("t.img.topdf", png, big); using var d = PdfDocument.Open(twoPages); return (d.NumberOfPages == 2, d.NumberOfPages + " pages"); });

        // PDF without Ghostscript or Poppler
        await T("PDF to JPG with the Windows renderer", async () =>
        {
            var folder = Path.Combine(dir, "pages"); await Tools.PdfToImages(twoPages, folder, "jpg", _ => { }, default);
            var files = Directory.GetFiles(folder, "*.jpg"); return (files.Length == 2 && Width(files[0]) > 0, files.Length + " files");
        });
        await T("PDF compress makes a scan-like PDF smaller", async () =>
        {
            var o = await Ask("t.pdf.smaller", "strong", twoPages); using var d = PdfDocument.Open(o);
            return (d.NumberOfPages == 2 && new FileInfo(o).Length < new FileInfo(twoPages).Length, $"{new FileInfo(twoPages).Length / 1024} KB -> {new FileInfo(o).Length / 1024} KB");
        });
        Check("PDF compress refuses unknown strength", PdfTools.CheckGsPreset("max power") != null);

        if (!Engines.Has("ffmpeg")) return;
        var mp4 = Path.Combine(dir, "clip.mp4"); var wav = Path.Combine(dir, "tone.wav");
        await T("video resize to 80 wide", async () =>
        {
            var o = await Ask("t.vid.resize", "80", mp4);
            var wh = Cap("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", o);
            return (wh == "80,60" && Plays(o), wh);
        });
        await T("video compress to 40 KB lands near 40 KB", async () => { var o = await Ask("t.vid.target", "40 KB", mp4); var n = new FileInfo(o).Length; return (n < 40 * 1024 * 1.35 && Plays(o), $"{n / 1024} KB"); });
        await T("audio compress to 16 KB lands near 16 KB", async () => { var o = await Ask("t.aud.target", "16 KB", wav); var n = new FileInfo(o).Length; return (n < 16 * 1024 * 1.35 && n > 16 * 1024 * 0.5 && Plays(o), $"{n / 1024} KB"); });
    }

    /// <summary>Number of pixels that differ between two images (ImageMagick compare -metric AE); 0 means identical pixels.</summary>
    static long PixelDiff(string a, string b)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Engines.Find("magick")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var x in new[] { "compare", "-metric", "AE", a + "[0]", b + "[0]", "null:" }) psi.ArgumentList.Add(x);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd(); p.WaitForExit(20000);
        var m = System.Text.RegularExpressions.Regex.Match(err, @"^\s*([\d.e+]+)");
        return m.Success ? (long)double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -1;
    }

    static string Magick(params string[] a) => Cap("magick", a);

    /// <summary>A photo-like JPG: smooth gradient plus fine noise, so compression has real work to do.</summary>
    static string Photo(string path, int w, int h)
    {
        var px = new byte[w * h * 3]; var rnd = new Random(3);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 3;
            px[i] = (byte)Math.Clamp(x * 255 / w + rnd.Next(-12, 12), 0, 255);
            px[i + 1] = (byte)Math.Clamp(y * 255 / h + rnd.Next(-12, 12), 0, 255);
            px[i + 2] = (byte)Math.Clamp(128 + rnd.Next(-30, 30), 0, 255);
        }
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, px, w * 3)));
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }

    static async Task ImageChecks(string dir, string png)
    {
        if (!Engines.Has("magick") || !Engines.Has("jpegtran")) { Check("image engines present (run app/fetch-engines.ps1)", false); return; }
        async Task T(string name, Func<Task<(bool ok, string info)>> body)
        {
            try { var (ok, info) = await body(); Check(name, ok, info); }
            catch (Exception e) { Check(name, false, e.Message); }
        }
        long Len(string p) => new FileInfo(p).Length;
        Check("engines come from the bundled folder", Engines.Find("magick")!.Contains(@"\engines\") && Engines.Find("jpegtran")!.Contains(@"\engines\"), Engines.Find("magick"));

        var photo = Photo(Path.Combine(dir, "photo.jpg"), 640, 480);
        var odd = Photo(Path.Combine(dir, "odd.jpg"), 641, 479);
        var gif = Path.Combine(dir, "anim.gif"); Magick("-delay", "20", png, "(", png, "-negate", ")", gif);
        var svg = Path.Combine(dir, "logo.svg");
        File.WriteAllText(svg, "<?xml version=\"1.0\"?>\n<!-- made in an editor -->\n<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" width=\"120\" height=\"80\" inkscape:version=\"1.3\">\n  <metadata><title>junk</title></metadata>\n  <rect x=\"10\" y=\"10\" width=\"100\" height=\"60\" fill=\"#f08a3a\" inkscape:label=\"box\"/>\n</svg>\n");

        // compress: smaller or equal, and not one pixel changed
        foreach (var (name, src) in new[] { ("JPG", photo), ("PNG", png), ("GIF", gif), ("SVG", svg) })
            await T($"lossless compress {name}: same pixels, not bigger", async () =>
            {
                var o = await Run("t.img.compress", src);
                var diff = PixelDiff(src, o);
                return (diff == 0 && Len(o) <= Len(src), $"{Len(src)} -> {Len(o)} B, {diff} pixels differ");
            });
        await T("SVG minify drops comments, metadata and editor markup", async () =>
        {
            var o = await Run("t.img.compress", svg); var t = File.ReadAllText(o);
            return (!t.Contains("<!--") && !t.Contains("metadata") && !t.Contains("inkscape") && t.Contains("<rect"), $"{t.Length} chars");
        });

        // strip metadata keeps pixels
        var tagged = Path.Combine(dir, "tagged.jpg"); Magick(photo, "-set", "comment", "secret location", tagged);
        await T("strip metadata: comment gone, pixels untouched", async () =>
        {
            var o = await Run("t.img.strip", tagged);
            return (Magick("identify", "-format", "%c", o) == "" && PixelDiff(tagged, o) == 0, "comment='" + Magick("identify", "-format", "%c", o) + "'");
        });

        // crop
        await T("crop JPG 16:9 is lossless and exact", async () =>
        {
            var o = await Ask("t.img.crop", "16:9", photo);
            var reference = Path.Combine(dir, "ref-crop.png"); Magick(photo, "-crop", "640x360+0+48", "+repage", reference); // centre 60 snapped to the 16 px grid
            // jpegtran does not re-encode; the decoder's chroma smoothing can still differ by a shade on the new edge, so allow 1 in 10,000 pixels
            var diff = PixelDiff(reference, o);
            return (Width(o) == 640 && Height(o) == 360 && diff >= 0 && diff <= 640 * 360 / 10000, $"{Width(o)}x{Height(o)}, {diff} edge pixels differ");
        });
        await T("crop exact x,y,w,h off the grid is exact", async () => { var o = await Ask("t.img.crop", "5,7,100,50", photo); return (Width(o) == 100 && Height(o) == 50, $"{Width(o)}x{Height(o)}"); });
        await T("crop PNG 1:1", async () => { var o = await Ask("t.img.crop", "1:1", png); return (Width(o) == 48 && Height(o) == 48 && o.EndsWith(".png"), $"{Width(o)}x{Height(o)}"); });
        Check("crop refuses nonsense", ImageTools.CheckCrop("banana") != null && ImageTools.CheckCrop("0:0") != null);

        // rotate: four right turns give back the very same pixels
        await T("rotate JPG right x4 is lossless", async () =>
        {
            var a = await Run("t.img.rotate", photo); bool swapped = Width(a) == 480 && Height(a) == 640;
            var b = await Run("t.img.rotate", a); var c = await Run("t.img.rotate", b); var d = await Run("t.img.rotate", c);
            return (swapped && PixelDiff(photo, d) == 0, $"{PixelDiff(photo, d)} pixels differ after 360°");
        });
        await T("rotate left on an odd-size JPG still works", async () => { var o = await Run("t.img.rotateleft", odd); return (Width(o) == 479 && Height(o) == 641, $"{Width(o)}x{Height(o)}"); });

        // create
        await T("watermark keeps the size and marks the corner", async () => { var o = await Ask("t.img.watermark", "© 2026 Test", photo); return (Width(o) == 640 && Height(o) == 480 && PixelDiff(photo, o) > 100, PixelDiff(photo, o) + " pixels changed"); });
        await T("meme keeps the size and adds both captions", async () => { var o = await Ask("t.img.meme", "top text | bottom text", photo); return (Width(o) == 640 && PixelDiff(photo, o) > 500, PixelDiff(photo, o) + " pixels changed"); });
        await T("To PDF puts the JPG in byte for byte", async () =>
        {
            var o = await Run("t.img.topdf", photo, png);
            using var d = PdfDocument.Open(o);
            var raw = d.GetPage(1).GetImages().Single().RawBytes.ToArray();
            return (d.NumberOfPages == 2 && raw.SequenceEqual(File.ReadAllBytes(photo)), $"{d.NumberOfPages} pages, jpg bytes identical={raw.SequenceEqual(File.ReadAllBytes(photo))}");
        });

        // convert
        await T("animated GIF from two pictures has two frames", async () =>
        {
            var o = await Run("img>anim", photo, odd);
            var frames = Magick("identify", "-format", "%n\n", o).Split('\n')[0].Trim();
            return (frames == "2", frames + " frames");
        });
        var psd = Path.Combine(dir, "layer.psd"); Magick(png, psd);
        await T("PSD converts to PNG", async () => { var o = await Run("img>png", psd); return (Width(o) == 64, $"{Width(o)} wide"); });
        Check("PSD and RAW are offered in the convert wheel", Registry.For(new[] { psd }, OpKind.Convert).Any(o => o.Id == "img>jpg") && Registry.For(new[] { Path.Combine(dir, "x.cr2") }, OpKind.Convert).Any(o => o.Id == "img>jpg"));
        Check("HEIC output only when ImageMagick can write it", ImageTools.MagickWrites("heic") == Registry.For(new[] { png }, OpKind.Convert).Any(o => o.Id == "img>heic"), "writes heic=" + ImageTools.MagickWrites("heic"));

        if (Engines.Has("msedge"))
        {
            var html = Path.Combine(dir, "page.html");
            File.WriteAllText(html, "<!doctype html><html><body style=\"margin:0\"><div style=\"height:300px;background:#f08a3a\">Hello</div></body></html>");
            await T("HTML page to PNG, 1280 wide", async () => { var o = await Run("html>png", html); return (Width(o) == 1280 && Height(o) >= 300 && Height(o) < 1500, $"{Width(o)}x{Height(o)}"); });
        }
    }

    static string PageText(string pdf, int n, string? password = null)
    {
        using var d = password == null ? PdfDocument.Open(pdf) : PdfDocument.Open(pdf, new UglyToad.PdfPig.ParsingOptions { Password = password });
        return d.GetPage(n).Text;
    }
    static int Pages(string pdf) { using var d = PdfDocument.Open(pdf); return d.NumberOfPages; }

    static async Task PdfChecks(string dir)
    {
        if (!Engines.Has("qpdf") || !Engines.Has("gswin64c")) { Check("PDF engines present (run app/fetch-engines.ps1)", false); return; }
        async Task T(string name, Func<Task<(bool ok, string info)>> body)
        {
            try { var (ok, info) = await body(); Check(name, ok, info); }
            catch (Exception e) { Check(name, false, e.Message); }
        }
        long Len(string p) => new FileInfo(p).Length;
        Check("PDF engines come from the bundled folder", Engines.Find("qpdf")!.Contains(@"\engines\") && Engines.Find("gswin64c")!.Contains(@"\engines\"));

        // a 3-page document with real text and a photo on page 1
        var photoBytes = File.ReadAllBytes(Photo(Path.Combine(dir, "pdfphoto.jpg"), 1600, 1200));
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        string[] words = { "Page one alpha", "Page two beta", "Page three gamma" };
        for (int i = 0; i < 3; i++)
        {
            var pg = b.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            pg.AddText(words[i], 18, new UglyToad.PdfPig.Core.PdfPoint(60, 760), font);
            if (i == 0) pg.AddJpeg(photoBytes, new UglyToad.PdfPig.Core.PdfRectangle(60, 300, 535, 656));
        }
        var doc = Path.Combine(dir, "doc.pdf"); File.WriteAllBytes(doc, b.Build());
        bool SameText(string o) => Enumerable.Range(1, 3).All(n => PageText(o, n) == PageText(doc, n));

        // optimize
        await T("PDF lossless compress: same text, same photo bytes, not bigger", async () =>
        {
            var o = await Run("t.pdf.compress", doc);
            using var d = PdfDocument.Open(o);
            bool photo = d.GetPage(1).GetImages().Single().RawBytes.ToArray().SequenceEqual(photoBytes);
            return (SameText(o) && photo && Len(o) <= Len(doc), $"{Len(doc) / 1024} -> {Len(o) / 1024} KB, photo identical={photo}");
        });
        await T("PDF compress more (strong): much smaller, text is still text", async () =>
        {
            var o = await Ask("t.pdf.smaller", "strong", doc);
            return (Len(o) < Len(doc) / 2 && PageText(o, 2).Contains("Page two beta") && Pages(o) == 3, $"{Len(doc) / 1024} -> {Len(o) / 1024} KB");
        });
        var broken = Path.Combine(dir, "broken.pdf");
        var bytes = File.ReadAllBytes(doc); File.WriteAllBytes(broken, bytes[..(bytes.Length - 400)]); // xref table and trailer cut off
        await T("PDF repair rebuilds a file with its xref cut off, and the result checks clean", async () =>
        {
            var o = await Run("t.pdf.repair", broken);
            bool clean; try { await Engines.Run("qpdf", new[] { "--check", o }, default); clean = true; } catch { clean = false; }
            return (clean && Pages(o) == 3 && PageText(o, 3).Contains("gamma"), $"{Pages(o)} pages, qpdf --check clean={clean}");
        });

        // organize
        var other = Path.Combine(dir, "other.pdf"); File.Copy(doc, other);
        await T("PDF merge: 3 + 3 pages", async () => { var o = await Run("t.pdf.merge", doc, other); return (Pages(o) == 6, Pages(o) + " pages"); });
        await T("PDF split all: one file per page", async () => { var o = await Ask("t.pdf.split", "all", doc); return (Directory.GetFiles(o, "*.pdf").Length == 3, Directory.GetFiles(o).Length + " files"); });
        await T("PDF split ranges 1-2, 3", async () =>
        {
            var o = await Ask("t.pdf.split", "1-2, 3", doc); var files = Directory.GetFiles(o, "*.pdf").OrderBy(x => x).ToArray();
            return (files.Length == 2 && files.Select(Pages).OrderBy(x => x).SequenceEqual(new[] { 1, 2 }), string.Join(", ", files.Select(Path.GetFileName)));
        });
        await T("PDF organize 3,1: page 3 first, page 2 gone", async () => { var o = await Ask("t.pdf.organize", "3,1", doc); return (Pages(o) == 2 && PageText(o, 1).Contains("gamma") && PageText(o, 2).Contains("alpha"), Pages(o) + " pages"); });
        await T("PDF rotate right: pages turned, content untouched", async () =>
        {
            var o = await Run("t.pdf.rotate", doc); using var d = PdfDocument.Open(o);
            return (d.GetPage(1).Rotation.Value == 90 && SameText(o), "rotation " + d.GetPage(1).Rotation.Value);
        });
        Check("bad page ranges are refused", PdfTools.CheckRanges("1-") != null && PdfTools.CheckRanges("abc") != null && PdfTools.CheckSplit("all") == null && PdfTools.CheckRanges("1-3, 5, 7-z") == null);

        // security
        string locked = "";
        await T("PDF protect: will not open without the password, opens with it", async () =>
        {
            locked = await Ask("t.pdf.protect", "s3cret-pw", doc);
            bool refused; try { PageText(locked, 1); refused = false; } catch { refused = true; }
            return (refused && PageText(locked, 1, "s3cret-pw").Contains("alpha"), $"refused without password={refused}");
        });
        await T("PDF unlock with the right password", async () => { var o = await Ask("t.pdf.unlock", "s3cret-pw", locked); return (PageText(o, 1).Contains("alpha"), "opens without password"); });
        await T("PDF unlock with a wrong password says so", async () =>
        {
            try { await Ask("t.pdf.unlock", "nope", locked); return (false, "no error"); }
            catch (InvalidOperationException e) { return (e.Message == "That password is not right.", e.Message); }
        });
        Check("short passwords are refused", PdfTools.CheckPassword("abc") != null && PdfTools.CheckPassword("abcd") == null);

        // edit
        await T("PDF page numbers: 2 / 3 on page 2, original text kept", async () => { var o = await Run("t.pdf.numbers", doc); return (PageText(o, 2).Contains("2 / 3") && PageText(o, 2).Contains("Page two beta"), PageText(o, 2)); });
        await T("PDF watermark: an extra picture on every page, text kept", async () =>
        {
            var o = await Ask("t.pdf.watermark", "CONFIDENTIAL", doc); using var d = PdfDocument.Open(o);
            return (d.GetPage(1).GetImages().Count() == 2 && d.GetPage(3).GetImages().Count() == 1 && SameText(o), $"page 1 pictures {d.GetPage(1).GetImages().Count()}");
        });
        await T("PDF crop 10 mm: crop box 28.3 pt in on each side, page content kept", async () =>
        {
            var o = await Ask("t.pdf.crop", "10", doc); using var d = PdfDocument.Open(o);
            var c = d.GetPage(2).CropBox.Bounds; var m = d.GetPage(2).MediaBox.Bounds;
            bool ok = Math.Abs(c.Left - 28.35) < 0.1 && Math.Abs(m.Right - c.Right - 28.35) < 0.1 && Math.Abs(m.Top - c.Top - 28.35) < 0.1;
            return (ok && PageText(o, 2).Contains("beta"), $"crop {c.Left:0.#},{c.Bottom:0.#},{c.Right:0.#},{c.Top:0.#} of {m.Right:0.#}x{m.Top:0.#}");
        });
        Check("bad margins are refused", PdfTools.CheckMargins("big") != null && PdfTools.CheckMargins("10, 5, 10, 5") == null);

        // convert
        await T("PDF/A-2b: archive marker and colour profile", async () =>
        {
            var o = await Run("pdf>pdfa", doc); var raw = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(o));
            return (raw.Contains("pdfaid:part") && raw.Contains("/OutputIntents") && Pages(o) == 3, "marker and intent present");
        });
        await T("PDF extract images: the photo comes out byte for byte", async () =>
        {
            var o = await Run("pdf>images", doc); var f = Directory.GetFiles(o).Single();
            return (f.EndsWith(".jpg") && File.ReadAllBytes(f).SequenceEqual(photoBytes), Path.GetFileName(f));
        });
        // A4 at 200 dpi is 1653.6 px, so allow rounding either way
        await T("PDF to PNG at 200 dpi", async () => { var o = await Run("pdf>png", doc); var f = Directory.GetFiles(o, "*.png"); return (f.Length == 3 && Math.Abs(Width(f[0]) - 1654) <= 1, $"{f.Length} files, {Width(f[0])} px wide"); });
        await T("PDF to text without Poppler", async () => { var o = await Run("pdf>txt", doc); var t = File.ReadAllText(o); return (t.Contains("Page one alpha") && t.Contains("Page three gamma"), t.Length + " chars"); });
        if (Engines.Has("msedge"))
        {
            var html = Path.Combine(dir, "print.html");
            File.WriteAllText(html, "<!doctype html><html><body><h1>Hello PDF</h1><p>Printed by Edge.</p></body></html>");
            await T("HTML page to PDF", async () => { var o = await Run("html>pdf", html); return (Pages(o) >= 1 && PageText(o, 1).Contains("Hello PDF"), Pages(o) + " page(s)"); });
        }
    }

    static string AllText(string pdf) { using var d = PdfDocument.Open(pdf); return string.Join("\n", d.GetPages().Select(p => p.Text)); }

    static async Task OfficeChecks(string dir)
    {
        if (!Engines.Has("soffice")) { Check("LibreOffice present (run app/fetch-engines.ps1)", false); return; }
        async Task T(string name, Func<Task<(bool ok, string info)>> body)
        {
            try { var (ok, info) = await body(); Check(name, ok, info); }
            catch (Exception e) { Check(name, false, e.Message); }
        }
        Check("LibreOffice comes from the bundled folder", Engines.Find("soffice")!.Contains(@"\engines\"), Engines.Find("soffice"));

        // Word
        var rtf = Path.Combine(dir, "letter.rtf");
        File.WriteAllText(rtf, @"{\rtf1\ansi{\fonttbl{\f0 Calibri;}}\f0\fs32 Quarterly report\par\fs22 Revenue grew in every region.\par}");
        string docx = "";
        await T("RTF to Word (DOCX)", async () => { docx = await Run("off>docx", rtf); return (new FileInfo(docx).Length > 1000 && docx.EndsWith(".docx"), new FileInfo(docx).Length + " B"); });
        await T("Word to PDF keeps the words", async () => { var o = await Run("off>pdf", docx); var t = AllText(o); return (t.Contains("Quarterly report") && t.Contains("Revenue grew"), t.Replace("\n", " ")); });

        // Excel
        var csv = Path.Combine(dir, "sales.csv");
        File.WriteAllText(csv, "Item,Qty,Price\nApples,3,1.25\nPears,10,0.80\n");
        string xlsx = "";
        await T("CSV to Excel (XLSX)", async () => { xlsx = await Run("sheet>xlsx", csv); return (new FileInfo(xlsx).Length > 1000, new FileInfo(xlsx).Length + " B"); });
        await T("Excel to PDF keeps the cells", async () => { var o = await Run("off>pdf", xlsx); var t = AllText(o); return (t.Contains("Apples") && t.Contains("Pears") && t.Contains("1.25"), t.Replace("\n", " ")); });

        // PDF to Office, using a real 3-page text PDF
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        foreach (var w in new[] { "Slide one alpha", "Slide two beta", "Slide three gamma" })
            b.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(w, 20, new UglyToad.PdfPig.Core.PdfPoint(60, 760), font);
        var src = Path.Combine(dir, "deck.pdf"); File.WriteAllBytes(src, b.Build());
        await T("PDF to Word, and the words survive the round trip", async () => { var o = await Run("pdf>docx", src); var back = await Run("off>pdf", o); var t = AllText(back); return (t.Contains("Slide two beta"), t.Replace("\n", " ")); });
        string pptx = "";
        await T("PDF to PowerPoint: a slide per page", async () => { pptx = await Run("pdf>pptx", src); var back = await Run("off>pdf", pptx); return (Pages(back) == 3 && AllText(back).Contains("gamma"), Pages(back) + " slides"); });
        await T("PowerPoint slides to PNG", async () => { var o = await Run("slide>png", pptx); var n = Directory.GetFiles(o, "*.png").Length; return (n == 3, n + " pictures"); });

        // PDF to Excel: a table with three columns
        var tb = new PdfDocumentBuilder();
        var tf = tb.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var tp = tb.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        string[][] table = { new[] { "Item", "Qty", "Price" }, new[] { "Green apples", "3", "1.25" }, new[] { "Pears", "10", "0.80" } };
        for (int r = 0; r < table.Length; r++)
            for (int c = 0; c < 3; c++)
                tp.AddText(table[r][c], 12, new UglyToad.PdfPig.Core.PdfPoint(60 + c * 180, 760 - r * 22), tf);
        var tpdf = Path.Combine(dir, "table.pdf"); File.WriteAllBytes(tpdf, tb.Build());
        await T("PDF to Excel: rows and columns land in the right cells", async () =>
        {
            var o = await Run("pdf>xlsx", tpdf);
            var back = await Run("sheet>csv", o);
            var lines = File.ReadAllLines(back).Select(l => l.Replace("\"", "")).ToArray();
            return (lines.Length >= 3 && lines[0] == "Item,Qty,Price" && lines[1] == "Green apples,3,1.25" && lines[2].StartsWith("Pears,10,0.8"), string.Join(" | ", lines));
        });
    }

    static async Task EngineChecks(string dir, string png)
    {
        async Task Try(string name, string id, string src, Func<string, bool> ok)
        {
            try { var o = await Run(id, src); var good = File.Exists(o) && new FileInfo(o).Length > 0 && ok(o); Check(name, good, Path.GetFileName(o) + " " + new FileInfo(o).Length + " B"); }
            catch (Exception e) { Check(name, false, e.Message); }
        }
        if (Engines.Has("magick"))
        {
            await Try("magick png>webp", "img>webp", png, o => File.ReadAllBytes(o)[8] == (byte)'W' && ImageWidth(o, 64));
            await Try("magick png>avif", "img>avif", png, o => ImageWidth(o, 64));
            await Try("magick png>heic", "img>heic", png, o => ImageWidth(o, 64));
        }
        if (Engines.Has("ffmpeg"))
        {
            var mp4 = Path.Combine(dir, "clip.mp4"); var wav = Path.Combine(dir, "tone.wav");
            await Engines.Run("ffmpeg", new[] { "-y", "-f", "lavfi", "-i", "testsrc=duration=2:size=160x120:rate=15", "-f", "lavfi", "-i", "sine=duration=2", "-shortest", "-pix_fmt", "yuv420p", mp4 }, default);
            await Engines.Run("ffmpeg", new[] { "-y", "-f", "lavfi", "-i", "sine=duration=2", wav }, default);
            foreach (var o in new[] { "mov", "mkv", "webm", "avi", "wmv", "gif" }) await Try("ffmpeg mp4>" + o, "vid>" + o, mp4, Plays);
            await Try("ffmpeg extract mp3", "vid>mp3", mp4, Plays);
            foreach (var o in new[] { "mp3", "m4a", "flac", "ogg", "opus", "aiff", "wma" }) await Try("ffmpeg wav>" + o, "aud>" + o, wav, Plays);
            await Try("ffmpeg compress video", "t.vid.compress", mp4, Plays);
            await Try("ffmpeg compress audio", "t.aud.compress", wav, Plays);
            var srt = Path.Combine(dir, "e.srt"); File.WriteAllLines(srt, new[] { "1", "00:00:01,000 --> 00:00:02,000", "Hi" });
            await Try("ffmpeg srt>vtt", "sub>vtt", srt, o => File.ReadAllText(o).StartsWith("WEBVTT"));
        }
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

    static void RenderWheel(string sample, string outPng, double scale = 1, bool clear = false)
    {
        var w = new WheelWindow(Mode.Convert, new[] { sample }, keyboard: true);
        if (clear) w.Hint.Visibility = Visibility.Collapsed; // the keyboard hint is for people, not pictures
        w.Show();
        w.UpdateLayout();
        int px = (int)(420 * scale);
        var rtb = new RenderTargetBitmap(px, px, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        if (!clear) using (var dc = bg.RenderOpen()) dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection { new(Color.FromRgb(0xff, 0x8a, 0x3d), 0), new(Color.FromRgb(0xff, 0x6f, 0x91), 0.55), new(Color.FromRgb(0x7b, 0x6c, 0xf0), 1) }, new Point(0, 0), new Point(1, 1)), null, new Rect(0, 0, 420, 420));
        rtb.Render(bg);
        rtb.Render(w.Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var f = File.Create(outPng);
        enc.Save(f);
        var named = w.Ring.Children.OfType<UIElement>().Where(e => e is not System.Windows.Controls.TextBlock)
            .Select(e => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(e)?.GetName()).ToList();
        Check("every wheel wedge is visible to screen readers with a name", named.Count > 0 && named.All(n => !string.IsNullOrEmpty(n)), string.Join(" | ", named));
        w.Close();
    }

    // ------------------------------------------------------------------ edge cases

    static async Task<JobVM> Wait(Func<JobVM?> find, int seconds = 15)
    {
        for (int i = 0; i < seconds * 20; i++)
        {
            var vm = find();
            if (vm != null && vm.State is JobState.Done or JobState.Failed or JobState.Cancelled) return vm!;
            await Task.Delay(50);
        }
        throw new TimeoutException("job did not finish");
    }

    static JobVM Newest() => JobRunner.Jobs.Last();

    static async Task EdgeCases(string dir, string png, string folder)
    {
        var jpg = Registry.All().First(o => o.Id == "img>jpg");

        // names: accents, emoji, CJK, spaces
        var odd = Path.Combine(dir, "café ✓ 写真 (1).png");
        File.Copy(png, odd);
        JobRunner.Start(jpg, new[] { odd });
        var vm = await Wait(Newest);
        Check("unicode, emoji and spaces in the name", vm.State == JobState.Done && File.Exists(vm.Output), vm.Message);

        // very long name
        var longName = Path.Combine(dir, new string('x', 180) + ".png");
        File.Copy(png, longName);
        JobRunner.Start(jpg, new[] { longName });
        vm = await Wait(Newest);
        Check("180-character file name", vm.State == JobState.Done, vm.Message);

        // corrupt input: readable error, no stray output file left behind
        var bad = Path.Combine(dir, "broken.png");
        File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4, 5 });
        JobRunner.Start(jpg, new[] { bad });
        vm = await Wait(Newest);
        Check("corrupt image fails with a message", vm.State == JobState.Failed && vm.Message.Contains("damaged"), vm.Message);
        Check("failed job leaves no output behind", !File.Exists(Path.Combine(dir, "broken.jpg")));

        // zero-byte input
        var empty = Path.Combine(dir, "empty.png");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        JobRunner.Start(jpg, new[] { empty });
        vm = await Wait(Newest);
        Check("zero-byte file fails cleanly", vm.State == JobState.Failed && !File.Exists(Path.Combine(dir, "empty.jpg")), vm.Message);

        // input locked by another program
        var locked = Path.Combine(dir, "locked.png");
        File.Copy(png, locked);
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            JobRunner.Start(jpg, new[] { locked });
            vm = await Wait(Newest);
        }
        Check("locked source fails cleanly, says why", vm.State == JobState.Failed && vm.Message.Contains("open in another program") && !File.Exists(Path.Combine(dir, "locked.jpg")), vm.Message);

        // source disappeared before the job ran
        JobRunner.Start(jpg, new[] { Path.Combine(dir, "gone.png") });
        vm = await Wait(Newest);
        Check("missing source fails cleanly, says why", vm.State == JobState.Failed && vm.Message.Contains("no longer exists"), vm.Message);

        // many jobs at once, same source and target: names must not collide
        var before = Directory.GetFiles(dir, "pic*.jpg").Length;
        for (int i = 0; i < 8; i++) JobRunner.Start(jpg, new[] { png });
        var batch = JobRunner.Jobs.TakeLast(8).ToList();
        foreach (var j in batch) await Wait(() => j);
        var outs = batch.Select(j => j.Output).Distinct().Count();
        Check("8 parallel jobs get 8 different files", batch.All(j => j.State == JobState.Done) && outs == 8, $"{outs} distinct");

        // cancel while running: state is cancelled and the reserved name is removed
        var slow = new Op("test.slow", OpKind.Convert, "SLOW", "Test", "", _ => true, "out",
            async (s, d, p, ct) => { await Task.Delay(30000, ct); });
        JobRunner.Start(slow, new[] { png });
        var running = Newest();
        for (int i = 0; i < 100 && running.State != JobState.Running; i++) await Task.Delay(30);
        Check("job reaches Running", running.State == JobState.Running);
        var stem = Path.Combine(dir, "pic.out");
        Check("name is reserved while running", File.Exists(stem));
        running.Cts.Cancel();
        vm = await Wait(() => running);
        Check("cancel ends as Cancelled and removes the placeholder", vm.State == JobState.Cancelled && !File.Exists(stem), vm.State.ToString());

        // there is no allowance: many files in a row all convert
        for (int i = 0; i < 25; i++) JobRunner.Start(jpg, new[] { png });
        await Task.Delay(500);
        for (int i = 0; i < 100 && JobRunner.Jobs.Any(j => j.State is JobState.Queued or JobState.Running); i++) await Task.Delay(100);
        Check("25 conversions in a row, no limit", Directory.GetFiles(dir, "pic*.jpg").Length >= 25, Directory.GetFiles(dir, "pic*.jpg").Length + " jpg files");

        // output folder setting
        var elsewhere = Directory.CreateDirectory(Path.Combine(dir, "elsewhere")).FullName;
        Settings.OutputDir = elsewhere;
        JobRunner.Start(jpg, new[] { png });
        vm = await Wait(Newest);
        Settings.OutputDir = "";
        Check("chosen output folder is used", vm.State == JobState.Done && Path.GetDirectoryName(vm.Output) == elsewhere, vm.Output);

        // folder names that contain a dot
        var dotted = Directory.CreateDirectory(Path.Combine(dir, "my.folder")).FullName;
        File.WriteAllText(Path.Combine(dotted, "x.txt"), "x");
        var zipOp = Registry.All().First(o => o.Id == "t.zip");
        JobRunner.Start(zipOp, new[] { dotted });
        vm = await Wait(Newest);
        Check("folder named my.folder zips to my.folder.zip", Path.GetFileName(vm.Output) == "my.folder.zip", vm.Output);

    }
}
