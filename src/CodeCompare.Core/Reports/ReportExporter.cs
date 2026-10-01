using System.Globalization;
using System.Net;
using System.Text;
using CodeCompare.Core.Comparison;
using CodeCompare.Core.Diff;
using CodeCompare.Core.IO;

namespace CodeCompare.Core.Reports;

public static class ReportExporter
{
    private const int MaxReportRowsPerFile = 3000;

    public static void ExportCsv(IEnumerable<FileComparison> files, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Status,Path,Language,Left size,Right size,Left modified,Right modified," +
                         "Left lines,Left code,Left comments,Left blank,Right lines,Right code,Right comments,Right blank," +
                         "Code delta,Added,Deleted,Modified,Note");
        foreach (var f in files)
        {
            writer.WriteLine(string.Join(",",
                Csv(f.StatusText), Csv(f.RelativePath), Csv(f.Language),
                Num(f.LeftSize), Num(f.RightSize), Date(f.LeftModified), Date(f.RightModified),
                Num(f.LeftLoc?.Total), Num(f.LeftLoc?.Code), Num(f.LeftLoc?.Comment), Num(f.LeftLoc?.Blank),
                Num(f.RightLoc?.Total), Num(f.RightLoc?.Code), Num(f.RightLoc?.Comment), Num(f.RightLoc?.Blank),
                Num(f.CodeDelta), f.Added, f.Deleted, f.Modified, Csv(f.Note ?? "")));
        }
    }

    /// <summary>Writes a git-style unified diff of every changed text file.</summary>
    public static void ExportPatch(IEnumerable<FileComparison> files, string path, CancellationToken cancellationToken = default)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (f.Status is FileStatus.Identical or FileStatus.Error || f.IsBinary) continue;

            string rel = f.RelativePath.Replace('\\', '/');
            var left = f.LeftPath is null ? TextFile.Empty : TextFile.Load(f.LeftPath);
            var right = f.RightPath is null ? TextFile.Empty : TextFile.Load(f.RightPath);
            if (left.IsBinary || right.IsBinary) continue;

            var diff = DiffEngine.Compare(left.Lines, right.Lines, TextCompareOptions.Exact);
            if (diff.Blocks.Count == 0) continue;

            writer.WriteLine($"diff --git a/{rel} b/{rel}");
            UnifiedDiff.Write(writer,
                f.LeftPath is null ? "/dev/null" : $"a/{rel}",
                f.RightPath is null ? "/dev/null" : $"b/{rel}",
                left.Lines, right.Lines, diff);
        }
    }

    public static void ExportHtml(CompareResult result, IReadOnlyCollection<FileComparison> files, string path,
                                  bool includeDiffs, CancellationToken cancellationToken = default)
    {
        var summary = CompareSummary.Compute(files);
        var o = result.Options;
        var sb = new StringBuilder();

        sb.Append("""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Code Compare Report</title>
            <style>
            :root{--bg:#fff;--fg:#1f2328;--muted:#656d76;--line:#d0d7de;--card:#f6f8fa;--del:#ffebe9;--delx:#ffc1bd;--add:#e6ffec;--addx:#abf2bc;--empty:#f3f4f6}
            @media (prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#8d96a0;--line:#30363d;--card:#161b22;--del:#3d1d20;--delx:#7a2d32;--add:#13301d;--addx:#24633a;--empty:#1c2128}}
            body{font:14px/1.45 "Segoe UI",system-ui,sans-serif;margin:0;padding:24px;background:var(--bg);color:var(--fg)}
            h1{margin:0 0 4px;font-size:22px}h2{margin:32px 0 12px;font-size:17px}.muted{color:var(--muted)}
            .cards{display:flex;flex-wrap:wrap;gap:12px;margin-top:16px}.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px 16px;min-width:120px}
            .card b{display:block;font-size:20px}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid var(--line);padding:5px 8px;text-align:left;vertical-align:top}
            th{background:var(--card);position:sticky;top:0}td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}
            .pill{border-radius:10px;padding:1px 8px;font-size:12px;white-space:nowrap}.Identical{background:#eaeef2;color:#424a53}.Different{background:#fff1e5;color:#953800}
            .LeftOnly{background:#ffebe9;color:#a40e26}.RightOnly{background:#dafbe1;color:#116329}.Error{background:#eee;color:#555}
            .pos{color:#1a7f37}.neg{color:#cf222e}details{border:1px solid var(--line);border-radius:8px;margin:10px 0;overflow:hidden}
            summary{cursor:pointer;padding:8px 12px;background:var(--card);font-weight:600}
            .diff{font:12px/1.4 Consolas,"Cascadia Mono",monospace;table-layout:fixed}.diff td{border:0;padding:0 6px;white-space:pre-wrap;word-break:break-all}
            .diff td.ln{width:48px;color:var(--muted);text-align:right;user-select:none}.diff .d{background:var(--del)}.diff .a{background:var(--add)}.diff .e{background:var(--empty)}
            .diff mark.x{background:var(--delx);color:inherit}.diff .a mark.x{background:var(--addx)}.diff tr.sep td{background:var(--card);color:var(--muted);text-align:center}
            </style></head><body>
            """);

        sb.Append("<h1>Code Compare Report</h1>");
        sb.Append($"<div class=\"muted\">Generated {DateTime.Now:yyyy-MM-dd HH:mm} · compared in {result.Elapsed.TotalSeconds:N1}s</div>");
        sb.Append($"<p><b>Left:</b> {E(o.LeftRoot)}<br><b>Right:</b> {E(o.RightRoot)}</p>");

        sb.Append("<div class=\"cards\">");
        Card(sb, "Files compared", summary.Total);
        Card(sb, "Identical", summary.Identical);
        Card(sb, "Different", summary.Different);
        Card(sb, "Left only", summary.LeftOnly);
        Card(sb, "Right only", summary.RightOnly);
        Card(sb, "Code lines (left)", summary.Left.Code);
        Card(sb, "Code lines (right)", summary.Right.Code);
        sb.Append($"<div class=\"card\">Net code change<b class=\"{(summary.CodeDelta >= 0 ? "pos" : "neg")}\">{Signed(summary.CodeDelta)}</b></div>");
        sb.Append($"<div class=\"card\">Lines changed<b><span class=\"pos\">+{summary.Added:N0}</span> <span class=\"neg\">−{summary.Deleted:N0}</span> ~{summary.Modified:N0}</b></div>");
        sb.Append("</div>");

        sb.Append("<h2>Lines of code by language</h2><table><tr><th>Language</th><th>Extensions</th><th class=n>Files L</th><th class=n>Files R</th>" +
                  "<th class=n>Code L</th><th class=n>Code R</th><th class=n>Δ Code</th><th class=n>Comments L</th><th class=n>Comments R</th>" +
                  "<th class=n>Blank L</th><th class=n>Blank R</th><th class=n>+Added</th><th class=n>−Deleted</th><th class=n>~Modified</th></tr>");
        foreach (var s in summary.Languages)
        {
            sb.Append($"<tr><td>{E(s.Language)}</td><td class=muted>{E(s.Extensions)}</td><td class=n>{s.LeftFiles:N0}</td><td class=n>{s.RightFiles:N0}</td>" +
                      $"<td class=n>{s.Left.Code:N0}</td><td class=n>{s.Right.Code:N0}</td><td class=\"n {(s.CodeDelta >= 0 ? "pos" : "neg")}\">{Signed(s.CodeDelta)}</td>" +
                      $"<td class=n>{s.Left.Comment:N0}</td><td class=n>{s.Right.Comment:N0}</td><td class=n>{s.Left.Blank:N0}</td><td class=n>{s.Right.Blank:N0}</td>" +
                      $"<td class=n>{s.Added:N0}</td><td class=n>{s.Deleted:N0}</td><td class=n>{s.Modified:N0}</td></tr>");
        }
        sb.Append($"<tr><th>Total</th><th></th><th class=n>{summary.LeftFiles:N0}</th><th class=n>{summary.RightFiles:N0}</th><th class=n>{summary.Left.Code:N0}</th>" +
                  $"<th class=n>{summary.Right.Code:N0}</th><th class=n>{Signed(summary.CodeDelta)}</th><th class=n>{summary.Left.Comment:N0}</th><th class=n>{summary.Right.Comment:N0}</th>" +
                  $"<th class=n>{summary.Left.Blank:N0}</th><th class=n>{summary.Right.Blank:N0}</th><th class=n>{summary.Added:N0}</th><th class=n>{summary.Deleted:N0}</th><th class=n>{summary.Modified:N0}</th></tr></table>");

        sb.Append("<h2>Files</h2><table><tr><th>Status</th><th>Path</th><th>Language</th><th class=n>Code L</th><th class=n>Code R</th>" +
                  "<th class=n>+Added</th><th class=n>−Deleted</th><th class=n>~Modified</th><th>Note</th></tr>");
        foreach (var f in files)
        {
            sb.Append($"<tr><td><span class=\"pill {f.Status}\">{E(f.StatusText)}</span></td><td>{E(f.RelativePath)}</td><td>{E(f.Language)}</td>" +
                      $"<td class=n>{Num(f.LeftCode)}</td><td class=n>{Num(f.RightCode)}</td><td class=n>{Zero(f.Added)}</td><td class=n>{Zero(f.Deleted)}</td>" +
                      $"<td class=n>{Zero(f.Modified)}</td><td class=muted>{E(f.Note ?? "")}</td></tr>");
        }
        sb.Append("</table>");

        if (includeDiffs)
        {
            sb.Append("<h2>Differences</h2>");
            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (f.Status != FileStatus.Different || f.IsBinary || f.LeftPath is null || f.RightPath is null) continue;
                AppendFileDiff(sb, f, o.Text);
            }
        }

        sb.Append("</body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void AppendFileDiff(StringBuilder sb, FileComparison f, TextCompareOptions options)
    {
        TextFile left, right;
        try
        {
            left = TextFile.Load(f.LeftPath!);
            right = TextFile.Load(f.RightPath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var diff = DiffEngine.Compare(left.Lines, right.Lines, options);
        var rows = DiffEngine.CollapseUnchanged(DiffEngine.BuildRows(left.Lines, right.Lines, diff, options));

        sb.Append($"<details><summary>{E(f.RelativePath)} <span class=pos>+{f.Added}</span> <span class=neg>−{f.Deleted}</span> ~{f.Modified}</summary><table class=diff>");
        int count = 0;
        foreach (var row in rows)
        {
            if (++count > MaxReportRowsPerFile)
            {
                sb.Append("<tr class=sep><td colspan=4>… diff truncated …</td></tr>");
                break;
            }

            if (row.Kind == DiffRowKind.Collapsed)
            {
                sb.Append($"<tr class=sep><td colspan=4>⋯ {row.HiddenRows!.Count:N0} unchanged lines ⋯</td></tr>");
                continue;
            }

            string lc = row.LeftSide switch { DiffSide.Changed => "d", DiffSide.Empty => "e", _ => "" };
            string rc = row.RightSide switch { DiffSide.Changed => "a", DiffSide.Empty => "e", _ => "" };
            sb.Append($"<tr><td class=\"ln {lc}\">{row.LeftNumber}</td><td class=\"{lc}\">{Segments(row.LeftSegments)}</td>" +
                      $"<td class=\"ln {rc}\">{row.RightNumber}</td><td class=\"{rc}\">{Segments(row.RightSegments)}</td></tr>");
        }
        sb.Append("</table></details>");
    }

    private static string Segments(IReadOnlyList<TextSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var s in segments)
        {
            if (s.Kind == SegmentKind.Changed) sb.Append("<mark class=x>").Append(E(s.Text)).Append("</mark>");
            else sb.Append(E(s.Text));
        }
        return sb.ToString();
    }

    private static void Card(StringBuilder sb, string label, int value) =>
        sb.Append($"<div class=\"card\">{E(label)}<b>{value:N0}</b></div>");

    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static string Signed(int v) => v > 0 ? $"+{v:N0}" : v < 0 ? $"−{-v:N0}" : "0";
    private static string Zero(int v) => v == 0 ? "" : v.ToString("N0", CultureInfo.CurrentCulture);
    private static string Num(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Num(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
