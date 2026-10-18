using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Writer;

namespace DropWheel;

/// <summary>
/// iLovePDF-style tools. qpdf rewrites PDFs without touching page content (lossless); Ghostscript does the jobs that
/// must re-render (smaller compression, PDF/A, pages to images). Page numbers and watermarks are drawn on a separate
/// layer and laid over the original pages with qpdf, so the original content is never re-encoded.
/// </summary>
public static class PdfTools
{
    // qpdf exit code 3 means "done, with warnings" (common for slightly broken files)
    static Task Qpdf(IEnumerable<string> args, CancellationToken ct) => Engines.Run("qpdf", args, ct, ok: new[] { 0, 3 });
    static Task Gs(IEnumerable<string> args, CancellationToken ct) =>
        Engines.Run("gswin64c", new[] { "-dNOPAUSE", "-dBATCH", "-dQUIET", "-dSAFER" }.Concat(args), ct);

    static void NeverBigger(string src, string dst)
    {
        if (!File.Exists(dst) || new FileInfo(dst).Length >= new FileInfo(src).Length) File.Copy(src, dst, true);
    }

    static string Tmp(string ext) => Path.Combine(Path.GetTempPath(), "dw-" + Guid.NewGuid().ToString("N") + ext);

    // ---------------- optimize
    /// <summary>Object streams, maximum Flate, old LZW/ASCII filters re-packed. Pages, images and text untouched.</summary>
    public static async Task CompressLossless(string src, string dst, CancellationToken ct)
    {
        await Qpdf(new[] { "--object-streams=generate", "--compress-streams=y", "--recompress-flate", "--compression-level=9", "--decode-level=generalized", src, dst }, ct);
        NeverBigger(src, dst);
    }

    public static string? GsPreset(string a) => a.Trim().ToLowerInvariant() switch
    {
        "low" or "light" or "less" => "/printer",      // images kept at 300 dpi
        "medium" or "recommended" => "/ebook",          // 150 dpi
        "strong" or "high" or "extreme" or "more" => "/screen", // 72 dpi
        _ => null,
    };
    public static string? CheckGsPreset(string a) => GsPreset(a) == null ? "Type low, medium or strong." : null;

    /// <summary>Ghostscript re-writes the file and downsamples images; text and vectors stay sharp. Never returns a bigger file.</summary>
    public static async Task CompressSmaller(string src, string dst, string preset, CancellationToken ct)
    {
        await Gs(new[] { "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.6", "-dPDFSETTINGS=" + preset, "-dDetectDuplicateImages=true", "-sOutputFile=" + dst, src }, ct);
        NeverBigger(src, dst);
    }

