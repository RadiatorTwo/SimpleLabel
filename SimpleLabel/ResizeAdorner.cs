using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SimpleLabel.Controls;
using SimpleLabel.Models;

namespace SimpleLabel
{
    public class ResizeAdorner : Adorner
    {
        // 8 Thumb controls for resize handles
        private Thumb topLeft, topCenter, topRight;
        private Thumb middleLeft, middleRight;
        private Thumb bottomLeft, bottomCenter, bottomRight;

        private VisualCollection visualChildren;
        private FrameworkElement? adornedElement;
        private Size initialSize; // Track initial size for undo/redo
        private Point initialLineStart; // Track Line X1/Y1 for undo/redo
        private Point initialLineEnd; // Track Line X2/Y2 for undo/redo
        private MainWindow? mainWindow; // Reference to MainWindow for command execution

        public ResizeAdorner(UIElement adornedElement) : base(adornedElement)
        {
            this.adornedElement = adornedElement as FrameworkElement;
            visualChildren = new VisualCollection(this);

            // Find MainWindow
            mainWindow = Window.GetWindow(adornedElement) as MainWindow;

            // Create 8 Thumb controls with consistent styling
            BuildThumb(ref topLeft, Cursors.SizeNWSE);
            BuildThumb(ref topCenter, Cursors.SizeNS);
            BuildThumb(ref topRight, Cursors.SizeNESW);
            BuildThumb(ref middleLeft, Cursors.SizeWE);
            BuildThumb(ref middleRight, Cursors.SizeWE);
            BuildThumb(ref bottomLeft, Cursors.SizeNESW);
            BuildThumb(ref bottomCenter, Cursors.SizeNS);
            BuildThumb(ref bottomRight, Cursors.SizeNWSE);

            // Wire up DragStarted and DragCompleted events
            topLeft.DragStarted += Thumb_DragStarted;
            topCenter.DragStarted += Thumb_DragStarted;
            topRight.DragStarted += Thumb_DragStarted;
            middleLeft.DragStarted += Thumb_DragStarted;
            middleRight.DragStarted += Thumb_DragStarted;
            bottomLeft.DragStarted += Thumb_DragStarted;
            bottomCenter.DragStarted += Thumb_DragStarted;
            bottomRight.DragStarted += Thumb_DragStarted;

            topLeft.DragCompleted += Thumb_DragCompleted;
            topCenter.DragCompleted += Thumb_DragCompleted;
            topRight.DragCompleted += Thumb_DragCompleted;
            middleLeft.DragCompleted += Thumb_DragCompleted;
            middleRight.DragCompleted += Thumb_DragCompleted;
            bottomLeft.DragCompleted += Thumb_DragCompleted;
            bottomCenter.DragCompleted += Thumb_DragCompleted;
            bottomRight.DragCompleted += Thumb_DragCompleted;

            // Wire up DragDelta events
            topLeft.DragDelta += HandleTopLeft;
            topCenter.DragDelta += HandleTop;
            topRight.DragDelta += HandleTopRight;
            middleLeft.DragDelta += HandleLeft;
            middleRight.DragDelta += HandleRight;
            bottomLeft.DragDelta += HandleBottomLeft;
            bottomCenter.DragDelta += HandleBottom;
            bottomRight.DragDelta += HandleBottomRight;
        }

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (adornedElement != null)
            {
                // Check for Line Canvas wrapper
                if (adornedElement is Canvas canvas && canvas.Tag is Tuple<Line, CanvasElement>)
                {
                    var lineData = (Tuple<Line, CanvasElement>)canvas.Tag;
                    var line = lineData.Item1;
                    initialLineStart = new Point(line.X1, line.Y1);
                    initialLineEnd = new Point(line.X2, line.Y2);
                }
                else if (adornedElement is Line line)
                {
                    initialLineStart = new Point(line.X1, line.Y1);
                    initialLineEnd = new Point(line.X2, line.Y2);
                }
                else
                {
                    initialSize = new Size(adornedElement.Width, adornedElement.Height);
                }
            }
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (adornedElement != null && mainWindow != null)
            {
                // Check for Line Canvas wrapper
                if (adornedElement is Canvas canvas && canvas.Tag is Tuple<Line, CanvasElement>)
                {
                    var lineData = (Tuple<Line, CanvasElement>)canvas.Tag;
                    var line = lineData.Item1;
                    Point finalStart = new Point(line.X1, line.Y1);
                    Point finalEnd = new Point(line.X2, line.Y2);
                    // Only create command if coordinates actually changed
                    if (initialLineStart != finalStart || initialLineEnd != finalEnd)
                    {
                        mainWindow.ExecuteLineResizeCommand(line, initialLineStart, initialLineEnd, finalStart, finalEnd);
                    }
                }
                else if (adornedElement is Line line)
                {
                    Point finalStart = new Point(line.X1, line.Y1);
                    Point finalEnd = new Point(line.X2, line.Y2);
                    // Only create command if coordinates actually changed
                    if (initialLineStart != finalStart || initialLineEnd != finalEnd)
                    {
                        mainWindow.ExecuteLineResizeCommand(line, initialLineStart, initialLineEnd, finalStart, finalEnd);
                    }
                }
                else
                {
                    Size finalSize = new Size(adornedElement.Width, adornedElement.Height);
                    // Only create command if size actually changed
                    if (initialSize != finalSize)
                    {
                        mainWindow.ExecuteResizeCommand(adornedElement, initialSize, finalSize);
                    }
                }
            }
        }

