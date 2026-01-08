using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Control for managing Arrow elements with endpoint-based positioning (X1/Y1/X2/Y2) and arrowhead management.
/// Arrows are wrapped in a Canvas for better hit-testing and to contain the arrowhead polygons.
/// </summary>
public class ArrowControl : ElementControlBase
{
    /// <summary>
    /// The Line shape within the Canvas wrapper (arrow shaft).
    /// </summary>
    private readonly Line _line;

    /// <summary>
    /// The Canvas wrapper that contains the Line and arrowheads.
    /// </summary>
    private readonly Canvas _arrowCanvas;

    /// <summary>
    /// The arrowhead polygon at the start point (if present).
    /// </summary>
    private Polygon? _startArrowhead;

    /// <summary>
    /// The arrowhead polygon at the end point (if present).
    /// </summary>
    private Polygon? _endArrowhead;

    /// <summary>
    /// Minimum arrow length to prevent collapsing to a point.
    /// </summary>
    private const double MinimumLength = 10;

    /// <summary>
    /// Padding around the arrow for hit-testing.
    /// </summary>
    private const double Padding = 10;

    /// <summary>
    /// Gets the type of element this control manages.
    /// </summary>
    public override ElementType ElementType => ElementType.Arrow;

    /// <summary>
    /// Gets whether this element uses endpoint-based positioning.
    /// Arrows use X1/Y1/X2/Y2 instead of X/Y/Width/Height.
    /// </summary>
    public override bool UsesEndpoints => true;

    /// <summary>
    /// Gets the internal Line shape (arrow shaft).
    /// </summary>
    public Line Line => _line;

    /// <summary>
    /// Gets the Canvas wrapper containing the Line and arrowheads.
    /// </summary>
    public Canvas ArrowCanvas => _arrowCanvas;

    /// <summary>
    /// Gets the start arrowhead polygon (may be null if no start arrowhead).
    /// </summary>
    public Polygon? StartArrowhead => _startArrowhead;

    /// <summary>
    /// Gets the end arrowhead polygon (may be null if no end arrowhead).
    /// </summary>
    public Polygon? EndArrowhead => _endArrowhead;

    /// <summary>
    /// Initializes a new instance of the ArrowControl class.
    /// </summary>
    /// <param name="arrowCanvas">The Canvas wrapper containing the Line and arrowheads.</param>
    /// <param name="line">The Line shape within the Canvas.</param>
    /// <param name="startArrowhead">The arrowhead at the start point (can be null).</param>
    /// <param name="endArrowhead">The arrowhead at the end point (can be null).</param>
    /// <param name="canvasElement">The data model for serialization.</param>
    /// <param name="mainWindow">Optional reference to the MainWindow for property panel access.</param>
    public ArrowControl(Canvas arrowCanvas, Line line, Polygon? startArrowhead, Polygon? endArrowhead, CanvasElement canvasElement, MainWindow? mainWindow = null)
        : base(arrowCanvas, canvasElement, mainWindow)
    {
        _arrowCanvas = arrowCanvas ?? throw new ArgumentNullException(nameof(arrowCanvas));
        _line = line ?? throw new ArgumentNullException(nameof(line));
        _startArrowhead = startArrowhead;
        _endArrowhead = endArrowhead;
    }

