using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CodeCompare.Core.Diff;

namespace CodeCompare.Controls;

/// <summary>Attached properties that render diff segments into a TextBlock, highlighting changed words.</summary>
public static class InlineText
{
    private const string Tab = "    ";

    private static readonly Brush DeletedMark = Frozen(0xFF, 0xC1, 0xBD);
    private static readonly Brush AddedMark = Frozen(0xAB, 0xF2, 0xBC);

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments", typeof(IReadOnlyList<TextSegment>), typeof(InlineText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty IsRightSideProperty = DependencyProperty.RegisterAttached(
        "IsRightSide", typeof(bool), typeof(InlineText), new PropertyMetadata(false, OnChanged));

    public static IReadOnlyList<TextSegment>? GetSegments(DependencyObject d) => (IReadOnlyList<TextSegment>?)d.GetValue(SegmentsProperty);
    public static void SetSegments(DependencyObject d, IReadOnlyList<TextSegment>? value) => d.SetValue(SegmentsProperty, value);

    public static bool GetIsRightSide(DependencyObject d) => (bool)d.GetValue(IsRightSideProperty);
    public static void SetIsRightSide(DependencyObject d, bool value) => d.SetValue(IsRightSideProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        var segments = GetSegments(textBlock);
        if (segments is null || segments.Count == 0)
        {
            textBlock.Text = "";
            return;
        }

        if (segments.Count == 1 && segments[0].Kind == SegmentKind.Normal)
        {
            textBlock.Text = ExpandTabs(segments[0].Text);
            return;
        }

        var mark = GetIsRightSide(textBlock) ? AddedMark : DeletedMark;
        textBlock.Inlines.Clear();
        foreach (var segment in segments)
        {
            var run = new Run(ExpandTabs(segment.Text));
            if (segment.Kind == SegmentKind.Changed) run.Background = mark;
            textBlock.Inlines.Add(run);
        }
    }

    private static string ExpandTabs(string text) => text.Contains('\t') ? text.Replace("\t", Tab) : text;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