        private void BuildThumb(ref Thumb thumb, Cursor cursor)
        {
            thumb = new Thumb
            {
                Width = 8,
                Height = 8,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Cursor = cursor
            };
            visualChildren.Add(thumb);
        }

        protected override int VisualChildrenCount => visualChildren.Count;
        protected override Visual GetVisualChild(int index) => visualChildren[index];

        public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
        {
            GeneralTransformGroup result = new GeneralTransformGroup();
            result.Children.Add(base.GetDesiredTransform(transform));

            // No special transform needed for Line Canvas wrapper - it uses Canvas.Left/Top like other elements
            // Only needed for standalone Line elements (legacy support)
            if (adornedElement is Line line)
            {
                double left = Math.Min(line.X1, line.X2);
                double top = Math.Min(line.Y1, line.Y2);

                const double padding = 10;
                double width = Math.Abs(line.X2 - line.X1);
                double height = Math.Abs(line.Y2 - line.Y1);

                // Calculate adorner dimensions (with minimum size for handles)
                double adornerWidth = Math.Max(width, padding * 2);
                double adornerHeight = Math.Max(height, padding * 2);

                // Center the adorner around the line
                double offsetX = (adornerWidth - width) / 2;
                double offsetY = (adornerHeight - height) / 2;

                result.Children.Add(new TranslateTransform(left - offsetX, top - offsetY));
            }

            return result;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (adornedElement == null) return finalSize;

            double width, height;
            const double padding = 10;

            // Try to get dimensions from IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null)
            {
                if (control.UsesEndpoints)
                {
                    // Endpoint-based elements: calculate dimensions from CanvasElement coordinates
                    var ce = control.CanvasElement;
                    double x1 = ce.X;
                    double y1 = ce.Y;
                    double x2 = ce.X2 ?? x1;
                    double y2 = ce.Y2 ?? y1;

                    // Calculate bounding box dimensions
                    double minX = Math.Min(x1, x2);
                    double minY = Math.Min(y1, y2);
                    width = Math.Abs(x2 - x1);
                    height = Math.Abs(y2 - y1);
                    // Add minimal padding to make handles visible
                    width = Math.Max(width, padding * 2);
                    height = Math.Max(height, padding * 2);

                    // Calculate centering offset (same as UpdateCanvasFromEndpoints)
                    double actualWidth = Math.Abs(x2 - x1);
                    double actualHeight = Math.Abs(y2 - y1);
                    double offsetX = (width - actualWidth) / 2;
                    double offsetY = (height - actualHeight) / 2;

                    // For endpoint elements, position TopLeft at start point, BottomRight at end point
                    // Calculate positions relative to the adorner's bounding box
                    double startRelX = (x1 - minX) + offsetX;
                    double startRelY = (y1 - minY) + offsetY;
                    double endRelX = (x2 - minX) + offsetX;
                    double endRelY = (y2 - minY) + offsetY;

                    const double endpointThumbSize = 8;
                    const double endpointHalfThumb = endpointThumbSize / 2;

                    // Arrange only corner thumbs for endpoint elements (TopLeft = Start, BottomRight = End)
                    topLeft.Arrange(new Rect(startRelX - endpointHalfThumb, startRelY - endpointHalfThumb, endpointThumbSize, endpointThumbSize));
                    bottomRight.Arrange(new Rect(endRelX - endpointHalfThumb, endRelY - endpointHalfThumb, endpointThumbSize, endpointThumbSize));

                    // Hide edge handles for endpoint elements (place off-screen)
                    var hidden = new Rect(-100, -100, 0, 0);
                    topCenter.Arrange(hidden);
                    topRight.Arrange(hidden);
                    middleLeft.Arrange(hidden);
                    middleRight.Arrange(hidden);
                    bottomLeft.Arrange(hidden);
                    bottomCenter.Arrange(hidden);

                    return finalSize;
                }
                else
                {
                    // Bounds-based elements: use ActualWidth/Height
                    width = adornedElement.ActualWidth;
                    height = adornedElement.ActualHeight;
                }
            }
            else
            {
                width = adornedElement.ActualWidth;
                height = adornedElement.ActualHeight;
            }

            double thumbSize = 8;
            double halfThumb = thumbSize / 2;

            // Arrange 8 thumbs at corners and edges
            topLeft.Arrange(new Rect(-halfThumb, -halfThumb, thumbSize, thumbSize));
            topCenter.Arrange(new Rect(width/2 - halfThumb, -halfThumb, thumbSize, thumbSize));
            topRight.Arrange(new Rect(width - halfThumb, -halfThumb, thumbSize, thumbSize));
            middleLeft.Arrange(new Rect(-halfThumb, height/2 - halfThumb, thumbSize, thumbSize));
            middleRight.Arrange(new Rect(width - halfThumb, height/2 - halfThumb, thumbSize, thumbSize));
            bottomLeft.Arrange(new Rect(-halfThumb, height - halfThumb, thumbSize, thumbSize));
            bottomCenter.Arrange(new Rect(width/2 - halfThumb, height - halfThumb, thumbSize, thumbSize));
            bottomRight.Arrange(new Rect(width - halfThumb, height - halfThumb, thumbSize, thumbSize));

            return finalSize;
        }

