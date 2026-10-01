# Code Compare

A Windows desktop app (WPF, .NET 10) that compares two folders of source code: which files differ, what changed inside them, and how the line counts compare.

## Features

**Folder comparison**
- Classifies every file as *Identical*, *Different*, *Left only* or *Right only* (and *Binary differs* / *Error*).
- Files that differ only in line endings, encoding, or ignored whitespace/case are reported as identical, with a note explaining why.
- File-type filter with ~150 programming extensions grouped by ecosystem (.NET, Web, C/C++, JVM, Scripting, Systems, Data, Config, Docs). You can add your own extensions or tick "Include all files".
- Excludes folders and files by wildcard (`bin;obj;.git;node_modules;…`, `*.min.js;*.designer.cs;…`). Symlinks and junctions are skipped.
- Runs in parallel in the background, with progress and Cancel (Esc).

**Code diff**
- Side-by-side view with line numbers, added/deleted/modified line colours and **word-level highlights** inside changed lines.
- Change map next to the scrollbar shows where every change is; click it to jump.
- Previous/Next change (Shift+F7 / F7) and previous/next changed file (Shift+F8 / F8).
- "Changes only" mode hides unchanged lines and keeps 3 lines of context. Double-click a collapsed block to expand it.
- Options: ignore leading/trailing whitespace or all whitespace, ignore case, ignore blank lines.
- Word wrap, font zoom (Ctrl + mouse wheel), pop-out diff windows, open a file or show it in Explorer.
- Shows each file's encoding (UTF-8, UTF-8 BOM, UTF-16, ANSI) and line-ending style (CRLF/LF/Mixed).

**Lines of code**
- Counts total, code, comment and blank lines for each file. The counter knows comment syntax for each language and ignores comment markers inside strings.
- The file list shows lines and code lines per side, the code delta, and lines added/deleted/modified.
- The Statistics tab shows file counts, line-change totals, a left-vs-right LOC table and a breakdown by language.

**Other**
- Copy files left → right or right → left (with confirmation), then re-compare.
- Export an HTML report (summary, statistics and side-by-side diffs), a CSV file list for Excel, or a unified `.patch` file. Exports include the files currently shown, so the filters can narrow a report.
- Filter the file list by status and by path/language text (Ctrl+F). Columns are sortable.
- Remembers recent folders, file types, options and window layout (`%AppData%\CodeCompare\settings.json`).
- Drag and drop folders onto the path boxes. Swap sides with ⇄.
- Command line: `CodeCompare.exe "C:\old" "C:\new"` starts comparing immediately.

## Build and run

Requires the .NET 10 SDK on Windows.

```bash
dotnet run --project src/CodeCompare
```

Run the tests:

```bash
dotnet test
```

Publish a single self-contained `.exe` (no .NET install needed on the target machine):

```bash
dotnet publish src/CodeCompare -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Project layout

| Path | Contents |
|------|----------|
| `src/CodeCompare.Core` | UI-independent engine: Myers O(ND) diff, line counter and language table, folder scanner, report exporters |
| `src/CodeCompare` | WPF app: main window, diff viewer control, change map, settings |
| `tests/CodeCompare.Tests` | xUnit tests (diff minimality against an LCS oracle, LOC counting, encodings, folder classification, exports) |

The diff engine uses the linear-space variant of Myers' algorithm, so large files don't use quadratic memory. It has no third-party dependencies.