    /// <summary>
    /// Update the properties panel with this element's values.
    /// </summary>
    /// <remarks>
    /// Populates Arrow-specific properties: X1/Y1/X2/Y2 endpoints, stroke color, thickness,
    /// and arrow-specific controls (start/end arrows, arrowhead size).
    /// Hides non-applicable controls like fill color and gradient.
    /// </remarks>
    public override void PopulatePropertiesPanel()
    {
        if (_mainWindow == null)
            return;

        // Show shape styling and arrow controls groups, hide others
        _mainWindow.groupTextFormatting.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupShapeStyling.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.groupImageFilters.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupArrowControls.Visibility = System.Windows.Visibility.Visible;

        // Get canvas position for calculating absolute coordinates
        double canvasLeft = Canvas.GetLeft(_arrowCanvas);
        double canvasTop = Canvas.GetTop(_arrowCanvas);
        if (double.IsNaN(canvasLeft)) canvasLeft = 0.0;
        if (double.IsNaN(canvasTop)) canvasTop = 0.0;

        // Calculate X1, Y1, X2, Y2
        double x1 = canvasLeft;
        double y1 = canvasTop;
        double x2 = canvasLeft + _line.X2;
        double y2 = canvasTop + _line.Y2;

        // Show X1, Y1 (as X, Y)
        _mainWindow.propertyX.Value = Math.Round(x1 * PIXELS_TO_MM, 2);
        _mainWindow.propertyY.Value = Math.Round(y1 * PIXELS_TO_MM, 2);

        // Show X2, Y2 controls
        _mainWindow.labelX2.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.propertyX2.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.labelY2.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.propertyY2.Visibility = System.Windows.Visibility.Visible;

        _mainWindow.propertyX2.Value = Math.Round(x2 * PIXELS_TO_MM, 2);
        _mainWindow.propertyY2.Value = Math.Round(y2 * PIXELS_TO_MM, 2);
        _mainWindow.propertyX2.IsEnabled = true;
        _mainWindow.propertyY2.IsEnabled = true;

        // Hide Width/Height
        _mainWindow.propertyWidth.IsEnabled = false;
        _mainWindow.propertyHeight.IsEnabled = false;

        // Show stroke color and thickness
        if (_line.Stroke is System.Windows.Media.SolidColorBrush strokeBrush)
        {
            _mainWindow.propertyStrokeColorPreview.Fill = strokeBrush;
        }
        _mainWindow.propertyStrokeThickness.Value = _line.StrokeThickness;

        // Enable stroke controls
        _mainWindow.propertyStrokeColorButton.IsEnabled = true;
        _mainWindow.propertyStrokeThickness.IsEnabled = true;

        // Arrow-specific controls
        _mainWindow.propertyHasStartArrow.IsChecked = _canvasElement.HasStartArrow ?? false;
        _mainWindow.propertyHasEndArrow.IsChecked = _canvasElement.HasEndArrow ?? true;
        _mainWindow.propertyArrowheadSize.Value = _canvasElement.ArrowheadSize ?? 10;
        _mainWindow.propertyArrowheadSizeValue.Text = (_canvasElement.ArrowheadSize ?? 10).ToString("F0");

        _mainWindow.propertyHasStartArrow.IsEnabled = true;
        _mainWindow.propertyHasEndArrow.IsEnabled = true;
        _mainWindow.propertyArrowheadSize.IsEnabled = true;

        // Hide other shape controls
        _mainWindow.propertyFillColorButton.IsEnabled = false;
        _mainWindow.propertyStrokeDashPattern.IsEnabled = false;
        _mainWindow.propertyUseGradientFill.IsEnabled = false;
        _mainWindow.labelRadiusX.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyRadiusX.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.labelRadiusY.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyRadiusY.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.panelGradientControls.Visibility = System.Windows.Visibility.Collapsed;
    }

    /// <summary>
    /// Apply property changes from the properties panel.
    /// </summary>
    /// <param name="propertyName">The name of the property being changed.</param>
    /// <param name="newValue">The new value for the property.</param>
    public override void ApplyPropertyChanges(string propertyName, object newValue)
    {
        // TODO: Implement in Phase 6 when integrating with MainWindow
        throw new NotImplementedException("Property panel integration will be completed in Phase 6.");
    }

    #region Endpoint Helpers

    /// <summary>
    /// Gets the absolute start point (X1, Y1) of the arrow.
    /// </summary>
    /// <returns>The start point in absolute canvas coordinates.</returns>
    public Point GetStartPoint()
    {
        return new Point(_canvasElement.X, _canvasElement.Y);
    }

