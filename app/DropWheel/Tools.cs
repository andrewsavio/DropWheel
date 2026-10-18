using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig.Writer;

namespace DropWheel;

/// <summary>What the user asked for: an exact size, one side (the other keeps the ratio), or a scale factor.</summary>
public readonly record struct SizeSpec(int? W, int? H, double? Scale)
{
    public (int w, int h) For(int srcW, int srcH)
    {
        if (Scale is double s) return (Math.Max(1, (int)Math.Round(srcW * s)), Math.Max(1, (int)Math.Round(srcH * s)));
        if (W is int w && H is int h) return (w, h);
        if (W is int ow) return (ow, Math.Max(1, (int)Math.Round((double)srcH * ow / srcW)));
        var oh = H!.Value;
        return (Math.Max(1, (int)Math.Round((double)srcW * oh / srcH)), oh);
    }
}

/// <summary>Parses what people type into the size prompts. Every tool validates with these before queueing a job.</summary>
public static class Sizes
{
    public const string ResizeHelp = "New size: 1920x1080 (exact), 800 (width), x600 (height), 50% or 2x (scale). Units: px, or in/cm/mm at 300 dpi (add @150dpi to change).";
    public const string TargetHelp = "Target file size, like 200 KB or 1.5 MB. For images you can also give a quality, like 70%.";

    static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
    const string N = @"(\d+(?:\.\d+)?)";

    public static SizeSpec? Parse(string text)
    {
        var s = text.Trim().ToLowerInvariant().Replace("×", "x").Replace(" by ", "x").Replace("px", "").Replace(" ", "");
        double dpi = 300, unit = 1;
        var m = Regex.Match(s, @"@(\d+)dpi$");
        if (m.Success) { dpi = D(m.Groups[1]); s = s[..m.Index]; }
        foreach (var (u, f) in new[] { ("in", dpi), ("cm", dpi / 2.54), ("mm", dpi / 25.4) })
            if (s.Contains(u)) { unit = f; s = s.Replace(u, ""); break; }

        SizeSpec? r = null;
        if ((m = Regex.Match(s, $"^{N}%$")).Success) r = new(null, null, D(m.Groups[1]) / 100);
        else if ((m = Regex.Match(s, $"^{N}x$")).Success && D(m.Groups[1]) <= 10 && unit == 1) r = new(null, null, D(m.Groups[1])); // "2x" scale, "800x" width
        else if ((m = Regex.Match(s, $"^{N}x{N}$")).Success) r = new(Px(D(m.Groups[1]) * unit), Px(D(m.Groups[2]) * unit), null);
        else if ((m = Regex.Match(s, $"^(?:w|width)?{N}(?:w|width)?x?$")).Success) r = new(Px(D(m.Groups[1]) * unit), null, null);
        else if ((m = Regex.Match(s, $"^(?:x|h|height){N}$|^{N}(?:h|height)$")).Success)
            r = new(null, Px(D(m.Groups[1].Success ? m.Groups[1] : m.Groups[2]) * unit), null);
        if (r is not { } v) return null;
        bool ok = v.Scale is double sc ? sc is >= 0.01 and <= 10 : (v.W ?? 1) is >= 1 and <= 30000 && (v.H ?? 1) is >= 1 and <= 30000;
        return ok ? v : null;
    }

    static int Px(double v) => (int)Math.Round(v);

    /// <summary>"200 KB", "1.5mb", "300k" give bytes; "70%" or "q70" give a quality (1-100). A bare number is KB.</summary>
    public static (long? bytes, int? quality)? Target(string text)
    {
        var s = text.Trim().ToLowerInvariant().Replace(" ", "");
        Match m;
        if ((m = Regex.Match(s, @"^(?:q)?(\d{1,3})%$|^q(\d{1,3})$")).Success)
        {
            var q = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            return q is >= 1 and <= 100 ? (null, q) : null;
        }
        if ((m = Regex.Match(s, $"^{N}(kb|k|mb|m|gb|g)?$")).Success)
        {
            double mult = m.Groups[2].Value switch { "mb" or "m" => 1 << 20, "gb" or "g" => 1L << 30, _ => 1024 };
            var b = (long)(D(m.Groups[1]) * mult);
            return b >= 1024 ? (b, null) : null;
        }
        return null;
    }

    public static string? CheckResize(string a) => Parse(a) == null ? "Try 1920x1080, 800, x600, 50% or 2x." : null;
    public static string? CheckTarget(string a) => Target(a) is { bytes: not null } or { quality: not null } ? null : "Try 200 KB, 1.5 MB or 70%.";
    public static string? CheckBytes(string a) => Target(a) is { bytes: not null } ? null : "Give a size, like 25 MB or 800 KB.";
}

