using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace DropWheel;

using RunFn = Func<string, string, Action<double>, CancellationToken, Task>;

/// <summary>
/// Every operation the wheel can offer, built from format tables rather than written one by one.
/// Native paths (Windows imaging, .NET archives, PdfSharp) need nothing installed; the rest use an
/// engine that is hidden from the wheel until it is found (see Engines.Find).
/// </summary>
public static class Registry
{
    public static string Ext(string p)
    {
        if (Directory.Exists(p)) return "dir";
        var n = Path.GetFileName(p).ToLowerInvariant();
        return n.EndsWith(".tar.gz") ? "tar.gz" : Path.GetExtension(n).TrimStart('.');
    }

    static Func<string, bool> In(params string[] exts) => p => exts.Contains(Ext(p));
    static Func<string, bool> InNot(string[] exts, string not) => p => { var e = Ext(p); return exts.Contains(e) && !Same(e, not); };
    static bool Same(string a, string b) => a == b || (a, b) is ("jpeg", "jpg") or ("jpg", "jpeg") or ("tif", "tiff") or ("tiff", "tif") or ("aif", "aiff") or ("aiff", "aif") or ("tgz", "tar.gz");
    static string Up(string e) => e.ToUpperInvariant();

    static readonly string[] ImgIn = { "jpg", "jpeg", "png", "bmp", "tif", "tiff", "gif", "webp", "avif", "heic", "heif", "svg" };
    static readonly string[] VideoIn = { "mp4", "mov", "mkv", "webm", "avi", "wmv", "flv", "m4v", "mpg", "mpeg" };
    static readonly string[] AudioIn = { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "aif", "wma", "aac" };
    static readonly string[] DocIn = { "md", "markdown", "txt", "html", "htm", "docx", "epub", "rtf", "odt" };
    static readonly string[] OfficeIn = { "doc", "docx", "odt", "rtf", "ppt", "pptx", "odp", "xls", "xlsx", "ods", "csv", "tsv" };
    static readonly string[] SheetIn = { "xls", "xlsx", "ods", "csv", "tsv" };
    static readonly string[] SlideIn = { "ppt", "pptx", "odp" };
    static readonly string[] ArchiveIn = { "zip", "tar", "tar.gz", "tgz", "gz", "rar", "7z" };

