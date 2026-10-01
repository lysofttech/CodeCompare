using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CodeCompare.Core.Diff;

namespace CodeCompare.Controls;

/// <summary>
/// A thin overview strip showing where changes are in the whole file, plus the visible viewport.
/// Clicking or dragging on it scrolls the diff.
/// </summary>
public sealed class DiffMap : FrameworkElement
{
    private static readonly Brush Background = Frozen(0xF6, 0xF8, 0xFA);
    private static readonly Brush Deleted = Frozen(0xFF, 0x81, 0x82);
    private static readonly Brush Inserted = Frozen(0x4A, 0xC2, 0x6B);
    private static readonly Brush Modified = Frozen(0xD4, 0xA7, 0x2C);
    private static readonly Brush Ignored = Frozen(0xD0, 0xD7, 0xDE);
    private static readonly Brush ViewportFill = Frozen(0x09, 0x69, 0xDA, 0x22);
    private static readonly Pen ViewportPen = FrozenPen(Frozen(0x09, 0x69, 0xDA, 0x88));
    private static readonly Pen EdgePen = FrozenPen(Frozen(0xD0, 0xD7, 0xDE));

    private IReadOnlyList<DiffRow>? _rows;
    private double _viewportStart;
    private double _viewportCount;

    public DiffMap()
    {
        Cursor = Cursors.Hand;
        ToolTip = "Change overview — click to jump";
    }

    /// <summary>Raised with the row index the user clicked on.</summary>
    public event EventHandler<int>? NavigateRequested;

    public void SetRows(IReadOnlyList<DiffRow>? rows)
    {
        _rows = rows;
        InvalidateVisual();
    }

    public void SetViewport(double start, double count)
    {
        _viewportStart = start;
        _viewportCount = count;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(Background, null, bounds);
        dc.DrawLine(EdgePen, new Point(0.5, 0), new Point(0.5, bounds.Height));

        if (_rows is not { Count: > 0 } rows || bounds.Height <= 0) return;

        double scale = bounds.Height / rows.Count;
        double width = Math.Max(0, bounds.Width - 5);
        int i = 0;
        while (i < rows.Count)
        {
            var brush = BrushFor(rows[i].Kind);
            if (brush is null)
            {
                i++;
                continue;
            }

            int start = i;
            while (i < rows.Count && BrushFor(rows[i].Kind) == brush) i++;
            dc.DrawRectangle(brush, null, new Rect(3, start * scale, width, Math.Max(2, (i - start) * scale)));
        }

        if (_viewportCount > 0 && _viewportCount < rows.Count)
        {
            double top = _viewportStart * scale;
            double height = Math.Max(6, _viewportCount * scale);
            dc.DrawRectangle(ViewportFill, ViewportPen, new Rect(1, top, Math.Max(0, bounds.Width - 2), height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Navigate(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Navigate(e.GetPosition(this).Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
    }

    private void Navigate(double y)
    {
        if (_rows is not { Count: > 0 } rows || ActualHeight <= 0) return;
        int index = (int)Math.Clamp(y / ActualHeight * rows.Count, 0, rows.Count - 1);
        NavigateRequested?.Invoke(this, index);
    }

    private static Brush? BrushFor(DiffRowKind kind) => kind switch
    {
        DiffRowKind.Deleted => Deleted,
        DiffRowKind.Inserted => Inserted,
        DiffRowKind.Modified => Modified,
        DiffRowKind.Ignored => Ignored,
        _ => null,
    };

    private static Brush Frozen(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush)
    {
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }
}