/// <summary>Image, video, audio and PDF tools in the spirit of the online "I love" sites, all on this machine.</summary>
public static class Tools
{
    static readonly string[] Native = { "jpg", "jpeg", "png", "bmp", "tif", "tiff", "gif" };

    // ---------------- images
    /// <summary>Decodes with Windows; formats it cannot read (WebP, HEIC, AVIF, SVG) go through ImageMagick first.</summary>
    static BitmapSource Load(string src)
    {
        var path = src;
        string? tmp = null;
        if (!Native.Contains(Registry.Ext(src)))
        {
            tmp = Path.Combine(Path.GetTempPath(), "dw-" + Guid.NewGuid().ToString("N") + ".png");
            Task.Run(() => Engines.Run("magick", new[] { src + "[0]", tmp }, default)).GetAwaiter().GetResult(); // off the UI context, so blocking cannot deadlock
            path = tmp;
        }
        try
        {
            using var fs = File.OpenRead(path);
            var f = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            f.Freeze();
            return f;
        }
        catch (Exception e) when (e is not IOException) { throw new InvalidOperationException($"{Path.GetFileName(src)} is damaged or is not a picture."); }
        finally { if (tmp != null) try { File.Delete(tmp); } catch { } }
    }

    static byte[] Encode(BitmapSource b, string ext, int quality = 92)
    {
        BitmapEncoder enc = ext switch
        {
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = quality },
            "png" => new PngBitmapEncoder(),
            "bmp" => new BmpBitmapEncoder(),
            "tif" or "tiff" => new TiffBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            _ => throw new InvalidOperationException("No native encoder for ." + ext),
        };
        if (ext is "jpg" or "jpeg" or "bmp") b = Wic.OnWhite(b);
        enc.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static BitmapSource Scale(BitmapSource b, int w, int h)
    {
        var t = new TransformedBitmap(b, new ScaleTransform((double)w / b.PixelWidth, (double)h / b.PixelHeight));
        t.Freeze();
        return t;
    }

    /// <summary>Resize or enlarge. ImageMagick (Lanczos) when installed, for the best quality; Windows otherwise.</summary>
    public static async Task Resize(string src, string dst, SizeSpec spec, CancellationToken ct)
    {
        var ext = Registry.Ext(dst);
        if (Engines.Has("magick"))
        {
            var info = new List<string>();
            await Engines.Run("magick", new[] { "identify", "-format", "%w %h", src + "[0]" }, ct, l => { if (l.Length > 0) info.Add(l); });
            var wh = info.Last().Split(' ');
            var (w, h) = spec.For(int.Parse(wh[0]), int.Parse(wh[1]));
            await Engines.Run("magick", new[] { src + "[0]", "-filter", "Lanczos", "-resize", $"{w}x{h}!", dst }, ct);
            return;
        }
        await Task.Run(() =>
        {
            var b = Load(src);
            var (w, h) = spec.For(b.PixelWidth, b.PixelHeight);
            File.WriteAllBytes(dst, Encode(Scale(b, w, h), ext));
        }, ct);
    }