    public static List<Op> All()
    {
        var ops = new List<Op>();
        bool ffmpeg = Engines.Has("ffmpeg"), magick = Engines.Has("magick"), pandoc = Engines.Has("pandoc"),
             soffice = Engines.Has("soffice"), sevenZ = Engines.Has("7z"), poppler = Engines.Has("pdftoppm"),
             gs = Engines.Has("gswin64c") || Engines.Has("gswin32c");

        // ---- images
        var imgIn = ImgIn.Where(e => magick || e != "svg").ToArray();
        foreach (var o in new[] { "jpg", "png", "bmp", "tiff", "gif", "webp", "avif", "heic", "pdf" })
        {
            bool native = o is "jpg" or "png" or "bmp" or "tiff" or "gif";
            ops.Add(new Op("img>" + o, OpKind.Convert, Up(o), "Image", $"Convert to {Up(o)}", InNot(imgIn, o), o,
                (s, d, p, ct) => ImageConvert(s, d, o, ct), native || magick, native ? null : "ImageMagick"));
        }
        // ---- video + audio
        foreach (var o in new[] { "mp4", "mov", "mkv", "webm", "avi", "wmv", "gif" })
            ops.Add(new Op("vid>" + o, OpKind.Convert, Up(o), "Video", $"Convert to {Up(o)}", InNot(VideoIn, o), o,
                (s, d, p, ct) => Ffmpeg(s, d, VideoArgs(o), p, ct), ffmpeg, "ffmpeg"));
        foreach (var o in new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma" })
        {
            ops.Add(new Op("aud>" + o, OpKind.Convert, Up(o), "Audio", $"Convert to {Up(o)}", InNot(AudioIn, o), o,
                (s, d, p, ct) => Ffmpeg(s, d, AudioArgs(o), p, ct), ffmpeg, "ffmpeg"));
            if (o is "mp3" or "m4a" or "wav")
                ops.Add(new Op("vid>" + o, OpKind.Convert, Up(o), "Video", $"Extract audio as {Up(o)}", In(VideoIn), o,
                    (s, d, p, ct) => Ffmpeg(s, d, AudioArgs(o), p, ct), ffmpeg, "ffmpeg"));
        }
        // ---- subtitles
        foreach (var o in new[] { "srt", "vtt" })
            ops.Add(new Op("sub>" + o, OpKind.Convert, Up(o), "Subtitles", $"Convert to {Up(o)}", InNot(new[] { "srt", "vtt" }, o), o,
                (s, d, p, ct) => Ffmpeg(s, d, Array.Empty<string>(), p, ct), ffmpeg, "ffmpeg"));
        ops.Add(new Op("sub>txt", OpKind.Convert, "TXT", "Subtitles", "Text only", In("srt", "vtt"), "txt",
            (s, d, p, ct) => Task.Run(() => File.WriteAllText(d, SubtitleText(File.ReadAllLines(s))), ct)));
        // ---- text documents (pandoc)
        foreach (var o in new[] { "docx", "html", "md", "txt", "epub", "odt", "rtf" })
            ops.Add(new Op("doc>" + o, OpKind.Convert, Up(o), "Document", $"Convert to {Up(o)}", InNot(DocIn, o), o,
                (s, d, p, ct) => Pandoc(s, d, o, ct), pandoc, "pandoc"));
        // ---- office documents, slides, sheets (LibreOffice)
        ops.Add(new Op("off>pdf", OpKind.Convert, "PDF", "Office", "Convert to PDF", In(OfficeIn), "pdf",
            (s, d, p, ct) => Soffice(s, d, "pdf", null, ct), soffice, "LibreOffice"));
        foreach (var o in new[] { "docx", "doc" })
            ops.Add(new Op("off>" + o, OpKind.Convert, Up(o), "Office", $"Convert to {Up(o)}", InNot(new[] { "doc", "docx", "odt", "rtf" }, o), o,
                (s, d, p, ct) => Soffice(s, d, o, null, ct), soffice, "LibreOffice"));
        foreach (var o in new[] { "xlsx", "ods", "csv" })
            ops.Add(new Op("sheet>" + o, OpKind.Convert, Up(o), "Spreadsheet", $"Convert to {Up(o)}", InNot(SheetIn, o), o,
                (s, d, p, ct) => Soffice(s, d, o, null, ct), soffice, "LibreOffice"));
        ops.Add(new Op("sheet>tsv", OpKind.Convert, "TSV", "Spreadsheet", "Convert to TSV", InNot(SheetIn, "tsv"), "tsv",
            (s, d, p, ct) => Soffice(s, d, "csv:Text - txt - csv (StarCalc):9,34,76,1", null, ct), soffice, "LibreOffice"));
        foreach (var o in new[] { "jpg", "png" })
        {
            ops.Add(new Op("slide>" + o, OpKind.Convert, Up(o), "Slides", $"Every slide as {Up(o)}", In(SlideIn), "",
                (s, d, p, ct) => SlidesToImages(s, d, o, ct), soffice && poppler, "LibreOffice + Poppler") { IsFolderOut = true, Suffix = " (slides)" });
        }
        // ---- PDF out
        foreach (var o in new[] { "jpg", "png" })
            ops.Add(new Op("pdf>" + o, OpKind.Convert, Up(o), "PDF", $"One {Up(o)} per page", In("pdf"), "",
                (s, d, p, ct) => PdfToImages(s, d, o, ct), poppler, "Poppler") { IsFolderOut = true, Suffix = " (pages)" });
        ops.Add(new Op("pdf>txt", OpKind.Convert, "TXT", "PDF", "Extract text", In("pdf"), "txt",
            (s, d, p, ct) => Engines.Run("pdftotext", new[] { "-layout", s, d }, ct), Engines.Has("pdftotext"), "Poppler"));
        ops.Add(new Op("pdf>md", OpKind.Convert, "MD", "PDF", "Extract text as Markdown (layout not kept)", In("pdf"), "md",
            (s, d, p, ct) => Engines.Run("pdftotext", new[] { s, d }, ct), Engines.Has("pdftotext"), "Poppler"));
        ops.Add(new Op("pdf>docx", OpKind.Convert, "DOCX", "PDF", "Best effort: layout may shift", In("pdf"), "docx",
            (s, d, p, ct) => Soffice(s, d, "docx:MS Word 2007 XML", "writer_pdf_import", ct), soffice, "LibreOffice"));
        ops.Add(new Op("pdf>epub", OpKind.Convert, "EPUB", "PDF", "Best effort: layout is lost", In("pdf"), "epub",
            (s, d, p, ct) => Soffice(s, d, "epub", "writer_pdf_import", ct), soffice, "LibreOffice"));
        ops.Add(new Op("pdf>pptx", OpKind.Convert, "PPTX", "PDF", "Best effort: pages become slides", In("pdf"), "pptx",
            (s, d, p, ct) => Soffice(s, d, "pptx", "impress_pdf_import", ct), soffice, "LibreOffice"));
        // ---- archives
        var archIn = ArchiveIn.Where(e => sevenZ || e is not ("rar" or "7z")).ToArray();
        foreach (var o in new[] { "zip", "tar", "tar.gz" })
            ops.Add(new Op("arc>" + o, OpKind.Convert, Up(o), "Archive", $"Repack as {Up(o)}", InNot(archIn, o), o,
                (s, d, p, ct) => ArchiveRepack(s, d, o, ct)));

        // ---- tools
        ops.Add(new Op("t.img.compress", OpKind.Tool, "Compress", "Image", "Smaller JPG (quality 72)",
            In("jpg", "jpeg", "png", "bmp", "tif", "tiff", "gif"), "jpg",
            (s, d, p, ct) => Task.Run(() => Wic.Convert(s, d, "jpg", 72), ct)) { Suffix = "-compressed" });
        ops.Add(new Op("t.img.strip", OpKind.Tool, "Strip metadata", "Image", "Re-save without EXIF or location",
            In("jpg", "jpeg", "png", "tif", "tiff"), "", (s, d, p, ct) => Task.Run(() => Wic.Convert(s, d, Ext(d), 95, strip: true), ct))
        { Suffix = "-clean", SameExt = true });
        ops.Add(new Op("t.vid.compress", OpKind.Tool, "Compress", "Video", "Smaller MP4 (H.264, CRF 28)", In(VideoIn), "mp4",
            (s, d, p, ct) => Ffmpeg(s, d, new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "28", "-preset", "medium", "-c:a", "aac", "-b:a", "96k" }, p, ct),
            ffmpeg, "ffmpeg") { Suffix = "-compressed" });
        ops.Add(new Op("t.aud.compress", OpKind.Tool, "Compress", "Audio", "Smaller MP3 (96 kbps)", In(AudioIn), "mp3",
            (s, d, p, ct) => Ffmpeg(s, d, new[] { "-vn", "-c:a", "libmp3lame", "-b:a", "96k" }, p, ct), ffmpeg, "ffmpeg") { Suffix = "-compressed" });
        ops.Add(new Op("t.pdf.compress", OpKind.Tool, "Compress", "PDF", "Smaller PDF (Ghostscript)", In("pdf"), "pdf",
            (s, d, p, ct) => Engines.Run(Engines.Has("gswin64c") ? "gswin64c" : "gswin32c",
                new[] { "-sDEVICE=pdfwrite", "-dPDFSETTINGS=/ebook", "-dNOPAUSE", "-dBATCH", "-dQUIET", "-sOutputFile=" + d, s }, ct),
            gs, "Ghostscript") { Suffix = "-compressed" });
        ops.Add(new Op("t.pdf.strip", OpKind.Tool, "Strip metadata", "PDF", "Clear title, author, subject, keywords", In("pdf"), "pdf",
            (s, d, p, ct) => Task.Run(() => PdfStrip(s, d), ct)) { Suffix = "-clean" });
        ops.Add(new Op("t.pdf.split", OpKind.Tool, "Split pages", "PDF", "One PDF per page", In("pdf"), "",
            (s, d, p, ct) => Task.Run(() => PdfSplit(s, d, p, ct), ct)) { IsFolderOut = true, Suffix = " (split)" });
        ops.Add(new Op("t.pdf.merge", OpKind.Tool, "Merge", "PDF", "Join PDFs in the order selected", In("pdf"), "pdf",
            (s, d, p, ct) => Task.Run(() => PdfMerge(s.Split('\n'), d), ct)) { Batch = true, MinFiles = 2, Suffix = "-merged" });
        ops.Add(new Op("t.pdf.redact", OpKind.Tool, "Redact", "PDF", "Black out words you name; pages become images", In("pdf"), "pdf",
            (s, d, p, ct) => Task.CompletedTask) { Suffix = "-redacted", Prompt = "Words or phrases to black out, separated by commas.",
            RunWith = (s, d, terms, p, ct) => PdfRedact(s, d, terms, p, ct) });
        ops.Add(new Op("t.zip", OpKind.Tool, "Zip", "Archive", "Put the selection in one ZIP", p => true, "zip",
            (s, d, p, ct) => Task.Run(() => ZipPaths(s.Split('\n'), d), ct)) { Batch = true });
        ops.Add(new Op("t.unpack", OpKind.Tool, "Unpack", "Archive", "Extract into a folder", In(archIn), "",
            (s, d, p, ct) => Extract(s, d, ct)) { IsFolderOut = true });

        ops.AddRange(Actions.All());
        return ops;
    }