    /// <summary>
    /// Gets the absolute end point (X2, Y2) of the arrow.
    /// </summary>
    /// <returns>The end point in absolute canvas coordinates.</returns>
    public Point GetEndPoint()
    {
        return new Point(_canvasElement.X2 ?? _canvasElement.X, _canvasElement.Y2 ?? _canvasElement.Y);
    }

    /// <summary>
    /// Gets both endpoints as a tuple.
    /// </summary>
    /// <returns>A tuple containing the start and end points.</returns>
    public (Point Start, Point End) GetEndpoints()
    {
        return (GetStartPoint(), GetEndPoint());
    }

    /// <summary>
    /// Gets the arrow length.
    /// </summary>
    /// <returns>The distance between start and end points.</returns>
    public double GetLength()
    {
        var (start, end) = GetEndpoints();
        return Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
    }

    #endregion

    #region Arrowhead Helpers

    /// <summary>
    /// Creates a triangular arrowhead polygon at the specified position and angle.
    /// </summary>
    /// <param name="x">X coordinate of the arrowhead tip.</param>
    /// <param name="y">Y coordinate of the arrowhead tip.</param>
    /// <param name="angle">Rotation angle in radians.</param>
    /// <param name="size">Size of the arrowhead.</param>
    /// <returns>A Polygon representing the arrowhead.</returns>
    public static Polygon CreateArrowhead(double x, double y, double angle, double size)
    {
        var arrowhead = new Polygon
        {
            Fill = Brushes.Black,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };

        double halfSize = size / 2;
        Point tip = new Point(x, y);
        Point left = new Point(x - size, y - halfSize);
        Point right = new Point(x - size, y + halfSize);

        Point leftRotated = RotatePoint(left, tip, angle);
        Point rightRotated = RotatePoint(right, tip, angle);

        arrowhead.Points.Add(tip);
        arrowhead.Points.Add(leftRotated);
        arrowhead.Points.Add(rightRotated);

        return arrowhead;
    }

