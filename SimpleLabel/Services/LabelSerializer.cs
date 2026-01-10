using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SimpleLabel.Controls;
using SimpleLabel.Models;

namespace SimpleLabel.Services;

/// <summary>
/// Service for serializing and deserializing canvas elements to/from LabelDocument format.
/// Handles conversion between WPF UIElements and JSON-serializable CanvasElement objects.
/// </summary>
public static class LabelSerializer
{
    #region Serialization

    /// <summary>
    /// Serializes all elements on the canvas to a LabelDocument.
    /// </summary>
    /// <param name="canvas">The canvas containing elements to serialize.</param>
    /// <returns>A LabelDocument representing all canvas elements.</returns>
    public static LabelDocument SerializeCanvas(Canvas canvas)
    {
        var doc = new LabelDocument
        {
            CanvasWidth = canvas.ActualWidth,
            CanvasHeight = canvas.ActualHeight,
            Elements = new List<CanvasElement>()
        };

        foreach (UIElement child in canvas.Children)
        {
            var element = SerializeElement(child);
            if (element != null && !string.IsNullOrEmpty(element.ElementType))
            {
                doc.Elements.Add(element);
            }
        }

        return doc;
    }

    /// <summary>
    /// Serializes a single UIElement to a CanvasElement.
    /// </summary>
    /// <param name="child">The UIElement to serialize.</param>
    /// <returns>A CanvasElement representation, or null if not serializable.</returns>
    private static CanvasElement? SerializeElement(UIElement child)
    {
        var element = new CanvasElement();

        // Get position (handle NaN)
        double left = Canvas.GetLeft(child);
        double top = Canvas.GetTop(child);
        element.X = double.IsNaN(left) ? 0.0 : left;
        element.Y = double.IsNaN(top) ? 0.0 : top;

        // Type-specific extraction
        if (child is TextBlock textBlock)
        {
            element.ElementType = "Text";
            element.Width = textBlock.Width;
            element.Height = textBlock.Height;
            element.Text = textBlock.Text;
            element.FontSize = textBlock.FontSize;
            if (textBlock.Foreground is SolidColorBrush brush)
                element.ForegroundColor = brush.Color.ToString();

            // Text formatting properties
            element.FontFamily = textBlock.FontFamily.Source;
            element.TextAlignment = textBlock.TextAlignment.ToString();
            element.FontWeight = textBlock.FontWeight == FontWeights.Bold ? "Bold" : "Normal";
            element.FontStyle = textBlock.FontStyle == FontStyles.Italic ? "Italic" : "Normal";
        }
        else if (child is Rectangle rectangle)
        {
            element.ElementType = "Rectangle";
            element.Width = rectangle.Width;
            element.Height = rectangle.Height;
            if (rectangle.Fill is SolidColorBrush fillBrush)
                element.FillColor = fillBrush.Color.ToString();
            if (rectangle.Stroke is SolidColorBrush strokeBrush)
                element.StrokeColor = strokeBrush.Color.ToString();
            element.StrokeThickness = rectangle.StrokeThickness;

            // Extended shape properties
            element.RadiusX = rectangle.RadiusX;
            element.RadiusY = rectangle.RadiusY;
            element.StrokeDashPattern = DetectDashPattern(rectangle.StrokeDashArray);

            if (rectangle.Fill is LinearGradientBrush gradientBrush)
            {
                element.UseGradientFill = true;
                if (gradientBrush.GradientStops.Count >= 2)
                {
                    element.GradientStartColor = gradientBrush.GradientStops[0].Color.ToString();
                    element.GradientEndColor = gradientBrush.GradientStops[^1].Color.ToString();
                    element.GradientAngle = CalculateGradientAngle(gradientBrush);
                }
            }
        }
        else if (child is Ellipse ellipse)
        {
            element.ElementType = "Ellipse";
            element.Width = ellipse.Width;
            element.Height = ellipse.Height;
            if (ellipse.Fill is SolidColorBrush fillBrush)
                element.FillColor = fillBrush.Color.ToString();
            if (ellipse.Stroke is SolidColorBrush strokeBrush)
                element.StrokeColor = strokeBrush.Color.ToString();
            element.StrokeThickness = ellipse.StrokeThickness;

            // Extended shape properties
            element.StrokeDashPattern = DetectDashPattern(ellipse.StrokeDashArray);

            if (ellipse.Fill is LinearGradientBrush gradientBrush)
            {
                element.UseGradientFill = true;
                if (gradientBrush.GradientStops.Count >= 2)
                {
                    element.GradientStartColor = gradientBrush.GradientStops[0].Color.ToString();
                    element.GradientEndColor = gradientBrush.GradientStops[^1].Color.ToString();
                    element.GradientAngle = CalculateGradientAngle(gradientBrush);
                }
            }
        }
        else if (child is Polygon polygon)
        {
            element.ElementType = "Polygon";
            // Serialize points as space-separated string "x1,y1 x2,y2 x3,y3..."
            // Use InvariantCulture to ensure decimal point (not comma) regardless of system locale
            element.PolygonPoints = string.Join(" ", polygon.Points.Select(p =>
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1}", p.X, p.Y)));
            // Calculate bounding box for Width/Height
            if (polygon.Points.Count > 0)
            {
                double minX = polygon.Points.Min(p => p.X);
                double maxX = polygon.Points.Max(p => p.X);
                double minY = polygon.Points.Min(p => p.Y);
                double maxY = polygon.Points.Max(p => p.Y);
                element.Width = maxX - minX;
                element.Height = maxY - minY;
            }
            if (polygon.Fill is SolidColorBrush fillBrush)
                element.FillColor = fillBrush.Color.ToString();
            if (polygon.Stroke is SolidColorBrush strokeBrush)
                element.StrokeColor = strokeBrush.Color.ToString();
            element.StrokeThickness = polygon.StrokeThickness;
        }
        else if (child is Line line)
        {
            element.ElementType = "Line";
            element.X = line.X1;
            element.Y = line.Y1;
            element.X2 = line.X2;
            element.Y2 = line.Y2;
            if (line.Stroke is SolidColorBrush strokeBrush)
                element.StrokeColor = strokeBrush.Color.ToString();
            element.StrokeThickness = line.StrokeThickness;
        }
        else if (child is Canvas lineCanvasWrapper && lineCanvasWrapper.Tag is Tuple<Line, CanvasElement>)
        {
            // Line element (stored as Canvas wrapper with Line)
            var lineData = (Tuple<Line, CanvasElement>)lineCanvasWrapper.Tag;
            var storedElement = lineData.Item2;
            element.ElementType = "Line";
            // Use absolute coordinates stored in CanvasElement (updated by UpdateLineCanvas)
            // Note: Canvas.Left/Top are offset by padding, so we can't reconstruct from them
            element.X = storedElement.X;
            element.Y = storedElement.Y;
            element.X2 = storedElement.X2;
            element.Y2 = storedElement.Y2;
            // Lines don't use Width/Height - leave at default 0
            // Update stroke properties from internal line
            var internalLine = lineData.Item1;
            if (internalLine.Stroke is SolidColorBrush strokeBrush)
                element.StrokeColor = strokeBrush.Color.ToString();
            element.StrokeThickness = internalLine.StrokeThickness;
        }
        else if (child is Canvas arrowCanvasWrapper && arrowCanvasWrapper.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement>)
        {
            // Arrow element (stored as Canvas with Line + Polygons)
            var arrowData = (Tuple<Line, Polygon?, Polygon?, CanvasElement>)arrowCanvasWrapper.Tag;
            var storedElement = arrowData.Item4;
            element.ElementType = "Arrow";
            // Use absolute coordinates stored in CanvasElement (updated by UpdateArrowCanvas)
            // Note: Canvas.Left/Top are offset by padding, so we can't reconstruct from them
            element.X = storedElement.X;
            element.Y = storedElement.Y;
            element.X2 = storedElement.X2;
            element.Y2 = storedElement.Y2;
            // Arrows don't use Width/Height - leave at default 0
            // Copy arrow-specific properties
            element.HasStartArrow = storedElement.HasStartArrow;
            element.HasEndArrow = storedElement.HasEndArrow;
            element.ArrowheadSize = storedElement.ArrowheadSize;
            // Update stroke properties from internal line
            var internalLine = arrowData.Item1;
            if (internalLine.Stroke is SolidColorBrush strokeBrush)
                element.StrokeColor = strokeBrush.Color.ToString();
            element.StrokeThickness = internalLine.StrokeThickness;
        }
        else if (child is Image image)
        {
            // Get CanvasElement from Tag if it exists
            if (image.Tag is Tuple<CanvasElement, BitmapSource> tuple)
            {
                // Use the CanvasElement from Tag which has all properties including filter settings
                element = tuple.Item1;
                // Update position and size from actual element
                element.X = double.IsNaN(left) ? 0.0 : left;
                element.Y = double.IsNaN(top) ? 0.0 : top;
                element.Width = image.Width;
                element.Height = image.Height;
            }
            else
            {
                // Backwards compatibility for old save files without Tag
                element.ElementType = "Image";
                element.Width = image.Width;
                element.Height = image.Height;
                if (image.Source is BitmapImage bitmapImage)
                    element.ImagePath = bitmapImage.UriSource?.AbsolutePath;
            }
        }
        else
        {
            return null;
        }

        // Validate Width/Height - replace invalid values with 0
        if (double.IsNaN(element.Width) || double.IsInfinity(element.Width))
            element.Width = 0;
        if (double.IsNaN(element.Height) || double.IsInfinity(element.Height))
            element.Height = 0;

        return element;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates a double value, returning defaultValue if invalid (NaN, Infinity, negative for sizes).
    /// </summary>
    public static double ValidateSize(double value, double defaultValue = 0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return defaultValue;
        return value;
    }

    /// <summary>
    /// Detects the dash pattern name from a DoubleCollection.
    /// </summary>
    public static string DetectDashPattern(DoubleCollection? dashArray)
    {
        if (dashArray == null || dashArray.Count == 0) return "Solid";

        // Compare with known patterns
        if (IsArrayEqual(dashArray, new[] { 2.0, 2.0 })) return "Dash";
        if (IsArrayEqual(dashArray, new[] { 1.0, 2.0 })) return "Dot";
        if (IsArrayEqual(dashArray, new[] { 2.0, 2.0, 1.0, 2.0 })) return "DashDot";

        return "Solid";
    }

    /// <summary>
    /// Detects the dash pattern index for combo box selection.
    /// </summary>
    public static int DetectDashPatternIndex(DoubleCollection? dashArray)
    {
        return DetectDashPattern(dashArray) switch
        {
            "Solid" => 0,
            "Dash" => 1,
            "Dot" => 2,
            "DashDot" => 3,
            _ => 0
        };
    }

    /// <summary>
    /// Compares a DoubleCollection with a pattern array.
    /// </summary>
    public static bool IsArrayEqual(DoubleCollection array, double[] pattern)
    {
        if (array.Count != pattern.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (Math.Abs(array[i] - pattern[i]) > 0.01) return false;
        }
        return true;
    }

    /// <summary>
    /// Calculates the gradient angle from a LinearGradientBrush.
    /// </summary>
    public static double CalculateGradientAngle(LinearGradientBrush brush)
    {
        double dx = brush.EndPoint.X - brush.StartPoint.X;
        double dy = brush.EndPoint.Y - brush.StartPoint.Y;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        return (angle + 360) % 360;
    }

    #endregion

    #region Deserialization

    /// <summary>
    /// Delegate for creating elements with proper event handler wiring.
    /// </summary>
    public delegate UIElement? ElementCreator(CanvasElement element);

    /// <summary>
    /// Delegate called after an element is created and added to the canvas.
    /// </summary>
    public delegate void ElementPostProcessor(UIElement element, CanvasElement canvasElement);

    /// <summary>
    /// Deserializes a LabelDocument to a canvas.
    /// </summary>
    /// <param name="doc">The document to deserialize.</param>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="createElement">Function to create elements with event handlers.</param>
    /// <param name="postProcess">Optional callback after element is added to canvas.</param>
    public static void DeserializeToCanvas(
        LabelDocument doc,
        Canvas canvas,
        ElementCreator createElement,
        ElementPostProcessor? postProcess = null)
    {
        canvas.Width = doc.CanvasWidth;
        canvas.Height = doc.CanvasHeight;

        foreach (var element in doc.Elements)
        {
            var uiElement = createElement(element);
            if (uiElement != null)
            {
                canvas.Children.Add(uiElement);
                postProcess?.Invoke(uiElement, element);
            }
        }
    }

    /// <summary>
    /// Applies stored properties to a deserialized TextBlock.
    /// </summary>
    public static void ApplyTextProperties(TextBlock tb, CanvasElement element)
    {
        double width = ValidateSize(element.Width);
        double height = ValidateSize(element.Height);
        if (width > 0) tb.Width = width;
        if (height > 0) tb.Height = height;
        if (element.FontSize.HasValue)
            tb.FontSize = element.FontSize.Value;
        if (!string.IsNullOrEmpty(element.ForegroundColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.ForegroundColor);
            tb.Foreground = new SolidColorBrush(color);
        }

        // Text formatting properties
        if (!string.IsNullOrEmpty(element.FontFamily))
            tb.FontFamily = new FontFamily(element.FontFamily);
        if (!string.IsNullOrEmpty(element.TextAlignment))
            tb.TextAlignment = Enum.Parse<TextAlignment>(element.TextAlignment);
        if (!string.IsNullOrEmpty(element.FontWeight))
            tb.FontWeight = element.FontWeight == "Bold" ? FontWeights.Bold : FontWeights.Normal;
        if (!string.IsNullOrEmpty(element.FontStyle))
            tb.FontStyle = element.FontStyle == "Italic" ? FontStyles.Italic : FontStyles.Normal;
    }

    /// <summary>
    /// Applies stored properties to a deserialized Rectangle.
    /// </summary>
    public static void ApplyRectangleProperties(Rectangle rect, CanvasElement element)
    {
        if (!string.IsNullOrEmpty(element.FillColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.FillColor);
            rect.Fill = new SolidColorBrush(color);
        }
        if (!string.IsNullOrEmpty(element.StrokeColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
            rect.Stroke = new SolidColorBrush(color);
        }
        if (element.StrokeThickness.HasValue)
            rect.StrokeThickness = element.StrokeThickness.Value;

        // Extended shape properties
        if (element.RadiusX.HasValue)
            rect.RadiusX = element.RadiusX.Value;
        if (element.RadiusY.HasValue)
            rect.RadiusY = element.RadiusY.Value;

        if (!string.IsNullOrEmpty(element.StrokeDashPattern))
            ApplyDashPattern(rect, element.StrokeDashPattern);

        if (element.UseGradientFill == true)
        {
            var startColor = (Color)ColorConverter.ConvertFromString(element.GradientStartColor ?? "#FFFFFF");
            var endColor = (Color)ColorConverter.ConvertFromString(element.GradientEndColor ?? "#000000");
            rect.Fill = CreateGradientBrush(startColor, endColor, element.GradientAngle ?? 0);
        }
    }

    /// <summary>
    /// Applies stored properties to a deserialized Ellipse.
    /// </summary>
    public static void ApplyEllipseProperties(Ellipse ellipse, CanvasElement element)
    {
        if (!string.IsNullOrEmpty(element.FillColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.FillColor);
            ellipse.Fill = new SolidColorBrush(color);
        }
        if (!string.IsNullOrEmpty(element.StrokeColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
            ellipse.Stroke = new SolidColorBrush(color);
        }
        if (element.StrokeThickness.HasValue)
            ellipse.StrokeThickness = element.StrokeThickness.Value;

        // Extended shape properties
        if (!string.IsNullOrEmpty(element.StrokeDashPattern))
            ApplyDashPattern(ellipse, element.StrokeDashPattern);

        if (element.UseGradientFill == true)
        {
            var startColor = (Color)ColorConverter.ConvertFromString(element.GradientStartColor ?? "#FFFFFF");
            var endColor = (Color)ColorConverter.ConvertFromString(element.GradientEndColor ?? "#000000");
            ellipse.Fill = CreateGradientBrush(startColor, endColor, element.GradientAngle ?? 0);
        }
    }

    /// <summary>
    /// Applies stored properties to a deserialized Polygon.
    /// </summary>
    public static void ApplyPolygonProperties(Polygon poly, CanvasElement element)
    {
        if (!string.IsNullOrEmpty(element.FillColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.FillColor);
            poly.Fill = new SolidColorBrush(color);
        }
        if (!string.IsNullOrEmpty(element.StrokeColor))
        {
            var color = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
            poly.Stroke = new SolidColorBrush(color);
        }
        if (element.StrokeThickness.HasValue)
            poly.StrokeThickness = element.StrokeThickness.Value;
    }

    /// <summary>
    /// Applies stored properties to a deserialized Line (wrapped in Canvas).
    /// </summary>
    public static void ApplyLineProperties(Canvas lineCanvas, CanvasElement element)
    {
        if (lineCanvas.Tag is Tuple<Line, CanvasElement> lineData)
        {
            var ln = lineData.Item1;
            if (!string.IsNullOrEmpty(element.StrokeColor))
            {
                var color = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
                ln.Stroke = new SolidColorBrush(color);
            }
            if (element.StrokeThickness.HasValue)
                ln.StrokeThickness = element.StrokeThickness.Value;
        }
    }

    /// <summary>
    /// Applies stored properties to a deserialized Arrow (wrapped in Canvas).
    /// </summary>
    public static void ApplyArrowProperties(Canvas arrowCanvas, CanvasElement element)
    {
        if (arrowCanvas.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement> arrowData)
        {
            var line = arrowData.Item1;
            if (!string.IsNullOrEmpty(element.StrokeColor))
            {
                var color = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
                line.Stroke = new SolidColorBrush(color);
                // Update arrowheads color too
                if (arrowData.Item2 != null)
                {
                    arrowData.Item2.Fill = new SolidColorBrush(color);
                    arrowData.Item2.Stroke = new SolidColorBrush(color);
                }
                if (arrowData.Item3 != null)
                {
                    arrowData.Item3.Fill = new SolidColorBrush(color);
                    arrowData.Item3.Stroke = new SolidColorBrush(color);
                }
            }
            if (element.StrokeThickness.HasValue)
                line.StrokeThickness = element.StrokeThickness.Value;
        }
    }

    /// <summary>
    /// Parses polygon points from a space-separated string "x1,y1 x2,y2 x3,y3...".
    /// </summary>
    public static PointCollection ParsePolygonPoints(string pointsString)
    {
        var points = new PointCollection();
        var pointPairs = pointsString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pointPairs)
        {
            var coords = pair.Split(',');
            // Use InvariantCulture to parse decimal point (not comma) regardless of system locale
            if (coords.Length == 2 &&
                double.TryParse(coords[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(coords[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y))
            {
                points.Add(new Point(x, y));
            }
        }
        return points;
    }

    /// <summary>
    /// Applies a dash pattern to a shape.
    /// </summary>
    public static void ApplyDashPattern(Shape shape, string pattern)
    {
        shape.StrokeDashArray = pattern switch
        {
            "Dash" => new DoubleCollection { 2, 2 },
            "Dot" => new DoubleCollection { 1, 2 },
            "DashDot" => new DoubleCollection { 2, 2, 1, 2 },
            _ => null
        };
    }

    /// <summary>
    /// Creates a linear gradient brush at the specified angle.
    /// </summary>
    public static LinearGradientBrush CreateGradientBrush(Color startColor, Color endColor, double angle)
    {
        double radians = angle * Math.PI / 180;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - Math.Cos(radians) * 0.5, 0.5 - Math.Sin(radians) * 0.5),
            EndPoint = new Point(0.5 + Math.Cos(radians) * 0.5, 0.5 + Math.Sin(radians) * 0.5)
        };
        brush.GradientStops.Add(new GradientStop(startColor, 0));
        brush.GradientStops.Add(new GradientStop(endColor, 1));
        return brush;
    }

    #endregion
}
