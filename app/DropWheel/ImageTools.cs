using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using UglyToad.PdfPig.Writer;

namespace DropWheel;

/// <summary>
/// iLoveIMG-style image tools. Lossless wherever the operation allows: oxipng, jpegtran and gifsicle rewrite
/// files without touching a pixel; JPG crop and rotate go through jpegtran so the picture is not re-encoded.
/// </summary>
public static class ImageTools
{
    // ---------------- compress, lossless
    public static readonly string[] LosslessIn = { "jpg", "jpeg", "png", "gif", "svg" };

    /// <summary>Same pixels, smaller file. If nothing can be saved the original bytes are kept (never bigger).</summary>
    public static async Task CompressLossless(string src, string dst, CancellationToken ct)
    {
        switch (Registry.Ext(src))
        {
            case "jpg" or "jpeg":
                await Engines.Run("jpegtran", new[] { "-copy", "all", "-optimize", "-progressive", "-outfile", dst, src }, ct); break;
            case "png":
                File.Delete(dst); // oxipng refuses to overwrite the placeholder
                await Engines.Run("oxipng", new[] { "-o", "4", "--strip", "safe", "--out", dst, src }, ct); break;
            case "gif":
                await Engines.Run("gifsicle", new[] { "-O3", src, "-o", dst }, ct); break;
            case "svg":
                await Task.Run(() => MinifySvg(src, dst), ct); break;
            default: throw new InvalidOperationException("Lossless compression covers JPG, PNG, GIF and SVG.");
        }
        if (!File.Exists(dst) || new FileInfo(dst).Length >= new FileInfo(src).Length) File.Copy(src, dst, true);
    }

    static readonly string[] EditorNs = { "http://www.inkscape.org/namespaces/inkscape", "http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd", "http://ns.adobe.com/AdobeIllustrator/10.0/", "http://www.bohemiancoding.com/sketch/ns" };

    /// <summary>Drops comments, metadata and editor-only markup, and the whitespace between tags. Drawing stays the same.</summary>
    public static void MinifySvg(string src, string dst)
    {
        XDocument doc;
        try { doc = XDocument.Load(src, LoadOptions.None); }
        catch (Exception e) when (e is System.Xml.XmlException) { throw new InvalidOperationException($"{Path.GetFileName(src)} is not valid SVG."); }
        doc.DescendantNodes().OfType<XComment>().ToList().ForEach(c => c.Remove());
        doc.Descendants().Where(e => e.Name.LocalName == "metadata" || EditorNs.Contains(e.Name.NamespaceName)).ToList().ForEach(e => e.Remove());
        foreach (var el in doc.Descendants())
            el.Attributes().Where(a => EditorNs.Contains(a.Name.NamespaceName) || (a.IsNamespaceDeclaration && EditorNs.Contains(a.Value))).ToList().ForEach(a => a.Remove());
        doc.Save(dst, SaveOptions.DisableFormatting);
    }

    /// <summary>EXIF, GPS and comments out; the colour profile stays so colours do not shift. JPG and PNG pixels are untouched.</summary>
    public static async Task StripMetadata(string src, string dst, CancellationToken ct)
    {
        switch (Registry.Ext(src))
        {
            case "jpg" or "jpeg" when Engines.Has("jpegtran"):
                await Engines.Run("jpegtran", new[] { "-copy", "icc", "-optimize", "-outfile", dst, src }, ct); break;
            case "png" when Engines.Has("oxipng"):
                File.Delete(dst);
                await Engines.Run("oxipng", new[] { "--strip", "safe", "--out", dst, src }, ct); break;
            default: // TIFF: Windows re-saves it, which is lossless for TIFF
                await Task.Run(() => Wic.Convert(src, dst, Registry.Ext(dst), 95, strip: true), ct); break;
        }
    }

    // ---------------- crop
    public const string CropHelp = "Crop to a ratio (16:9, 1:1), a size from the centre (800x600), or x,y,width,height in pixels (10,20,800,600).";