    public static List<Op> For(IReadOnlyList<string> paths, OpKind kind) =>
        All().Where(o => o.Kind == kind && o.Available && paths.Count >= o.MinFiles && paths.All(o.Accepts)).ToList();

    /// <summary>Engines that would unlock more operations, for the Settings page.</summary>
    public static List<(string engine, int ops)> Missing() =>
        All().Where(o => !o.Available && o.Needs != null).GroupBy(o => o.Needs!).Select(g => (g.Key, g.Count())).ToList();

    // ---------------- images
    static async Task ImageConvert(string src, string dst, string o, CancellationToken ct)
    {
        bool native = o is "jpg" or "png" or "bmp" or "tiff" or "gif";
        if (native && Ext(src) != "svg")
        {
            try { await Task.Run(() => Wic.Convert(src, dst, o, 90), ct); return; }
            catch when (Engines.Has("magick")) { /* codec missing in Windows: retry with ImageMagick */ }
        }
        var args = new List<string> { Ext(src) == "gif" && o != "gif" ? src + "[0]" : src };
        if (o is "jpg" or "bmp") args.AddRange(new[] { "-background", "white", "-alpha", "remove", "-alpha", "off" });
        args.Add(dst);
        await Engines.Run("magick", args, ct);
    }

    // ---------------- ffmpeg
    static string[] VideoArgs(string o) => o switch
    {
        "gif" => new[] { "-vf", "fps=12,scale='min(480,iw)':-1:flags=lanczos", "-an" },
        "webm" => new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-c:a", "libopus" },
        "mp4" or "mov" or "mkv" => new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23", "-c:a", "aac" },
        _ => Array.Empty<string>(),
    };