    /// <summary>
    /// Rotates a point around a center point by the specified angle.
    /// </summary>
    /// <param name="point">The point to rotate.</param>
    /// <param name="center">The center point of rotation.</param>
    /// <param name="angle">The rotation angle in radians.</param>
    /// <returns>The rotated point.</returns>
    private static Point RotatePoint(Point point, Point center, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    /// <summary>
    /// Gets the arrow angle in radians from line endpoints.
    /// </summary>
    /// <returns>The angle in radians from start to end point.</returns>
    public double GetAngle()
    {
        var (start, end) = GetEndpoints();
        return Math.Atan2(end.Y - start.Y, end.X - start.X);
    }

    /// <summary>
    /// Recreates arrowheads based on current CanvasElement settings.
    /// Call this after changing HasStartArrow, HasEndArrow, or ArrowheadSize.
    /// </summary>
    public void RecreateArrowheads()
    {
        // Remove existing arrowheads
        if (_startArrowhead != null)
        {
            _arrowCanvas.Children.Remove(_startArrowhead);
            _startArrowhead = null;
        }
        if (_endArrowhead != null)
        {
            _arrowCanvas.Children.Remove(_endArrowhead);
            _endArrowhead = null;
        }

        double arrowheadSize = _canvasElement.ArrowheadSize ?? 10;
        double angle = GetAngle();

        // Create start arrowhead if needed (at line start point, pointing backward)
        if (_canvasElement.HasStartArrow ?? false)
        {
            _startArrowhead = CreateArrowhead(_line.X1, _line.Y1, angle + Math.PI, arrowheadSize);
            _arrowCanvas.Children.Insert(0, _startArrowhead);
        }

        // Create end arrowhead if needed (at line endpoint, pointing forward)
        if (_canvasElement.HasEndArrow ?? true)
        {
            _endArrowhead = CreateArrowhead(_line.X2, _line.Y2, angle, arrowheadSize);
            _arrowCanvas.Children.Insert(0, _endArrowhead);
        }
    }

    #endregion

    #region Resize Handling

    /// <summary>
    /// Handle resize operations for endpoint-based Arrow elements.
    /// After moving endpoints, arrowheads are automatically recreated.
    /// </summary>
    /// <param name="handle">The resize handle being dragged.</param>
    /// <param name="horizontalChange">The horizontal movement delta.</param>
    /// <param name="verticalChange">The vertical movement delta.</param>
    public override void HandleResize(ResizeHandle handle, double horizontalChange, double verticalChange)
    {
        // Get current absolute endpoints from CanvasElement
        double x1 = _canvasElement.X;
        double y1 = _canvasElement.Y;
        double x2 = _canvasElement.X2 ?? x1;
        double y2 = _canvasElement.Y2 ?? y1;

        switch (handle)
        {
            case ResizeHandle.StartPoint:
            case ResizeHandle.TopLeft:
                // Move start point (X1/Y1)
                x1 += horizontalChange;
                y1 += verticalChange;
                break;

            case ResizeHandle.EndPoint:
            case ResizeHandle.BottomRight:
                // Move end point (X2/Y2)
                x2 += horizontalChange;
                y2 += verticalChange;
                break;

            default:
                // Other handles don't apply to arrows - ignore
                return;
        }

        // Enforce minimum length to prevent collapsing
        double length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        if (length < MinimumLength)
            return;

        // Update the canvas, line, and arrowheads using the helper method
        UpdateCanvasFromEndpoints(x1, y1, x2, y2);
    }

    /// <summary>
    /// Updates the canvas wrapper, line, and arrowheads to reflect new absolute endpoints.
    /// This handles the coordinate conversion between absolute canvas coordinates and
    /// relative line coordinates within the canvas wrapper, then recreates arrowheads.
    /// </summary>
    /// <param name="x1">Absolute X coordinate of start point.</param>
    /// <param name="y1">Absolute Y coordinate of start point.</param>
    /// <param name="x2">Absolute X coordinate of end point.</param>
    /// <param name="y2">Absolute Y coordinate of end point.</param>
    public void UpdateCanvasFromEndpoints(double x1, double y1, double x2, double y2)
    {
        // Calculate bounding box
        double minX = Math.Min(x1, x2);
        double minY = Math.Min(y1, y2);
        double maxX = Math.Max(x1, x2);
        double maxY = Math.Max(y1, y2);

        double width = maxX - minX;
        double height = maxY - minY;

        // Add padding for arrowheads (use larger of fixed padding or arrowhead size)
        double arrowheadSize = _canvasElement.ArrowheadSize ?? 10;
        double padding = Math.Max(Padding, arrowheadSize);
        double canvasWidth = Math.Max(width, padding * 2);
        double canvasHeight = Math.Max(height, padding * 2);

        // Calculate centering offset
        double offsetX = (canvasWidth - width) / 2;
        double offsetY = (canvasHeight - height) / 2;

        // Position canvas (adjusted for centering padding)
        double canvasLeft = minX - offsetX;
        double canvasTop = minY - offsetY;

        Canvas.SetLeft(_arrowCanvas, canvasLeft);
        Canvas.SetTop(_arrowCanvas, canvasTop);

        // Set canvas size
        _arrowCanvas.Width = canvasWidth;
        _arrowCanvas.Height = canvasHeight;

        // Set line coordinates relative to canvas (centered with padding)
        _line.X1 = (x1 - minX) + offsetX;
        _line.Y1 = (y1 - minY) + offsetY;
        _line.X2 = (x2 - minX) + offsetX;
        _line.Y2 = (y2 - minY) + offsetY;

        // Update CanvasElement with absolute coordinates
        _canvasElement.X = x1;
        _canvasElement.Y = y1;
        _canvasElement.X2 = x2;
        _canvasElement.Y2 = y2;

        // Recreate arrowheads at new positions and angles
        RecreateArrowheads();
    }

    #endregion
}
