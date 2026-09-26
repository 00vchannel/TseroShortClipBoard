using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClipboardApp.Styles;

/// <summary>Shows a snapshot of the entire dragged row beside the cursor during an OLE drag.</summary>
internal sealed class DragRowPreview : IDisposable
{
    private readonly ListBox list;
    private readonly ListBoxItem row;
    private readonly AdornerLayer layer;
    private readonly RowAdorner adorner;
    private readonly Point pointerOffset;
    private readonly double originalOpacity;
    private bool disposed;

    private DragRowPreview(ListBox list, ListBoxItem row, AdornerLayer layer, ImageSource snapshot)
    {
        this.list = list;
        this.row = row;
        this.layer = layer;
        pointerOffset = Mouse.GetPosition(row);
        originalOpacity = row.Opacity;
        adorner = new RowAdorner(list, snapshot, new Size(row.ActualWidth, row.ActualHeight));
        layer.Add(adorner);
        row.Opacity = 0.35;
        UpdatePosition();
    }

    public static DragRowPreview? Begin(ListBox list, ListBoxItem row)
    {
        if (list.ActualWidth <= 0 || row.ActualWidth <= 0 || row.ActualHeight <= 0)
            return null;

        var layer = AdornerLayer.GetAdornerLayer(list);
        if (layer is null)
            return null;

        var dpi = VisualTreeHelper.GetDpi(row);
        var width = Math.Max(1, (int)Math.Ceiling(row.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(row.ActualHeight * dpi.DpiScaleY));
        var snapshot = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        // Render through a visual brush so each row is captured in local
        // coordinates. Rendering the row directly leaves later rows outside
        // the bitmap because their list offset is nonzero.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(row)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(row.TranslatePoint(new Point(), list),
                    new Size(row.ActualWidth, row.ActualHeight)),
                Stretch = Stretch.Fill
            }, null, new Rect(0, 0, row.ActualWidth, row.ActualHeight));
        snapshot.Render(visual);
        snapshot.Freeze();
        return new DragRowPreview(list, row, layer, snapshot);
    }

    public void UpdatePosition()
    {
        if (disposed || !GetCursorPos(out var cursor))
            return;

        // PointFromScreen converts physical screen pixels into this ListBox's WPF units.
        var point = list.PointFromScreen(new Point(cursor.X, cursor.Y));
        adorner.Position = new Point(point.X - pointerOffset.X, point.Y - pointerOffset.Y);
        layer.Update(list);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        row.Opacity = originalOpacity;
        layer.Remove(adorner);
    }

    private sealed class RowAdorner : Adorner
    {
        private readonly Border preview;
        private readonly Size rowSize;
        private Point position;

        public RowAdorner(UIElement adornedElement, ImageSource snapshot, Size rowSize)
            : base(adornedElement)
        {
            this.rowSize = rowSize;
            IsHitTestVisible = false;
            preview = new Border
            {
                Width = rowSize.Width,
                Height = rowSize.Height,
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Opacity = 0.9,
                Background = Brushes.Transparent,
                Child = new Image { Source = snapshot, Stretch = Stretch.Fill },
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 7,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };
            AddVisualChild(preview);
        }

        public Point Position
        {
            set
            {
                position = value;
                InvalidateArrange();
            }
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) =>
            index == 0 ? preview : throw new ArgumentOutOfRangeException(nameof(index));

        protected override Size MeasureOverride(Size constraint)
        {
            preview.Measure(rowSize);
            // The adorner covers the list; a row-sized adorner clips previews
            // whose starting position is below the first list item.
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            preview.Arrange(new Rect(position, rowSize));
            return finalSize;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}