    static string[] AudioArgs(string o) => o switch
    {
        "mp3" => new[] { "-vn", "-q:a", "2" },
        "m4a" => new[] { "-vn", "-c:a", "aac", "-b:a", "192k" },
        "ogg" => new[] { "-vn", "-c:a", "libvorbis", "-q:a", "5" },
        "opus" => new[] { "-vn", "-c:a", "libopus", "-b:a", "128k" },
        _ => new[] { "-vn" },
    };

    static Task Ffmpeg(string src, string dst, string[] extra, Action<double> progress, CancellationToken ct)
    {
        double total = 0;
        var args = new List<string> { "-y", "-hide_banner", "-i", src };
        args.AddRange(extra);
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", dst });
        return Engines.Run("ffmpeg", args, ct, line =>
        {
            var m = Regex.Match(line, @"Duration:\s*(\d+):(\d+):([\d.]+)");
            if (m.Success)
                total = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            else if (line.StartsWith("out_time_us=") && total > 0 && long.TryParse(line[12..], out var us))
                progress(Math.Clamp(us / 1e6 / total, 0, 1));
        });
    }

    // ---------------- documents
    static Task Pandoc(string src, string dst, string o, CancellationToken ct)
    {
        var args = new List<string> { src, "-o", dst };
        if (o == "md") args.AddRange(new[] { "-t", "gfm" });
        if (o == "txt") args.AddRange(new[] { "-t", "plain" });
        if (o == "epub") args.AddRange(new[] { "--metadata", "title=" + Path.GetFileNameWithoutExtension(src) });
        if (Ext(src) == "html" || Ext(src) == "htm") args.AddRange(new[] { "-f", "html" });
        return Engines.Run("pandoc", args, ct);
    }