    /// <summary>JPG at a set quality, or the best quality that fits the size; shrinks the picture only if quality 10 is still too big.</summary>
    public static void CompressTo(string src, string dst, (long? bytes, int? quality) target, CancellationToken ct)
    {
        var b = Load(src);
        if (target.quality is int q) { File.WriteAllBytes(dst, Encode(b, "jpg", q)); return; }
        long max = target.bytes!.Value;
        for (int shrink = 0; shrink < 15; shrink++)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? best = null;
            int lo = 10, hi = 95;
            while (lo <= hi) // largest quality that fits
            {
                int mid = (lo + hi) / 2;
                var bytes = Encode(b, "jpg", mid);
                if (bytes.Length <= max) { best = bytes; lo = mid + 1; } else hi = mid - 1;
            }
            if (best != null) { File.WriteAllBytes(dst, best); return; }
            b = Scale(b, Math.Max(1, (int)(b.PixelWidth * 0.85)), Math.Max(1, (int)(b.PixelHeight * 0.85)));
        }
        throw new InvalidOperationException($"Could not get {Path.GetFileName(src)} under {max / 1024} KB.");
    }

    // ---------------- video and audio (ffmpeg)
    public static async Task<double> Duration(string src, CancellationToken ct)
    {
        string? last = null;
        await Engines.Run("ffprobe", new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", src }, ct, l => { if (l.Trim().Length > 0) last = l.Trim(); });
        return double.TryParse(last, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0
            ? d : throw new InvalidOperationException($"Could not read the length of {Path.GetFileName(src)}.");
    }

    static int Even(int v) => Math.Max(2, v - v % 2); // H.264 needs even sides

    public static string ScaleFilter(SizeSpec s) => s switch
    {
        { Scale: double k } => $"scale=trunc(iw*{k.ToString(CultureInfo.InvariantCulture)}/2)*2:-2",
        { W: int w, H: int h } => $"scale={Even(w)}:{Even(h)}",
        { W: int w } => $"scale={Even(w)}:-2",
        _ => $"scale=-2:{Even(s.H!.Value)}",
    };

    /// <summary>Average bitrate that lands near the size; one pass, so expect within about 10%.</summary>
    public static async Task<string[]> VideoForSize(string src, long bytes, CancellationToken ct)
    {
        var d = await Duration(src, ct);
        int kbps = (int)(bytes * 8 / d / 1000) - 96;
        if (kbps < 50) throw new InvalidOperationException($"{bytes / 1024} KB is too small for {d:0} seconds of video. Try a bigger size.");
        return new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p", "-b:v", kbps + "k", "-maxrate", (int)(kbps * 1.2) + "k", "-bufsize", kbps * 2 + "k", "-c:a", "aac", "-b:a", "96k" };
    }

    public static async Task<string[]> AudioForSize(string src, long bytes, CancellationToken ct)
    {
        var d = await Duration(src, ct);
        int kbps = Math.Clamp((int)(bytes * 8 / d / 1000), 32, 320);
        return new[] { "-vn", "-c:a", "libmp3lame", "-b:a", kbps + "k" };
    }

    // ---------------- PDF (Windows draws the pages; nothing to install)
    static async Task<Windows.Data.Pdf.PdfDocument> OpenPdf(string src)
    {
        try { return await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(src))); }
        catch (Exception e) when (e is not FileNotFoundException) { throw new InvalidOperationException($"{Path.GetFileName(src)} is damaged or password protected."); }
    }

    /// <summary>One page as pixels, <paramref name="dpi"/> dots per inch, white background.</summary>
    public static async Task<BitmapSource> RenderPage(Windows.Data.Pdf.PdfDocument doc, uint index, double dpi)
    {
        using var page = doc.GetPage(index);
        using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(ms, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)Math.Round(page.Size.Width / 96 * dpi) });
        ms.Seek(0);
        var f = BitmapDecoder.Create(ms.AsStreamForRead(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var rgb = new FormatConvertedBitmap(f, PixelFormats.Bgr24, null, 0);
        rgb.Freeze();
        return rgb;
    }

    public static async Task PdfToImages(string src, string dir, string ext, Action<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var doc = await OpenPdf(src);
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            File.WriteAllBytes(Path.Combine(dir, $"page-{i + 1}.{ext}"), Encode(await RenderPage(doc, i, 150), ext, 90));
            progress((i + 1.0) / doc.PageCount);
        }
    }

    public static (int dpi, int quality)? PdfLevel(string a) => a.Trim().ToLowerInvariant() switch
    {
        "low" or "light" or "less" => (150, 75),
        "medium" or "recommended" or "" => (120, 60),
        "high" or "strong" or "extreme" or "more" => (90, 45),
        _ => null,
    };
    public static string? CheckPdfLevel(string a) => PdfLevel(a) == null ? "Type low, medium or strong." : null;

    /// <summary>Redraws every page as a JPG. Much smaller for scans and photos; text stops being selectable.</summary>
    public static async Task PdfShrink(string src, string dst, (int dpi, int quality) level, Action<double> progress, CancellationToken ct)
    {
        var doc = await OpenPdf(src);
        var pdf = new PdfDocumentBuilder();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            double w, h;
            using (var p = doc.GetPage(i)) { w = p.Size.Width * 0.75; h = p.Size.Height * 0.75; } // DIPs to points
            pdf.AddPage(w, h).AddJpeg(Encode(await RenderPage(doc, i, level.dpi), "jpg", level.quality), new UglyToad.PdfPig.Core.PdfRectangle(0, 0, w, h));
            progress((i + 1.0) / doc.PageCount);
        }
        File.WriteAllBytes(dst, pdf.Build());
        if (new FileInfo(dst).Length >= new FileInfo(src).Length)
            throw new InvalidOperationException("This PDF is already small; shrinking it would make it bigger. Nothing was changed.");
    }
}