    /// <summary>
    /// qpdf rebuilds the cross-reference table and objects. Its result must then pass qpdf --check cleanly; if not (for
    /// example a lost trailer, which qpdf rewrites without /Size), Ghostscript re-writes the file from what it can read.
    /// </summary>
    public static async Task Repair(string src, string dst, CancellationToken ct)
    {
        try
        {
            await Qpdf(new[] { src, dst }, ct);
            await Engines.Run("qpdf", new[] { "--check", dst }, ct); // exit 0 only: warnings mean it is still not right
            return;
        }
        catch (InvalidOperationException) { }
        var tmp = Tmp(".pdf");
        try
        {
            await Gs(new[] { "-sDEVICE=pdfwrite", "-sOutputFile=" + tmp, src }, ct);
            File.Copy(tmp, dst, true);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ---------------- organize
    public static Task Merge(IReadOnlyList<string> files, string dst, CancellationToken ct) =>
        Qpdf(new[] { "--empty", "--pages" }.Concat(files).Concat(new[] { "--", dst }), ct);

    /// <summary>qpdf page ranges: 1-3,5,7-z (z = last page, r1 = last, r2 = second last).</summary>
    static readonly Regex Range = new(@"^\s*(\d+|z|r\d+)(\s*-\s*(\d+|z|r\d+))?\s*$", RegexOptions.IgnoreCase);
    public static string? CheckRanges(string a) => a.Split(',').All(r => Range.IsMatch(r)) ? null : "Use page numbers and ranges, like 1-3, 5, 8-z (z is the last page).";
    static string Clean(string ranges) => string.Join(",", ranges.Split(',').Select(r => Regex.Replace(r, @"\s+", "").ToLowerInvariant()));

    public const string SplitHelp = "Split how: all (one PDF per page), or ranges where each becomes its own PDF, like 1-3, 4-6, 7-z.";
    public static string? CheckSplit(string a) => a.Trim().ToLowerInvariant() is "all" or "every page" ? null : CheckRanges(a);

    public static async Task Split(string src, string dir, string answer, Action<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        if (answer.Trim().ToLowerInvariant() is "all" or "every page")
        {
            await Qpdf(new[] { "--split-pages", src, Path.Combine(dir, "page-%d.pdf") }, ct);
            return;
        }
        var parts = answer.Split(',').Select(r => Regex.Replace(r, @"\s+", "").ToLowerInvariant()).ToList();
        for (int i = 0; i < parts.Count; i++)
        {
            await Qpdf(new[] { "--empty", "--pages", src, parts[i], "--", Path.Combine(dir, $"pages {parts[i]}.pdf") }, ct);
            progress((i + 1.0) / parts.Count);
        }
    }

    public const string OrganizeHelp = "Pages to keep, in the new order, like 3, 1, 2, 5-z. Pages you leave out are removed.";
    public static Task Organize(string src, string dst, string ranges, CancellationToken ct) =>
        Qpdf(new[] { "--empty", "--pages", src, Clean(ranges), "--", dst }, ct);

    public static Task Rotate(string src, string dst, int degrees, CancellationToken ct) =>
        Qpdf(new[] { src, dst, $"--rotate={(degrees > 0 ? "+" : "")}{degrees}" }, ct);

    // ---------------- security (passwords go through a file, never on a command line)
    static async Task WithSecretFile(string content, Func<string, Task> body)
    {
        var f = Tmp(".txt");
        try { await File.WriteAllTextAsync(f, content); await body(f); }
        finally { try { File.Delete(f); } catch { } }
    }

    public static async Task Unlock(string src, string dst, string password, CancellationToken ct)
    {
        try { await WithSecretFile(password, f => Qpdf(new[] { "--password-file=" + f, "--decrypt", src, dst }, ct)); }
        catch (InvalidOperationException e) when (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException(password.Length == 0 ? "This PDF needs its password to open." : "That password is not right."); }
    }

    /// <summary>AES-256. The same password opens the file and allows changes.</summary>
    public static Task Protect(string src, string dst, string password, CancellationToken ct) =>
        // @file: qpdf reads these arguments, one per line, from the file
        WithSecretFile(string.Join("\n", src, "--encrypt", password, password, "256", "--", dst), f => Qpdf(new[] { "@" + f }, ct));
    public static string? CheckPassword(string a) => a.Length < 4 ? "Use at least 4 characters." : a.Contains('\n') ? "One line only." : null;

    // ---------------- edit: a layer drawn by PdfPig, laid over the original pages by qpdf
    static async Task Overlay(string src, string dst, Action<PdfDocumentBuilder, int, double, double, int> draw, CancellationToken ct)
    {
        var layer = Tmp(".pdf");
        try
        {
            using (var doc = PdfDocument.Open(src))
            {
                var b = new PdfDocumentBuilder();
                for (int i = 1; i <= doc.NumberOfPages; i++)
                {
                    var p = doc.GetPage(i);
                    draw(b, i, p.Width, p.Height, doc.NumberOfPages);
                }
                await File.WriteAllBytesAsync(layer, b.Build(), ct);
            }
            await Qpdf(new[] { src, "--overlay", layer, "--", dst }, ct);
        }
        finally { try { File.Delete(layer); } catch { } }
    }

    /// <summary>"1", "1 / 12" style numbers, bottom centre, 10 pt Helvetica.</summary>
    public static Task PageNumbers(string src, string dst, CancellationToken ct) => Overlay(src, dst, (b, n, w, h, total) =>
    {
        var page = b.AddPage(w, h);
        var font = b.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var text = $"{n} / {total}";
        var width = page.MeasureText(text, 10, new UglyToad.PdfPig.Core.PdfPoint(0, 0), font).Sum(l => l.Width);
        page.AddText(text, 10, new UglyToad.PdfPig.Core.PdfPoint((w - width) / 2, 22), font);
    }, ct);

    /// <summary>Your text across the middle of every page, grey at 30%, turned 30°. Rendered once as a transparent PNG.</summary>
    public static async Task Watermark(string src, string dst, string text, CancellationToken ct)
    {
        var png = Tmp(".png");
        try
        {
            await Engines.Run("magick", new[] { "-size", "1600x400", "-background", "none", "-fill", "rgba(110,110,110,0.30)", "-font", "Segoe-UI-Semibold",
                "-gravity", "center", "caption:" + text, "-rotate", "-30", "-trim", "+repage", png }, ct);
            var bytes = await File.ReadAllBytesAsync(png, ct);
            int pw, ph;
            using (var ms = new MemoryStream(bytes)) { var f = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0]; pw = f.PixelWidth; ph = f.PixelHeight; }
            await Overlay(src, dst, (b, n, w, h, total) =>
            {
                double iw = w * 0.75, ih = iw * ph / pw;
                b.AddPage(w, h).AddPng(bytes, new UglyToad.PdfPig.Core.PdfRectangle((w - iw) / 2, (h - ih) / 2, (w + iw) / 2, (h + ih) / 2));
            }, ct);
        }
        finally { try { File.Delete(png); } catch { } }
    }

    public const string CropHelp = "Margin to cut off, in millimetres: one number for every side (10), or top, right, bottom, left (10, 5, 10, 5).";
    public static double[]? Margins(string a)
    {
        var parts = a.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => double.TryParse(p.Replace("mm", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1).ToArray();
        if (parts.Length == 1) parts = new[] { parts[0], parts[0], parts[0], parts[0] };
        return parts.Length == 4 && parts.All(v => v is >= 0 and <= 200) ? parts : null;
    }
    public static string? CheckMargins(string a) => Margins(a) == null ? "Try 10, or 10, 5, 10, 5 (millimetres)." : null;

    /// <summary>Sets each page's crop box through qpdf's JSON update: content is untouched, the cut is only hidden, so it is fully lossless.</summary>
    public static async Task CropMargins(string src, string dst, string answer, CancellationToken ct)
    {
        var mm = Margins(answer)!; double pt(double v) => v * 72 / 25.4;
        var lines = new List<string>();
        await Engines.Run("qpdf", new[] { "--json=2", "--json-key=pages", "--json-key=qpdf", src }, ct, l => lines.Add(l), ok: new[] { 0, 3 });
        var json = JsonNode.Parse(string.Join("\n", lines))!;
        var objects = json["qpdf"]![1]!.AsObject();
        var pages = json["pages"]!.AsArray().Select(p => (string)p!["object"]!).ToList();
        using var doc = PdfDocument.Open(src);
        var update = new JsonObject();
        for (int i = 0; i < pages.Count; i++)
        {
            var key = "obj:" + pages[i];
            var value = objects[key]!["value"]!.DeepClone().AsObject();
            var box = doc.GetPage(i + 1).MediaBox.Bounds; // effective size, including inherited boxes
            double l = box.Left + pt(mm[3]), b = box.Bottom + pt(mm[2]), r = box.Right - pt(mm[1]), t = box.Top - pt(mm[0]);
            if (r - l < 10 || t - b < 10) throw new InvalidOperationException($"Those margins leave nothing of page {i + 1}.");
            value["/CropBox"] = new JsonArray(l, b, r, t);
            update[key] = new JsonObject { ["value"] = value };
        }
        var head = json["qpdf"]![0]!.DeepClone();
        var file = Tmp(".json");
        try
        {
            await File.WriteAllTextAsync(file, new JsonObject { ["qpdf"] = new JsonArray(head, update) }.ToJsonString(), ct);
            await Qpdf(new[] { src, "--update-from-json=" + file, dst }, ct);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    // ---------------- convert
    /// <summary>PDF/A-2b for archiving: fonts embedded, sRGB output intent. Made by Ghostscript; not checked with a validator.</summary>
    public static async Task ToPdfA(string src, string dst, CancellationToken ct)
    {
        var lib = Path.Combine(Path.GetDirectoryName(Engines.Find("gswin64c")!)!, "lib", "PDFA_def.ps");
        var def = Tmp(".ps");
        try
        {
            await File.WriteAllTextAsync(def, (await File.ReadAllTextAsync(lib, ct)).Replace("/ICCProfile (srgb.icc)", "/ICCProfile (%rom%iccprofiles/srgb.icc)"), ct);
            await Gs(new[] { "-dPDFA=2", "-dPDFACompatibilityPolicy=1", "-sColorConversionStrategy=RGB", "-sDEVICE=pdfwrite", "-sOutputFile=" + dst, def, src }, ct);
        }
        finally { try { File.Delete(def); } catch { } }
    }

    /// <summary>Every picture stored in the PDF, as it is stored: JPGs byte for byte, the rest as PNG.</summary>
    public static async Task ExtractImages(string src, string dir, Action<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        int count = 0;
        using var doc = PdfDocument.Open(src);
        for (int n = 1; n <= doc.NumberOfPages; n++)
        {
            ct.ThrowIfCancellationRequested();
            int k = 0;
            foreach (var img in doc.GetPage(n).GetImages())
            {
                k++;
                var name = Path.Combine(dir, $"page-{n}-image-{k}");
                var filters = img.ImageDictionary.ContainsKey(UglyToad.PdfPig.Tokens.NameToken.Filter) ? img.ImageDictionary.Data[UglyToad.PdfPig.Tokens.NameToken.Filter.Data].ToString() ?? "" : "";
                if (filters.Contains("DCTDecode")) await File.WriteAllBytesAsync(name + ".jpg", img.RawBytes.ToArray(), ct);
                else if (img.TryGetPng(out var png)) await File.WriteAllBytesAsync(name + ".png", png, ct);
                else continue;
                count++;
            }
            progress((double)n / doc.NumberOfPages);
        }
        if (count == 0) throw new InvalidOperationException("This PDF has no pictures stored in it.");
    }

    /// <summary>Ghostscript at 200 dpi: PNG is lossless, JPG at quality 95.</summary>
    public static async Task ToImages(string src, string dir, string ext, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var dev = ext == "png" ? new[] { "-sDEVICE=png16m" } : new[] { "-sDEVICE=jpeg", "-dJPEGQ=95" };
        await Gs(dev.Concat(new[] { "-r200", "-dTextAlphaBits=4", "-dGraphicsAlphaBits=4", "-sOutputFile=" + Path.Combine(dir, "page-%d." + ext), src }), ct);
    }

    /// <summary>Text in reading order, page by page. Markdown gets a heading per page; layout and tables are not rebuilt.</summary>
    public static async Task ToText(string src, string dst, bool markdown, CancellationToken ct)
    {
        using var doc = PdfDocument.Open(src);
        var sb = new System.Text.StringBuilder();
        for (int n = 1; n <= doc.NumberOfPages; n++)
        {
            ct.ThrowIfCancellationRequested();
            if (markdown) sb.AppendLine($"## Page {n}").AppendLine();
            sb.AppendLine(ContentOrderTextExtractor.GetText(doc.GetPage(n))).AppendLine();
        }
        await File.WriteAllTextAsync(dst, sb.ToString(), ct);
    }

    /// <summary>Microsoft Edge prints the page to PDF, as its own print dialog would, without the date and URL header.</summary>
    public static async Task HtmlToPdf(string src, string dst, CancellationToken ct)
    {
        var target = Registry.Ext(src) == "url"
            ? File.ReadLines(src).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..]
              ?? throw new InvalidOperationException($"{Path.GetFileName(src)} has no web address.")
            : new Uri(Path.GetFullPath(src)).AbsoluteUri;
        var profile = Directory.CreateTempSubdirectory("dw-edge-").FullName;
        try
        {
            File.Delete(dst);
            await Engines.Run("msedge", new[] { "--headless", "--disable-gpu", "--no-first-run", "--user-data-dir=" + profile, "--no-pdf-header-footer",
                "--virtual-time-budget=5000", "--print-to-pdf=" + dst, target }, ct);
            if (!File.Exists(dst)) throw new InvalidOperationException("Edge did not produce a PDF of that page.");
        }
        finally { try { Directory.Delete(profile, true); } catch { } }
    }
}