    static async Task Soffice(string src, string dst, string fmt, string? infilter, CancellationToken ct)
    {
        var tmp = Directory.CreateTempSubdirectory("dw-");
        try
        {
            var outDir = Path.Combine(tmp.FullName, "out");
            Directory.CreateDirectory(outDir);
            var args = new List<string>
            {
                "--headless", "--norestore", "--nolockcheck",
                "-env:UserInstallation=" + new Uri(Path.Combine(tmp.FullName, "profile")).AbsoluteUri,
            };
            if (infilter != null) args.Add("--infilter=" + infilter);
            args.AddRange(new[] { "--convert-to", fmt, "--outdir", outDir, src });
            await Engines.Run("soffice", args, ct);
            var made = Directory.GetFiles(outDir).FirstOrDefault() ?? throw new InvalidOperationException("LibreOffice produced no file");
            File.Move(made, dst, true);
        }
        finally { try { tmp.Delete(true); } catch { } }
    }

    static async Task PdfToImages(string src, string dstDir, string o, CancellationToken ct)
    {
        Directory.CreateDirectory(dstDir);
        await Engines.Run("pdftoppm", new[] { o == "jpg" ? "-jpeg" : "-png", "-r", "150", src, Path.Combine(dstDir, "page") }, ct);
    }

    static async Task SlidesToImages(string src, string dstDir, string o, CancellationToken ct)
    {
        var pdf = Path.Combine(Path.GetTempPath(), "dw-" + Guid.NewGuid().ToString("N") + ".pdf");
        try { await Soffice(src, pdf, "pdf", null, ct); await PdfToImages(pdf, dstDir, o, ct); }
        finally { try { File.Delete(pdf); } catch { } }
    }

