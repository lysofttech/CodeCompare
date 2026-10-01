using System.Text;
using CodeCompare.Core.Code;
using CodeCompare.Core.Comparison;
using CodeCompare.Core.IO;

namespace CodeCompare.Tests;

public class LineCounterTests
{
    [Fact]
    public void CountsCSharpCodeCommentsAndBlankLines()
    {
        string[] lines =
        [
            "// header comment",
            "using System;",
            "",
            "/* block",
            "   still comment */",
            "var url = \"http://example.com\"; // trailing",
            "var s = \"/* not a comment\";",
            "   ",
            "int x = 1; /* inline */",
        ];

        var stats = LineCounter.Count(lines, Languages.Get("file.cs"));

        Assert.Equal(new LocStats(Total: 9, Code: 4, Comment: 3, Blank: 2), stats);
    }

    [Fact]
    public void CountsPythonAndBatchComments()
    {
        var python = LineCounter.Count(["# comment", "x = 1  # trailing", "", "print('#')"], Languages.Get("a.py"));
        Assert.Equal(new LocStats(4, 2, 1, 1), python);

        var batch = LineCounter.Count(["@echo off", "REM comment", ":: comment", "REMOVE_ME=1"], Languages.Get("a.bat"));
        Assert.Equal(new LocStats(4, 2, 2, 0), batch);
    }

    [Fact]
    public void LuaBlockCommentsTakePrecedenceOverLineComments()
    {
        var stats = LineCounter.Count(["--[[ start", "inside", "]] local x = 1", "-- line"], Languages.Get("a.lua"));
        Assert.Equal(new LocStats(4, 1, 3, 0), stats);
    }

    [Fact]
    public void SplitLines_HandlesAllLineEndings()
    {
        Assert.Equal(["a", "b", "c", ""], TextFile.SplitLines("a\r\nb\nc\r\r\n"));
        Assert.Empty(TextFile.SplitLines(""));
        Assert.Equal("Mixed", TextFile.DetectLineEnding("a\r\nb\n"));
    }

    [Fact]
    public void DetectsBinaryAndEncodings()
    {
        Assert.True(TextFile.FromBytes([1, 2, 0, 4]).IsBinary);
        Assert.Equal("UTF-16 LE", TextFile.FromBytes([0xFF, 0xFE, (byte)'a', 0]).EncodingName);
        Assert.Equal("UTF-8 BOM", TextFile.FromBytes([0xEF, 0xBB, 0xBF, (byte)'a']).EncodingName);
        Assert.Equal("ANSI", TextFile.FromBytes([(byte)'a', 0xE9]).EncodingName);
    }

    [Fact]
    public void FolderComparer_ClassifiesFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "cc-tests-" + Guid.NewGuid().ToString("N"));
        string left = Path.Combine(root, "L"), right = Path.Combine(root, "R");
        try
        {
            Write(left, "same.cs", "int a;\n");
            Write(right, "same.cs", "int a;\n");
            Write(left, "eol.cs", "int a;\r\nint b;\r\n");
            Write(right, "eol.cs", "int a;\nint b;\n");
            Write(left, "changed.cs", "int a;\nint b;\n");
            Write(right, "changed.cs", "int a;\nint c;\nint d;\n");
            Write(left, "gone.cs", "x\n");
            Write(right, "sub/new.cs", "y\n");
            Write(left, "bin/skip.cs", "ignored\n");
            Write(left, "notes.txt", "not selected\n");

            var options = new CompareOptions
            {
                LeftRoot = left,
                RightRoot = right,
                Extensions = new HashSet<string> { ".cs" },
                ExcludedFolders = ["bin"],
            };

            var files = FolderComparer.Compare(options, null, CancellationToken.None).Files.ToDictionary(f => f.RelativePath.Replace('\\', '/'));

            Assert.Equal(5, files.Count);
            Assert.Equal(FileStatus.Identical, files["same.cs"].Status);
            Assert.Equal(FileStatus.Identical, files["eol.cs"].Status);
            Assert.Contains("line endings", files["eol.cs"].Note);
            Assert.Equal(FileStatus.Different, files["changed.cs"].Status);
            Assert.Equal((1, 1, 0), (files["changed.cs"].Modified, files["changed.cs"].Added, files["changed.cs"].Deleted));
            Assert.Equal(FileStatus.LeftOnly, files["gone.cs"].Status);
            Assert.Equal(FileStatus.RightOnly, files["sub/new.cs"].Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Exporters_WriteHtmlCsvAndPatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "cc-tests-" + Guid.NewGuid().ToString("N"));
        string left = Path.Combine(root, "L"), right = Path.Combine(root, "R");
        try
        {
            Write(left, "a.cs", "int a;\nint b;\n");
            Write(right, "a.cs", "int a;\nint <c>;\n");
            Write(right, "new.cs", "x\n");
            var options = new CompareOptions { LeftRoot = left, RightRoot = right, Extensions = new HashSet<string> { ".cs" } };
            var result = FolderComparer.Compare(options, null, CancellationToken.None);

            string html = Path.Combine(root, "r.html"), csv = Path.Combine(root, "r.csv"), patch = Path.Combine(root, "r.patch");
            Core.Reports.ReportExporter.ExportHtml(result, result.Files.ToList(), html, includeDiffs: true);
            Core.Reports.ReportExporter.ExportCsv(result.Files, csv);
            Core.Reports.ReportExporter.ExportPatch(result.Files, patch);

            Assert.Contains("&lt;c&gt;", File.ReadAllText(html));
            Assert.Equal(3, File.ReadAllLines(csv).Length);
            Assert.Equal(
                "diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -1,2 +1,2 @@\n int a;\n-int b;\n+int <c>;\n" +
                "diff --git a/new.cs b/new.cs\n--- /dev/null\n+++ b/new.cs\n@@ -0,0 +1 @@\n+x\n",
                File.ReadAllText(patch));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
