namespace CodeCompare.Core.Code;

/// <summary>Comment syntax used to tell code lines from comment lines.</summary>
public sealed class LanguageDefinition
{
    public LanguageDefinition(string name, string[] lineComments, (string Start, string End)[] blockComments,
                              string stringDelimiters, string[]? startOnlyLineComments = null)
    {
        Name = name;
        LineComments = lineComments;
        BlockComments = blockComments;
        StringDelimiters = stringDelimiters.ToCharArray();
        StartOnlyLineComments = startOnlyLineComments ?? [];
    }

    public string Name { get; }
    public string[] LineComments { get; }
    public (string Start, string End)[] BlockComments { get; }
    public char[] StringDelimiters { get; }

    /// <summary>Comment markers only recognised as the first token on a line (e.g. REM), matched case-insensitively.</summary>
    public string[] StartOnlyLineComments { get; }
}

public sealed record ExtensionInfo(string Extension, string Language, string Category, bool DefaultSelected);

public static class Languages
{
    public const string NoExtension = "";

    private static readonly Dictionary<string, LanguageDefinition> ByExtension = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LanguageDefinition> ByFileName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LanguageDefinition> Generic = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ExtensionInfo> CatalogList = [];

    public static IReadOnlyList<ExtensionInfo> Catalog => CatalogList;

    static Languages()
    {
        const string Net = ".NET", Web = "Web", Cpp = "C / C++", Jvm = "JVM", Script = "Scripting",
            Sys = "Systems & other", Data = "Data & query", Config = "Config & build", Docs = "Docs & text";

        Add(Net, CLike("C#"), true, ".cs", ".csx");
        Add(Net, new("Visual Basic", ["'"], [], "\"", ["REM"]), true, ".vb", ".vbs");
        Add(Net, new("F#", ["//"], [("(*", "*)")], "\""), true, ".fs", ".fsi", ".fsx");
        Add(Net, Xml("XAML"), true, ".xaml", ".axaml");
        Add(Net, new("Razor", ["//"], [("@*", "*@"), ("<!--", "-->"), ("/*", "*/")], "\""), true, ".cshtml", ".razor", ".vbhtml");
        Add(Net, Xml("MSBuild"), true, ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".props", ".targets", ".proj", ".nuspec");
        Add(Net, Xml("Config XML"), true, ".config", ".manifest", ".resx");
        Add(Net, Hash("Solution"), true, ".sln");
        Add(Net, Xml("Solution"), true, ".slnx");

        Add(Web, CLike("JavaScript", "\"'`"), true, ".js", ".jsx", ".mjs", ".cjs");
        Add(Web, CLike("TypeScript", "\"'`"), true, ".ts", ".tsx", ".mts", ".cts");
        Add(Web, Xml("HTML"), true, ".html", ".htm", ".xhtml");
        Add(Web, new("CSS", [], [("/*", "*/")], "\"'"), true, ".css");
        Add(Web, CLike("SCSS / Less"), true, ".scss", ".sass", ".less", ".styl");
        Add(Web, new("Vue / Svelte", ["//"], [("<!--", "-->"), ("/*", "*/")], "\"'`"), true, ".vue", ".svelte", ".astro");
        Add(Web, new("PHP", ["//", "#"], [("/*", "*/")], "\"'"), true, ".php", ".phtml");

        Add(Cpp, CLike("C"), true, ".c", ".h");
        Add(Cpp, CLike("C++"), true, ".cpp", ".cc", ".cxx", ".c++", ".hpp", ".hh", ".hxx", ".h++", ".inl", ".ipp", ".ino", ".cu");
        Add(Cpp, CLike("Objective-C"), true, ".m", ".mm");

        Add(Jvm, CLike("Java"), true, ".java");
        Add(Jvm, CLike("Kotlin"), true, ".kt", ".kts");
        Add(Jvm, CLike("Scala"), true, ".scala", ".sc");
        Add(Jvm, CLike("Groovy"), true, ".groovy", ".gradle");
        Add(Jvm, new("Clojure", [";"], [], "\""), true, ".clj", ".cljs", ".cljc", ".edn");

        Add(Script, new("Python", ["#"], [], "\"'"), true, ".py", ".pyw", ".pyi");
        Add(Script, new("Ruby", ["#"], [("=begin", "=end")], "\"'"), true, ".rb", ".rake", ".gemspec");
        Add(Script, new("Perl", ["#"], [("=pod", "=cut")], "\"'"), true, ".pl", ".pm");
        Add(Script, new("Lua", ["--"], [("--[[", "]]")], "\"'"), true, ".lua");
        Add(Script, Hash("R"), true, ".r");
        Add(Script, new("PowerShell", ["#"], [("<#", "#>")], "\"'"), true, ".ps1", ".psm1", ".psd1");
        Add(Script, Hash("Shell"), true, ".sh", ".bash", ".zsh", ".ksh", ".fish");
        Add(Script, new("Batch", [], [], "\"", ["REM", "::", "@REM"]), true, ".bat", ".cmd");
        Add(Script, Hash("Tcl"), true, ".tcl");

        Add(Sys, CLike("Go", "\"`"), true, ".go");
        Add(Sys, CLike("Rust", "\""), true, ".rs");
        Add(Sys, CLike("Swift", "\""), true, ".swift");
        Add(Sys, CLike("Dart"), true, ".dart");
        Add(Sys, CLike("Zig", "\""), true, ".zig");
        Add(Sys, CLike("Solidity"), true, ".sol");
        Add(Sys, new("Elixir", ["#"], [], "\"'"), true, ".ex", ".exs");
        Add(Sys, new("Erlang", ["%"], [], "\""), true, ".erl", ".hrl");
        Add(Sys, new("Haskell", ["--"], [("{-", "-}")], "\""), true, ".hs");
        Add(Sys, new("OCaml", [], [("(*", "*)")], "\""), true, ".ml", ".mli");
        Add(Sys, new("Julia", ["#"], [("#=", "=#")], "\""), true, ".jl");
        Add(Sys, Hash("Nim"), true, ".nim");
        Add(Sys, new("Pascal", ["//"], [("{", "}"), ("(*", "*)")], "'"), true, ".pas", ".pp", ".dpr");
        Add(Sys, new("Assembly", [";"], [], "\""), true, ".asm", ".s");
        Add(Sys, new("Fortran", ["!"], [], "\"'"), true, ".f90", ".f95", ".f03");

        Add(Data, new("SQL", ["--"], [("/*", "*/")], "'"), true, ".sql");
        Add(Data, Hash("GraphQL"), true, ".graphql", ".gql");
        Add(Data, CLike("Protobuf"), true, ".proto");

        Add(Config, CLike("JSON"), true, ".json", ".jsonc");
        Add(Config, Xml("XML"), true, ".xml", ".xsd", ".xsl", ".xslt", ".plist");
        Add(Config, Hash("YAML"), true, ".yml", ".yaml");
        Add(Config, Hash("TOML"), true, ".toml");
        Add(Config, new("INI", [";", "#"], [], ""), true, ".ini", ".cfg", ".conf", ".properties", ".editorconfig");
        Add(Config, Hash("CMake"), true, ".cmake");
        Add(Config, Hash("Docker"), true, ".dockerfile");
        Add(Config, new("Terraform", ["#", "//"], [("/*", "*/")], "\""), true, ".tf", ".tfvars", ".hcl");
        Add(Config, Hash("Makefile"), true, ".mk");
        Add(Config, Hash("Git config"), false, ".gitignore", ".gitattributes");
        Add(Config, Xml("SVG"), false, ".svg");

        Add(Docs, Xml("Markdown"), false, ".md", ".markdown");
        Add(Docs, new("Text", [], [], ""), false, ".txt", ".log");
        Add(Docs, new("CSV", [], [], ""), false, ".csv", ".tsv");

        CatalogList.Add(new ExtensionInfo(NoExtension, "No extension", Config, false));

        ByFileName["Makefile"] = ByExtension[".mk"];
        ByFileName["GNUmakefile"] = ByExtension[".mk"];
        ByFileName["Dockerfile"] = ByExtension[".dockerfile"];
        ByFileName["CMakeLists.txt"] = ByExtension[".cmake"];
        ByFileName["Jenkinsfile"] = ByExtension[".groovy"];
        ByFileName["Rakefile"] = ByExtension[".rb"];
        ByFileName["Gemfile"] = ByExtension[".rb"];
    }