    static string SubtitleText(string[] lines)
    {
        var keep = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("-->") && l != "WEBVTT" && !Regex.IsMatch(l, @"^\d+$"))
                        .Select(l => Regex.Replace(l, "<[^>]+>", ""));
        return string.Join(Environment.NewLine, keep);
    }

    // ---------------- PDF (PdfPig, Apache-2.0, no engine needed)
    // PdfPig's merger writes a fresh document, so merge, split and "strip metadata" are all one call; the
    // original title/author/keywords do not carry over.
    static void PdfMerge(string[] files, string dst) =>
        File.WriteAllBytes(dst, PdfMerger.Merge(files.Select(File.ReadAllBytes).ToList()));

    static void PdfSplit(string src, string dstDir, Action<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dstDir);
        var bytes = File.ReadAllBytes(src);
        int pages;
        using (var d = PdfDocument.Open(bytes)) pages = d.NumberOfPages;
        for (int i = 1; i <= pages; i++)
        {
            ct.ThrowIfCancellationRequested();
            File.WriteAllBytes(Path.Combine(dstDir, $"page-{i}.pdf"),
                PdfMerger.Merge(new List<byte[]> { bytes }, new List<IReadOnlyList<int>> { new[] { i } }));
            progress((double)i / pages);
        }
    }

    /// <summary>
    /// Real redaction: each page is drawn to pixels, the matching words are painted black in the pixels, and the
    /// pages are written back as images, so the hidden text no longer exists in the file. (Windows.Data.Pdf draws
    /// the page, PdfPig finds the words.) ponytail: matches word runs, ignores page rotation; add regex and
    /// rotation if a customer needs them.
    /// </summary>
    public static async Task PdfRedact(string src, string dst, string terms, Action<double> progress, CancellationToken ct)
    {
        const double Scale = 2; // 144 dpi
        var wanted = terms.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(t => t.Length > 0).ToList();
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(src));
        var render = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        var builder = new PdfDocumentBuilder();
        int hits = 0;
        using var doc = UglyToad.PdfPig.PdfDocument.Open(File.ReadAllBytes(src));
        for (int n = 1; n <= doc.NumberOfPages; n++)
        {
            ct.ThrowIfCancellationRequested();
            var page = doc.GetPage(n);
            var words = page.GetWords().ToList();
            var boxes = new List<(double l, double t, double r, double b)>(); // PDF points, origin top-left
            foreach (var term in wanted)
                for (int i = 0; i + term.Length <= words.Count; i++)
                {
                    bool match = true;
                    for (int k = 0; k < term.Length && match; k++)
                        match = words[i + k].Text.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"').Equals(term[k], StringComparison.OrdinalIgnoreCase);
                    if (!match) continue;
                    hits++;
                    for (int k = 0; k < term.Length; k++)
                    {
                        var r = words[i + k].BoundingBox;
                        boxes.Add((r.Left - 1, page.Height - r.Top - 1, r.Right + 1, page.Height - r.Bottom + 1));
                    }
                }

            using var rp = render.GetPage((uint)(n - 1));
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await rp.RenderToStreamAsync(ms, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)Math.Round(page.Width * Scale) });
            ms.Seek(0);
            var frame = BitmapDecoder.Create(ms.AsStreamForRead(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var rgb = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
            int w = rgb.PixelWidth, h = rgb.PixelHeight, stride = w * 3;
            var px = new byte[stride * h];
            rgb.CopyPixels(px, stride, 0);
            double sx = w / page.Width, sy = h / page.Height;
            foreach (var (l, t, r, b) in boxes)
            {
                int x0 = Math.Max(0, (int)(l * sx)), x1 = Math.Min(w, (int)Math.Ceiling(r * sx));
                for (int y = Math.Max(0, (int)(t * sy)); y < Math.Min(h, (int)Math.Ceiling(b * sy)); y++)
                    if (x1 > x0) Array.Clear(px, y * stride + x0 * 3, (x1 - x0) * 3);
            }
            var enc = new JpegBitmapEncoder { QualityLevel = 88 };
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, px, stride)));
            using var jpg = new MemoryStream(); enc.Save(jpg);
            builder.AddPage(page.Width, page.Height).AddJpeg(jpg.ToArray(), new UglyToad.PdfPig.Core.PdfRectangle(0, 0, page.Width, page.Height));
            progress((double)n / doc.NumberOfPages);
        }
        if (hits == 0) throw new InvalidOperationException("None of those words were found, so nothing was redacted. Scanned pages have no text to search.");
        File.WriteAllBytes(dst, builder.Build());
    }

    static void PdfStrip(string src, string dst) =>
        File.WriteAllBytes(dst, PdfMerger.Merge(new List<byte[]> { File.ReadAllBytes(src) }));

    // ---------------- archives (.NET built-ins; 7-Zip only for RAR/7z input)
    static async Task Extract(string src, string dir, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        switch (Ext(src))
        {
            case "zip": await Task.Run(() => ZipFile.ExtractToDirectory(src, dir), ct); break;
            case "tar": await Task.Run(() => TarFile.ExtractToDirectory(src, dir, true), ct); break;
            case "tar.gz" or "tgz":
                await Task.Run(() =>
                {
                    using var f = File.OpenRead(src);
                    using var g = new GZipStream(f, CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(g, dir, true);
                }, ct);
                break;
            case "gz":
                await Task.Run(() =>
                {
                    using var f = File.OpenRead(src);
                    using var g = new GZipStream(f, CompressionMode.Decompress);
                    using var o = File.Create(Path.Combine(dir, Path.GetFileNameWithoutExtension(src)));
                    g.CopyTo(o);
                }, ct);
                break;
            default: await Engines.Run("7z", new[] { "x", "-y", "-o" + dir, src }, ct); break;
        }
    }

    static async Task ArchiveRepack(string src, string dst, string o, CancellationToken ct)
    {
        var tmp = Directory.CreateTempSubdirectory("dw-");
        try
        {
            await Extract(src, tmp.FullName, ct);
            await Task.Run(() =>
            {
                switch (o)
                {
                    case "zip": using (var f = File.Create(dst)) ZipFile.CreateFromDirectory(tmp.FullName, f); break;
                    case "tar": using (var f = File.Create(dst)) TarFile.CreateFromDirectory(tmp.FullName, f, false); break;
                    default:
                        using (var f = File.Create(dst))
                        using (var g = new GZipStream(f, CompressionLevel.Optimal))
                            TarFile.CreateFromDirectory(tmp.FullName, g, false);
                        break;
                }
            }, ct);
        }
        finally { try { tmp.Delete(true); } catch { } }
    }

    static void ZipPaths(string[] paths, string dst)
    {
        using var fs = File.Create(dst); // dst is a name already reserved on disk, so Create-mode ZipFile.Open would refuse it
        using var z = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var p in paths)
        {
            if (File.Exists(p)) z.CreateEntryFromFile(p, Path.GetFileName(p));
            else
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    z.CreateEntryFromFile(f, Path.Combine(Path.GetFileName(p), Path.GetRelativePath(p, f)));
        }
    }
}

