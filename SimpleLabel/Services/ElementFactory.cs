using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SimpleLabel.Models;

namespace SimpleLabel.Services;

/// <summary>
/// Factory class for creating canvas elements with standardized setup.
/// All factory methods return UI elements ready for canvas placement.
/// Event handlers are wired up by passing delegates to enable MainWindow orchestration.
/// </summary>
public static class ElementFactory
{
    #region Element Factory Methods

    /// <summary>
    /// Creates a TextBlock element for the canvas.
    /// </summary>
    /// <param name="text">The text content.</param>
    /// <param name="x">X position on canvas.</param>
    /// <param name="y">Y position on canvas.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>The created TextBlock element.</returns>
    public static TextBlock CreateTextElement(
        string text,
        double x,
        double y,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 30,  // 8mm character height (one step smaller)
            Foreground = Brushes.Black,
            FontFamily = new FontFamily("Arial"),
            TextAlignment = TextAlignment.Left,
            FontWeight = FontWeights.Normal,
            FontStyle = FontStyles.Normal,
            TextWrapping = TextWrapping.Wrap, // Enable text wrapping to prevent clipping
            Cursor = Cursors.Hand,
            Tag = "draggable",
            Width = 300,  // Larger default width for more text
            Height = 80   // Larger default height to fit 8mm text comfortably
        };

        Canvas.SetLeft(textBlock, x);
        Canvas.SetTop(textBlock, y);

        textBlock.MouseLeftButtonDown += onSelect;
        textBlock.MouseLeftButtonDown += onMouseDown;
        textBlock.MouseMove += onMouseMove;
        textBlock.MouseLeftButtonUp += onMouseUp;

