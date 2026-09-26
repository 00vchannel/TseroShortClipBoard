using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Rect = System.Windows.Rect;

namespace ClipboardApp.Styles;

/// <summary>Highlights the row that will receive a dragged snippet.</summary>
internal sealed class DropTargetIndicator : IDisposable
{
    private AdornerLayer layer;
    private TargetAdorner adorner;
    private bool disposed;

    private DropTargetIndicator(ListBoxItem target, AdornerLayer layer)
    {
        this.layer = layer;
        adorner = new TargetAdorner(target);
        layer.Add(adorner);
    }

    public static DropTargetIndicator? Show(ListBoxItem target)
    {
        if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return null;

        var layer = AdornerLayer.GetAdornerLayer(target);
        return layer is null ? null : new DropTargetIndicator(target, layer);
    }

    public void Update(ListBoxItem target)
    {
        if (disposed)
            return;

        if (ReferenceEquals(adorner.AdornedElement, target))
            return;

        var nextLayer = AdornerLayer.GetAdornerLayer(target);
        if (nextLayer is null || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            layer.Remove(adorner);
            disposed = true;
            return;
        }

        layer.Remove(adorner);
        layer = nextLayer;
        adorner = new TargetAdorner(target);
        layer.Add(adorner);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        layer.Remove(adorner);
    }

    private sealed class TargetAdorner : Adorner
    {
        private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(38, 215, 255, 47));
        private static readonly Pen Outline = new(new SolidColorBrush(Color.FromArgb(160, 215, 255, 47)), 2);
        public TargetAdorner(ListBoxItem target) : base(target)
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = AdornedElement.RenderSize.Width;
            var height = AdornedElement.RenderSize.Height;
            if (width < 12 || height < 8)
                return;

            // Adorners sit above the ScrollViewer; clip to its visible content
            // so a scrolled-off target cannot paint over the rest of the window.
            var viewport = FindAncestor<ScrollContentPresenter>(AdornedElement);
            if (viewport is not null)
            {
                var visible = viewport.TransformToVisual(AdornedElement)
                    .TransformBounds(new Rect(viewport.RenderSize));
                drawingContext.PushClip(new RectangleGeometry(visible));
            }

            var body = new Rect(2, 1, width - 4, height - 3);
            drawingContext.DrawRoundedRectangle(Fill, Outline, body, 13, 13);

            if (viewport is not null)
                drawingContext.Pop();
        }

        private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            for (var current = VisualTreeHelper.GetParent(child); current is not null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is T ancestor)
                    return ancestor;
            }
            return null;
        }
    }
}