    /// <summary>Returns the normalised (lower-case) extension of a path, or "" when there is none.</summary>
    public static string GetExtension(string path) => Path.GetExtension(path).ToLowerInvariant();

    public static LanguageDefinition Get(string path)
    {
        string fileName = Path.GetFileName(path);
        if (ByFileName.TryGetValue(fileName, out var byName)) return byName;

        string ext = GetExtension(path);
        if (ByExtension.TryGetValue(ext, out var byExt)) return byExt;

        lock (Generic)
        {
            if (!Generic.TryGetValue(ext, out var generic))
            {
                string name = ext.Length > 1 ? ext[1..].ToUpperInvariant() : "Other";
                generic = new LanguageDefinition(name, [], [], "");
                Generic[ext] = generic;
            }
            return generic;
        }
    }

    private static void Add(string category, LanguageDefinition language, bool defaultSelected, params string[] extensions)
    {
        foreach (var ext in extensions)
        {
            ByExtension[ext] = language;
            CatalogList.Add(new ExtensionInfo(ext, language.Name, category, defaultSelected));
        }
    }

    private static LanguageDefinition CLike(string name, string quotes = "\"'") => new(name, ["//"], [("/*", "*/")], quotes);

    private static LanguageDefinition Hash(string name) => new(name, ["#"], [], "\"'");

    private static LanguageDefinition Xml(string name) => new(name, [], [("<!--", "-->")], "");
}
