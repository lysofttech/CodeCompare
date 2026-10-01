using CodeCompare.Infrastructure;

namespace CodeCompare.ViewModels;

public sealed class ExtensionItem : ObservableObject
{
    private bool _isChecked;

    public ExtensionItem(string extension, string language, string category, bool defaultSelected)
    {
        Extension = extension;
        Language = language;
        Category = category;
        DefaultSelected = defaultSelected;
    }

    public string Extension { get; }
    public string Language { get; }
    public string Category { get; }
    public bool DefaultSelected { get; }

    public string Display => Extension.Length == 0 ? "(none)" : Extension;
    public string ToolTip => Extension.Length == 0 ? "Files without an extension (Makefile, Dockerfile, …)" : Language;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