    public static (int x, int y, int w, int h)? CropRect(string text, int srcW, int srcH)
    {
        var s = text.Trim().ToLowerInvariant().Replace(" ", "").Replace("px", "").Replace("×", "x");
        Match m;
        if ((m = Regex.Match(s, @"^(\d+(?:\.\d+)?):(\d+(?:\.\d+)?)$")).Success)
        {
            double r = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!(r > 0) || double.IsInfinity(r)) return null; // also catches 0:0 (NaN)
            int w = srcW, h = (int)Math.Round(srcW / r);
            if (h > srcH) { h = srcH; w = (int)Math.Round(srcH * r); }
            return ((srcW - w) / 2, (srcH - h) / 2, w, h);
        }
        if ((m = Regex.Match(s, @"^(\d+)x(\d+)$")).Success)
        {
            int w = Math.Min(srcW, int.Parse(m.Groups[1].Value)), h = Math.Min(srcH, int.Parse(m.Groups[2].Value));
            return w > 0 && h > 0 ? ((srcW - w) / 2, (srcH - h) / 2, w, h) : null;
        }
        if ((m = Regex.Match(s, @"^(\d+),(\d+),(\d+),(\d+)$")).Success)
        {
            int x = int.Parse(m.Groups[1].Value), y = int.Parse(m.Groups[2].Value), w = int.Parse(m.Groups[3].Value), h = int.Parse(m.Groups[4].Value);
            w = Math.Min(w, srcW - x); h = Math.Min(h, srcH - y);
            return x < srcW && y < srcH && w > 0 && h > 0 ? (x, y, w, h) : null;
        }
        return null;
    }

    public static string? CheckCrop(string a) => CropRect(a, 10000, 10000) == null ? "Try 16:9, 800x600 or 10,20,800,600." : null;

    static async Task<(int w, int h)> Size(string src, CancellationToken ct)
    {
        string? last = null;
        await Engines.Run("magick", new[] { "identify", "-format", "%w %h", src + "[0]" }, ct, l => { if (l.Trim().Length > 0) last = l.Trim(); });
        var p = (last ?? throw new InvalidOperationException($"Could not read {Path.GetFileName(src)}.")).Split(' ');
        return (int.Parse(p[0]), int.Parse(p[1]));
    }

    /// <summary>
    /// JPG crops are lossless when the crop starts on the 16 px block grid. Ratio and centre crops are nudged onto it
    /// (still centred to within a few pixels); an exact x,y that is off the grid is re-saved once at the original quality.
    /// </summary>
    public static async Task Crop(string src, string dst, string answer, CancellationToken ct)
    {
        var (sw, sh) = await Size(src, ct);
        var (x, y, w, h) = CropRect(answer, sw, sh) ?? throw new InvalidOperationException("That crop is outside the picture.");
        if (Registry.Ext(src) is "jpg" or "jpeg")
        {
            if (!answer.Contains(',')) { x -= x % 16; y -= y % 16; } // centred crops: snap onto the block grid
            if (x % 16 == 0 && y % 16 == 0)
            {
                await Engines.Run("jpegtran", new[] { "-copy", "all", "-crop", $"{w}x{h}+{x}+{y}", "-outfile", dst, src }, ct);
                if (await Size(dst, ct) == (w, h)) return;
            }
        }
        await Engines.Run("magick", new[] { src, "-crop", $"{w}x{h}+{x}+{y}", "+repage", dst }, ct);
    }

    // ---------------- rotate
    /// <summary>JPG: lossless when the sides are whole 16 px blocks; otherwise re-saved once at the original quality. Others: lossless.</summary>
    public static async Task Rotate(string src, string dst, int degrees, CancellationToken ct)
    {
        if (Registry.Ext(src) is "jpg" or "jpeg")
        {
            try { await Engines.Run("jpegtran", new[] { "-copy", "all", "-perfect", "-rotate", degrees.ToString(), "-outfile", dst, src }, ct); return; }
            catch (InvalidOperationException) { /* sides not on the block grid: fall through */ }
        }
        // ImageMagick keeps the source JPG quality when none is given; -auto-orient first so EXIF rotation is not applied twice
        await Engines.Run("magick", new[] { src, "-auto-orient", "-rotate", degrees.ToString(), dst }, ct);
    }

    // ---------------- watermark and meme
    /// <summary>Text in the bottom-right corner, white at 75% with a soft outline, sized to the picture.</summary>
    public static async Task Watermark(string src, string dst, string text, CancellationToken ct)
    {
        var (w, h) = await Size(src, ct);
        await Engines.Run("magick", new[]
        {
            src, "-auto-orient",
            "(", "-size", $"{w * 45 / 100}x{h * 12 / 100}", "-background", "none", "-fill", "rgba(255,255,255,0.75)",
            "-stroke", "rgba(0,0,0,0.35)", "-strokewidth", "1", "-font", "Segoe-UI-Semibold", "-gravity", "east", "caption:" + text, ")",
            "-gravity", "southeast", "-geometry", $"+{w * 3 / 100}+{h * 3 / 100}", "-composite", dst,
        }, ct);
    }

    /// <summary>"TOP | BOTTOM", or one line for the top only. Classic white Impact with a black outline, sized to fit.</summary>
    public static async Task Meme(string src, string dst, string text, CancellationToken ct)
    {
        var (w, h) = await Size(src, ct);
        var parts = text.Split('|', 2);
        string top = parts[0].Trim().ToUpperInvariant(), bottom = parts.Length > 1 ? parts[1].Trim().ToUpperInvariant() : "";
        var args = new List<string> { src, "-auto-orient" };
        foreach (var (line, where) in new[] { (top, "north"), (bottom, "south") })
        {
            if (line.Length == 0) continue;
            args.AddRange(new[] { "(", "-size", $"{w * 94 / 100}x{h * 22 / 100}", "-background", "none", "-font", "Impact", "-fill", "white",
                "-stroke", "black", "-strokewidth", Math.Max(2, w / 250).ToString(), "-gravity", "center", "caption:" + line, ")",
                "-gravity", where, "-geometry", $"+0+{h * 2 / 100}", "-composite" });
        }
        args.Add(dst);
        await Engines.Run("magick", args, ct);
    }

    // ---------------- create
    /// <summary>Lossless: JPG bytes go into the PDF unchanged, PNG is stored losslessly, anything else becomes PNG first.</summary>
    public static async Task ToPdf(IReadOnlyList<string> files, string dst, Action<double> progress, CancellationToken ct)
    {
        var pdf = new PdfDocumentBuilder();
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = files[i];
            var ext = Registry.Ext(f);
            byte[] bytes;
            if (ext is "jpg" or "jpeg" or "png") bytes = await File.ReadAllBytesAsync(f, ct);
            else
            {
                var tmp = Path.Combine(Path.GetTempPath(), "dw-" + Guid.NewGuid().ToString("N") + ".png");
                try { await Engines.Run("magick", new[] { f + "[0]", tmp }, ct); bytes = await File.ReadAllBytesAsync(tmp, ct); ext = "png"; }
                finally { try { File.Delete(tmp); } catch { } }
            }
            BitmapFrame head;
            using (var ms = new MemoryStream(bytes)) head = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            double w = head.PixelWidth * 0.75, h = head.PixelHeight * 0.75; // 96 dpi pixels to PDF points
            var rect = new UglyToad.PdfPig.Core.PdfRectangle(0, 0, w, h);
            var page = pdf.AddPage(w, h);
            if (ext == "png") page.AddPng(bytes, rect); else page.AddJpeg(bytes, rect);
            progress((i + 1.0) / files.Count);
        }
        await File.WriteAllBytesAsync(dst, pdf.Build(), ct);
    }

    /// <summary>Pictures in the order selected, half a second each, looping; later frames are fitted to the first one's size.</summary>
    public static async Task AnimatedGif(IReadOnlyList<string> files, string dst, CancellationToken ct)
    {
        var (w, h) = await Size(files[0], ct);
        var args = new List<string> { "-delay", "50", "-loop", "0" };
        foreach (var f in files) args.AddRange(new[] { "(", f + "[0]", "-auto-orient", "-resize", $"{w}x{h}", "-background", "white", "-gravity", "center", "-extent", $"{w}x{h}", ")" });
        args.AddRange(new[] { "-layers", "Optimize", dst });
        await Engines.Run("magick", args, ct);
    }

    // ---------------- HTML to image (Microsoft Edge, part of Windows)
    public static readonly string[] HtmlIn = { "html", "htm", "url" };

    /// <summary>A web page or .url shortcut as one tall picture, 1280 px wide, blank space at the bottom trimmed.</summary>
    public static async Task HtmlToImage(string src, string dst, CancellationToken ct)
    {
        var target = Registry.Ext(src) == "url"
            ? File.ReadLines(src).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..]
              ?? throw new InvalidOperationException($"{Path.GetFileName(src)} has no web address.")
            : new Uri(Path.GetFullPath(src)).AbsoluteUri;
        var shot = Path.Combine(Path.GetTempPath(), "dw-" + Guid.NewGuid().ToString("N") + ".png");
        var profile = Directory.CreateTempSubdirectory("dw-edge-").FullName;
        try
        {
            await Engines.Run("msedge", new[] { "--headless", "--disable-gpu", "--hide-scrollbars", "--no-first-run", "--user-data-dir=" + profile,
                "--window-size=1280,8000", "--virtual-time-budget=5000", "--screenshot=" + shot, target }, ct);
            if (!File.Exists(shot)) throw new InvalidOperationException("Edge did not produce a picture of that page.");
            await Engines.Run("magick", new[] { shot, "-define", "trim:edges=south", "-trim", "+repage", dst }, ct);
        }
        finally { try { File.Delete(shot); Directory.Delete(profile, true); } catch { } }
    }

    // ---------------- what the bundled ImageMagick can do
    static HashSet<string>? _writable;
    /// <summary>Formats ImageMagick can write (the portable build reads HEIC but cannot write it).</summary>
    public static bool MagickWrites(string ext)
    {
        if (!Engines.Has("magick")) return false;
        if (_writable == null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Plain blocking process call: this runs on the UI thread when a wheel opens, so it must not wait on async work.
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Engines.Find("magick")!, "-list format") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                foreach (var l in p.StandardOutput.ReadToEnd().Split('\n'))
                {
                    var m = Regex.Match(l, @"^\s*([A-Z0-9-]+)\*?\s+(r|-)(w)");
                    if (m.Success) set.Add(m.Groups[1].Value);
                }
                p.WaitForExit(5000);
            }
            catch { }
            _writable = set;
        }
        return _writable.Contains(ext == "jpg" ? "JPEG" : ext.ToUpperInvariant());
    }
}