        return textBlock;
    }

    /// <summary>
    /// Creates a Rectangle element for the canvas.
    /// </summary>
    /// <param name="x">X position on canvas.</param>
    /// <param name="y">Y position on canvas.</param>
    /// <param name="width">Width of the rectangle.</param>
    /// <param name="height">Height of the rectangle.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>The created Rectangle element.</returns>
    public static Rectangle CreateRectangleElement(
        double x,
        double y,
        double width,
        double height,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        var rectangle = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = Brushes.Transparent,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = "draggable"
        };

        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);

        rectangle.MouseLeftButtonDown += onSelect;
        rectangle.MouseLeftButtonDown += onMouseDown;
        rectangle.MouseMove += onMouseMove;
        rectangle.MouseLeftButtonUp += onMouseUp;

        return rectangle;
    }

    /// <summary>
    /// Creates an Ellipse element for the canvas.
    /// </summary>
    /// <param name="x">X position on canvas.</param>
    /// <param name="y">Y position on canvas.</param>
    /// <param name="width">Width of the ellipse.</param>
    /// <param name="height">Height of the ellipse.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>The created Ellipse element.</returns>
    public static Ellipse CreateEllipseElement(
        double x,
        double y,
        double width,
        double height,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Fill = Brushes.Transparent,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = "draggable"
        };

        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);

        ellipse.MouseLeftButtonDown += onSelect;
        ellipse.MouseLeftButtonDown += onMouseDown;
        ellipse.MouseMove += onMouseMove;
        ellipse.MouseLeftButtonUp += onMouseUp;

        return ellipse;
    }

    /// <summary>
    /// Creates an Image element for the canvas.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="x">X position on canvas.</param>
    /// <param name="y">Y position on canvas.</param>
    /// <param name="canvasActualWidth">The actual width of the canvas for sizing calculations.</param>
    /// <param name="canvasActualHeight">The actual height of the canvas for sizing calculations.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>The created Image element.</returns>
    public static Image CreateImageElement(
        string imagePath,
        double x,
        double y,
        double canvasActualWidth,
        double canvasActualHeight,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        var bitmap = new BitmapImage(new Uri(imagePath, UriKind.Absolute));

        // Calculate size that fits in canvas while preserving aspect ratio
        double imageWidth = bitmap.PixelWidth;
        double imageHeight = bitmap.PixelHeight;
        double aspectRatio = imageWidth / imageHeight;

        // Maximum size: 80% of canvas dimensions to ensure it fits nicely
        double maxWidth = canvasActualWidth * 0.8;
        double maxHeight = canvasActualHeight * 0.8;

        double finalWidth = imageWidth;
        double finalHeight = imageHeight;

        // Scale down if image is larger than max dimensions
        if (finalWidth > maxWidth || finalHeight > maxHeight)
        {
            double widthScale = maxWidth / finalWidth;
            double heightScale = maxHeight / finalHeight;
            double scale = Math.Min(widthScale, heightScale);

            finalWidth = finalWidth * scale;
            finalHeight = finalHeight * scale;
        }

        // Create CanvasElement with filter properties initialized
        var canvasElement = new CanvasElement
        {
            ElementType = "Image",
            ImagePath = imagePath,
            MonochromeEnabled = false,
            Threshold = 128,
            MonochromeAlgorithm = "Threshold",
            InvertColors = false,
            Brightness = 0,
            Contrast = 0
        };

        var image = new Image
        {
            Stretch = Stretch.Fill,  // Fill instead of Uniform to prevent aspect ratio shifts
            Width = finalWidth,
            Height = finalHeight,
            Cursor = Cursors.Hand,
            // Store both CanvasElement and original BitmapSource in Tag
            Tag = Tuple.Create(canvasElement, (BitmapSource)bitmap)
        };

        image.Source = bitmap;

        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);

        image.MouseLeftButtonDown += onSelect;
        image.MouseLeftButtonDown += onMouseDown;
        image.MouseMove += onMouseMove;
        image.MouseLeftButtonUp += onMouseUp;

        return image;
    }

    /// <summary>
    /// Creates a Line element wrapped in a Canvas for better hit-testing.
    /// </summary>
    /// <param name="x1">Start X coordinate.</param>
    /// <param name="y1">Start Y coordinate.</param>
    /// <param name="x2">End X coordinate.</param>
    /// <param name="y2">End Y coordinate.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>A Canvas containing the Line element.</returns>
    public static UIElement CreateLineElement(
        double x1,
        double y1,
        double x2,
        double y2,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        // Create a Canvas to wrap the line for better hit-testing
        var lineCanvas = new Canvas
        {
            Cursor = Cursors.Hand,
            Tag = "draggable",
            Background = Brushes.Transparent // Transparent background enables hit-testing on entire canvas
        };

        // Create the line (coordinates will be set by UpdateLineCanvas)
        var line = new Line
        {
            Stroke = Brushes.Black,
            StrokeThickness = 2
        };

        // Store line data in CanvasElement for serialization
        var canvasElement = new CanvasElement
        {
            ElementType = "Line",
            X = x1,
            Y = y1,
            X2 = x2,
            Y2 = y2,
            StrokeColor = "#FF000000",
            StrokeThickness = 2
        };

        // Add line to canvas
        lineCanvas.Children.Add(line);

        // Store Line and CanvasElement in Tag for later access
        lineCanvas.Tag = Tuple.Create(line, canvasElement);

        // Update canvas and line positions to center the line
        UpdateLineCanvas(lineCanvas, x1, y1, x2, y2);

        // Wire up mouse handlers to the canvas (not the line)
        lineCanvas.MouseLeftButtonDown += onSelect;
        lineCanvas.MouseLeftButtonDown += onMouseDown;
        lineCanvas.MouseMove += onMouseMove;
        lineCanvas.MouseLeftButtonUp += onMouseUp;

        return lineCanvas;
    }

    /// <summary>
    /// Creates an Arrow element (line with arrowheads) wrapped in a Canvas.
    /// Uses the same bounding-box centering logic as CreateLineElement for consistent behavior.
    /// </summary>
    /// <param name="x1">Start X coordinate.</param>
    /// <param name="y1">Start Y coordinate.</param>
    /// <param name="x2">End X coordinate.</param>
    /// <param name="y2">End Y coordinate.</param>
    /// <param name="hasStartArrow">Whether to show an arrowhead at the start.</param>
    /// <param name="hasEndArrow">Whether to show an arrowhead at the end.</param>
    /// <param name="arrowheadSize">Size of the arrowheads.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>A Canvas containing the Arrow element.</returns>
    public static UIElement CreateArrowElement(
        double x1,
        double y1,
        double x2,
        double y2,
        bool hasStartArrow,
        bool hasEndArrow,
        double arrowheadSize,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        // Create a Canvas to group the line and arrowheads together
        var arrowCanvas = new Canvas
        {
            Cursor = Cursors.Hand,
            Tag = "draggable",
            Background = Brushes.Transparent // Transparent background enables hit-testing on entire canvas
        };

        // Create the main line (coordinates will be set below using bounding-box centering)
        var line = new Line
        {
            Stroke = Brushes.Black,
            StrokeThickness = 2
        };

        // Store arrow data in Tag as Tuple<Line, Polygon?, Polygon?, CanvasElement>
        var canvasElement = new CanvasElement
        {
            ElementType = "Arrow",
            X = x1,
            Y = y1,
            X2 = x2,
            Y2 = y2,
            StrokeColor = "#FF000000",
            StrokeThickness = 2,
            HasStartArrow = hasStartArrow,
            HasEndArrow = hasEndArrow,
            ArrowheadSize = arrowheadSize
        };

        // Add line to canvas first
        arrowCanvas.Children.Add(line);

        // Store references in Tag (arrowheads will be added by UpdateArrowCanvas)
        arrowCanvas.Tag = Tuple.Create(line, (Polygon?)null, (Polygon?)null, canvasElement);

        // Use bounding-box centering (same logic as CreateLineElement)
        UpdateArrowCanvas(arrowCanvas, x1, y1, x2, y2);

        // Wire up mouse handlers to the canvas (not the line)
        arrowCanvas.MouseLeftButtonDown += onSelect;
        arrowCanvas.MouseLeftButtonDown += onMouseDown;
        arrowCanvas.MouseMove += onMouseMove;
        arrowCanvas.MouseLeftButtonUp += onMouseUp;

        return arrowCanvas;
    }

    /// <summary>
    /// Creates a Polygon element for the canvas.
    /// </summary>
    /// <param name="points">The collection of points defining the polygon.</param>
    /// <param name="x">X position on canvas.</param>
    /// <param name="y">Y position on canvas.</param>
    /// <param name="onSelect">Handler for element selection.</param>
    /// <param name="onMouseDown">Handler for mouse down event.</param>
    /// <param name="onMouseMove">Handler for mouse move event.</param>
    /// <param name="onMouseUp">Handler for mouse up event.</param>
    /// <returns>The created Polygon element.</returns>
    public static Polygon CreatePolygonElement(
        PointCollection points,
        double x,
        double y,
        MouseButtonEventHandler onSelect,
        MouseButtonEventHandler onMouseDown,
        MouseEventHandler onMouseMove,
        MouseButtonEventHandler onMouseUp)
    {
        // Calculate bounds from points to set Width/Height
        double width = 0;
        double height = 0;

        if (points.Count > 0)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            width = maxX - minX;
            height = maxY - minY;

            // Validate calculated values
            if (double.IsNaN(width) || double.IsInfinity(width) || width < 0)
                width = 0;
            if (double.IsNaN(height) || double.IsInfinity(height) || height < 0)
                height = 0;
        }

        // Add padding for stroke (stroke is drawn centered on the edge)
        const double strokePadding = 2;

        var polygon = new Polygon
        {
            Points = points,
            Fill = Brushes.Transparent,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = "draggable",
            Width = width + strokePadding,
            Height = height + strokePadding
        };

        // Position using Canvas.SetLeft/Top
        Canvas.SetLeft(polygon, x);
        Canvas.SetTop(polygon, y);

        polygon.MouseLeftButtonDown += onSelect;
        polygon.MouseLeftButtonDown += onMouseDown;
        polygon.MouseMove += onMouseMove;
        polygon.MouseLeftButtonUp += onMouseUp;

        return polygon;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a triangular arrowhead polygon.
    /// </summary>
    /// <param name="x">X position of the arrowhead tip.</param>
    /// <param name="y">Y position of the arrowhead tip.</param>
    /// <param name="angle">Rotation angle in radians.</param>
    /// <param name="size">Size of the arrowhead.</param>
    /// <returns>The created arrowhead Polygon.</returns>
    public static Polygon CreateArrowhead(double x, double y, double angle, double size)
    {
        // Create triangular arrowhead
        var arrowhead = new Polygon
        {
            Fill = Brushes.Black,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };

        // Arrow points (triangle pointing right, then rotated)
        double halfSize = size / 2;
        Point tip = new Point(x, y);
        Point left = new Point(x - size, y - halfSize);
        Point right = new Point(x - size, y + halfSize);

        // Rotate points around tip
        Point leftRotated = RotatePoint(left, tip, angle);
        Point rightRotated = RotatePoint(right, tip, angle);

        arrowhead.Points.Add(tip);
        arrowhead.Points.Add(leftRotated);
        arrowhead.Points.Add(rightRotated);

        return arrowhead;
    }

    /// <summary>
    /// Rotates a point around a center point by a given angle.
    /// </summary>
    /// <param name="point">The point to rotate.</param>
    /// <param name="center">The center of rotation.</param>
    /// <param name="angle">The rotation angle in radians.</param>
    /// <returns>The rotated point.</returns>
    public static Point RotatePoint(Point point, Point center, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos
        );
    }

    /// <summary>
    /// Creates points for an equilateral triangle that fills the given size.
    /// Points are offset inward to account for stroke thickness rendering.
    /// </summary>
    /// <param name="size">The size of the bounding square.</param>
    /// <returns>A PointCollection defining the triangle.</returns>
    public static PointCollection CreateTrianglePoints(double size)
    {
        // Offset to keep stroke within bounds (stroke is centered on edge)
        const double strokeOffset = 1;

        // Triangle with points offset inward to prevent stroke clipping
        var points = new PointCollection
        {
            new Point(size / 2, strokeOffset),           // Top center (offset down)
            new Point(strokeOffset, size - strokeOffset), // Bottom left (offset right and up)
            new Point(size - strokeOffset, size - strokeOffset) // Bottom right (offset left and up)
        };
        return points;
    }

    /// <summary>
    /// Creates points for a star shape with alternating outer and inner points.
    /// Points are normalized to start at (0,0) to prevent clipping.
    /// </summary>
    /// <param name="size">The size of the bounding square.</param>
    /// <param name="pointCount">Number of points (default 5 for a 5-pointed star).</param>
    /// <returns>A PointCollection defining the star.</returns>
    public static PointCollection CreateStarPoints(double size, int pointCount = 5)
    {
        var rawPoints = new List<Point>();
        double centerX = size / 2;
        double centerY = size / 2;
        double outerRadius = size / 2;
        double innerRadius = size / 4;

        // Generate alternating outer and inner points
        for (int i = 0; i < pointCount * 2; i++)
        {
            double angle = Math.PI / 2 + (i * Math.PI / pointCount);  // Start from top, rotate clockwise
            double radius = (i % 2 == 0) ? outerRadius : innerRadius;

            double x = centerX + radius * Math.Cos(angle);
            double y = centerY - radius * Math.Sin(angle);  // Negative because Y increases downward

            rawPoints.Add(new Point(x, y));
        }

        // Normalize points to start at (0,0) to prevent clipping
        double minX = rawPoints.Min(p => p.X);
        double minY = rawPoints.Min(p => p.Y);

        var points = new PointCollection();
        foreach (var p in rawPoints)
        {
            points.Add(new Point(p.X - minX, p.Y - minY));
        }

        return points;
    }

    /// <summary>
    /// Updates the position and size of a line canvas to keep the line centered.
    /// </summary>
    /// <param name="lineCanvas">The canvas containing the line.</param>
    /// <param name="x1">Start X coordinate.</param>
    /// <param name="y1">Start Y coordinate.</param>
    /// <param name="x2">End X coordinate.</param>
    /// <param name="y2">End Y coordinate.</param>
    public static void UpdateLineCanvas(Canvas lineCanvas, double x1, double y1, double x2, double y2)
    {
        if (lineCanvas.Tag is not Tuple<Line, CanvasElement> lineData)
            return;

        var line = lineData.Item1;
        var canvasElement = lineData.Item2;

        // Calculate bounding box
        double minX = Math.Min(x1, x2);
        double minY = Math.Min(y1, y2);
        double maxX = Math.Max(x1, x2);
        double maxY = Math.Max(y1, y2);

        double width = maxX - minX;
        double height = maxY - minY;

        // Add padding for better hit-testing
        const double padding = 10;
        double canvasWidth = Math.Max(width, padding * 2);
        double canvasHeight = Math.Max(height, padding * 2);

        // Calculate centering offset
        double offsetX = (canvasWidth - width) / 2;
        double offsetY = (canvasHeight - height) / 2;

        // Position canvas (adjusted for centering padding)
        double canvasLeft = minX - offsetX;
        double canvasTop = minY - offsetY;

        Canvas.SetLeft(lineCanvas, canvasLeft);
        Canvas.SetTop(lineCanvas, canvasTop);

        // Set canvas size
        lineCanvas.Width = canvasWidth;
        lineCanvas.Height = canvasHeight;

        // Set line coordinates relative to canvas (centered with padding)
        line.X1 = (x1 - minX) + offsetX;
        line.Y1 = (y1 - minY) + offsetY;
        line.X2 = (x2 - minX) + offsetX;
        line.Y2 = (y2 - minY) + offsetY;

        // Update CanvasElement with absolute coordinates
        canvasElement.X = x1;
        canvasElement.Y = y1;
        canvasElement.X2 = x2;
        canvasElement.Y2 = y2;
    }

    /// <summary>
    /// Updates the position and size of an arrow canvas to keep the arrow centered.
    /// Uses the same bounding-box centering logic as UpdateLineCanvas for consistent behavior.
    /// </summary>
    /// <param name="arrowCanvas">The canvas containing the arrow.</param>
    /// <param name="x1">Start X coordinate.</param>
    /// <param name="y1">Start Y coordinate.</param>
    /// <param name="x2">End X coordinate.</param>
    /// <param name="y2">End Y coordinate.</param>
    public static void UpdateArrowCanvas(Canvas arrowCanvas, double x1, double y1, double x2, double y2)
    {
        if (arrowCanvas.Tag is not Tuple<Line, Polygon?, Polygon?, CanvasElement> arrowData)
            return;

        var line = arrowData.Item1;
        var oldStartArrowhead = arrowData.Item2;
        var oldEndArrowhead = arrowData.Item3;
        var canvasElement = arrowData.Item4;

        // Calculate bounding box
        double minX = Math.Min(x1, x2);
        double minY = Math.Min(y1, y2);
        double maxX = Math.Max(x1, x2);
        double maxY = Math.Max(y1, y2);

        double width = maxX - minX;
        double height = maxY - minY;

        // Add padding for arrowheads (use larger of fixed padding or arrowhead size)
        double arrowheadSize = canvasElement.ArrowheadSize ?? 10;
        double padding = Math.Max(10, arrowheadSize);
        double canvasWidth = Math.Max(width, padding * 2);
        double canvasHeight = Math.Max(height, padding * 2);

        // Calculate centering offset
        double offsetX = (canvasWidth - width) / 2;
        double offsetY = (canvasHeight - height) / 2;

        // Position canvas (adjusted for centering padding)
        double canvasLeft = minX - offsetX;
        double canvasTop = minY - offsetY;

        Canvas.SetLeft(arrowCanvas, canvasLeft);
        Canvas.SetTop(arrowCanvas, canvasTop);

        // Set canvas size
        arrowCanvas.Width = canvasWidth;
        arrowCanvas.Height = canvasHeight;

        // Set line coordinates relative to canvas (centered with padding)
        line.X1 = (x1 - minX) + offsetX;
        line.Y1 = (y1 - minY) + offsetY;
        line.X2 = (x2 - minX) + offsetX;
        line.Y2 = (y2 - minY) + offsetY;

        // Update CanvasElement with absolute coordinates
        canvasElement.X = x1;
        canvasElement.Y = y1;
        canvasElement.X2 = x2;
        canvasElement.Y2 = y2;

        // Remove old arrowheads
        if (oldStartArrowhead != null)
            arrowCanvas.Children.Remove(oldStartArrowhead);
        if (oldEndArrowhead != null)
            arrowCanvas.Children.Remove(oldEndArrowhead);

        // Calculate arrow angle from relative line coordinates
        double angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1);

        // Create new arrowheads
        Polygon? newStartArrowhead = null;
        Polygon? newEndArrowhead = null;

        if (canvasElement.HasStartArrow ?? false)
        {
            newStartArrowhead = CreateArrowhead(line.X1, line.Y1, angle + Math.PI, arrowheadSize);
            arrowCanvas.Children.Insert(0, newStartArrowhead);
        }

        if (canvasElement.HasEndArrow ?? true)
        {
            newEndArrowhead = CreateArrowhead(line.X2, line.Y2, angle, arrowheadSize);
            arrowCanvas.Children.Insert(0, newEndArrowhead);
        }

        // Update Tag with new arrowheads
        arrowCanvas.Tag = Tuple.Create(line, newStartArrowhead, newEndArrowhead, canvasElement);
    }

    #endregion
}