/// <summary>Image encode/decode through Windows Imaging Component: JPG, PNG, BMP, TIFF, GIF with no engine installed.</summary>
public static class Wic
{
    public static void Convert(string src, string dst, string outExt, int quality, bool strip = false)
    {
        var name = Path.GetFileName(src);
        BitmapDecoder dec;
        FileStream fs;
        try { fs = File.OpenRead(src); }
        catch (FileNotFoundException) { throw new InvalidOperationException($"{name} no longer exists."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException($"{name} is open in another program or cannot be read."); }
        using (fs)
        {
            try { dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); }
            catch (Exception)
            {
                var ext = Registry.Ext(src);
                throw new InvalidOperationException(ext is "heic" or "heif" or "webp" or "avif"
                    ? $"Windows cannot read {ext.ToUpperInvariant()} files yet. Install the {ext.ToUpperInvariant()} Image Extension from the Microsoft Store, or ImageMagick."
                    : $"{name} is damaged or is not a real {ext.ToUpperInvariant()} image.");
            }
        }

        try { Encode(dec, dst, outExt, quality, keepMeta: !strip); }
        catch when (!strip) { Encode(dec, dst, outExt, quality, keepMeta: false); } // metadata that the target format cannot hold
    }

    static void Encode(BitmapDecoder dec, string dst, string outExt, int quality, bool keepMeta)
    {
        BitmapEncoder enc = outExt switch
        {
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = quality },
            "png" => new PngBitmapEncoder(),
            "bmp" => new BmpBitmapEncoder(),
            "tif" or "tiff" => new TiffBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            _ => throw new InvalidOperationException("No native encoder for ." + outExt),
        };
        bool flatten = outExt is "jpg" or "jpeg" or "bmp";
        int count = outExt is "gif" or "tif" or "tiff" ? dec.Frames.Count : 1;
        for (int i = 0; i < count; i++)
        {
            BitmapSource frame = dec.Frames[i];
            if (flatten) frame = OnWhite(frame);
            enc.Frames.Add(keepMeta && frame == dec.Frames[i]
                ? BitmapFrame.Create(frame, null, dec.Frames[i].Metadata as BitmapMetadata, dec.Frames[i].ColorContexts)
                : BitmapFrame.Create(frame));
        }
        using var o = File.Create(dst);
        enc.Save(o);
    }

    /// <summary>Alpha is flattened onto white, because JPG and BMP cannot hold it and black is the wrong default.</summary>
    static BitmapSource OnWhite(BitmapSource s)
    {
        var bgra = new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var px = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            px[i] = (byte)((px[i] * a + 255 * (255 - a)) / 255);
            px[i + 1] = (byte)((px[i + 1] * a + 255 * (255 - a)) / 255);
            px[i + 2] = (byte)((px[i + 2] * a + 255 * (255 - a)) / 255);
            px[i + 3] = 255;
        }
        var flat = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, s.DpiX, s.DpiY, PixelFormats.Bgra32, null, px, stride);
        flat.Freeze();
        return new FormatConvertedBitmap(flat, PixelFormats.Bgr24, null, 0);
    }
}
