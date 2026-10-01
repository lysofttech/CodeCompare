using System.Windows;
using System.Windows.Input;
using CodeCompare.Core.Comparison;
using CodeCompare.Core.Diff;

namespace CodeCompare;

public partial class DiffWindow : Window
{
    public DiffWindow(FileComparison item, TextCompareOptions options, bool wordWrap, bool changesOnly, double fontSize)
    {
        InitializeComponent();
        Title = $"{item.RelativePath} — Code Compare";
        Viewer.Options = options;
        Viewer.WordWrap = wordWrap;
        Viewer.ChangesOnly = changesOnly;
        Viewer.CodeFontSize = fontSize;
        Loaded += async (_, _) => await Viewer.ShowComparisonAsync(item);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F7 when Keyboard.Modifiers == ModifierKeys.Shift:
                Viewer.PreviousChange();
                e.Handled = true;
                break;
            case Key.F7:
                Viewer.NextChange();
                e.Handled = true;
                break;
            case Key.F5:
                e.Handled = true;
                await Viewer.ReloadAsync();
                break;
            case Key.Escape:
                Close();
                break;
        }
    }
}