        private void HandleBottomRight(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null)
            {
                control.HandleResize(ResizeHandle.BottomRight, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleTopLeft(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null)
            {
                control.HandleResize(ResizeHandle.TopLeft, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleTop(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null && !control.UsesEndpoints)
            {
                control.HandleResize(ResizeHandle.TopCenter, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleTopRight(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null && !control.UsesEndpoints)
            {
                control.HandleResize(ResizeHandle.TopRight, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleLeft(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null && !control.UsesEndpoints)
            {
                control.HandleResize(ResizeHandle.MiddleLeft, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleRight(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null && !control.UsesEndpoints)
            {
                control.HandleResize(ResizeHandle.MiddleRight, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleBottomLeft(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null && !control.UsesEndpoints)
            {
                control.HandleResize(ResizeHandle.BottomLeft, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }

        private void HandleBottom(object sender, DragDeltaEventArgs e)
        {
            if (adornedElement == null) return;

            // Delegate to IElementControl if registered
            var control = mainWindow?.GetElementControl(adornedElement);
            if (control != null && !control.UsesEndpoints)
            {
                control.HandleResize(ResizeHandle.BottomCenter, e.HorizontalChange, e.VerticalChange);
                InvalidateArrange();
                mainWindow?.UpdatePropertiesPanel();
            }
        }
    }
}
