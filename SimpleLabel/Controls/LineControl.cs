using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Control for managing Line elements with endpoint-based positioning (X1/Y1/X2/Y2).
/// Lines are wrapped in a Canvas for better hit-testing, so this control manages
/// both the Canvas wrapper and the internal Line shape.
/// </summary>
public class LineControl : ElementControlBase
{
    /// <summary>
    /// The Line shape within the Canvas wrapper.
    /// </summary>
    private readonly Line _line;

    /// <summary>
    /// The Canvas wrapper that contains the Line for hit-testing.
    /// </summary>
    private readonly Canvas _lineCanvas;

    /// <summary>
    /// Gets the type of element this control manages.
    /// </summary>
    public override ElementType ElementType => ElementType.Line;

    /// <summary>
    /// Gets whether this element uses endpoint-based positioning.
    /// Lines use X1/Y1/X2/Y2 instead of X/Y/Width/Height.
    /// </summary>
    public override bool UsesEndpoints => true;

    /// <summary>
    /// Gets the internal Line shape.
    /// </summary>
    public Line Line => _line;

    /// <summary>
    /// Gets the Canvas wrapper containing the Line.
    /// </summary>
    public Canvas LineCanvas => _lineCanvas;

    /// <summary>
    /// Minimum line length to prevent collapsing to a point.
    /// </summary>
    private const double MinimumLength = 10;

    /// <summary>
    /// Initializes a new instance of the LineControl class.
    /// </summary>
    /// <param name="lineCanvas">The Canvas wrapper containing the Line.</param>
    /// <param name="line">The Line shape within the Canvas.</param>
    /// <param name="canvasElement">The data model for serialization.</param>
    /// <param name="mainWindow">Optional reference to the MainWindow for property panel access.</param>
    public LineControl(Canvas lineCanvas, Line line, CanvasElement canvasElement, MainWindow? mainWindow = null)
        : base(lineCanvas, canvasElement, mainWindow)
    {
        _lineCanvas = lineCanvas ?? throw new ArgumentNullException(nameof(lineCanvas));
        _line = line ?? throw new ArgumentNullException(nameof(line));
    }

    /// <summary>
    /// Update the properties panel with this element's values.
    /// </summary>
    /// <remarks>
    /// Populates Line-specific properties: X1/Y1/X2/Y2 endpoints, stroke color, thickness.
    /// Hides non-applicable controls like fill color, gradient, and arrow controls.
    /// </remarks>
    public override void PopulatePropertiesPanel()
    {
        if (_mainWindow == null)
            return;

        // Show shape styling group, hide others
        _mainWindow.groupTextFormatting.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupShapeStyling.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.groupImageFilters.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupArrowControls.Visibility = System.Windows.Visibility.Collapsed;

        // Get canvas position for calculating absolute coordinates
        double canvasLeft = Canvas.GetLeft(_lineCanvas);
        double canvasTop = Canvas.GetTop(_lineCanvas);
        if (double.IsNaN(canvasLeft)) canvasLeft = 0.0;
        if (double.IsNaN(canvasTop)) canvasTop = 0.0;

        // Calculate absolute coordinates
        double x1 = canvasLeft + _line.X1;
        double y1 = canvasTop + _line.Y1;
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

        // Hide Width/Height (not applicable for Line)
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

        // Hide fill color and other shape-specific controls
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
        // Get current absolute endpoints
        double x1 = _canvasElement.X;
        double y1 = _canvasElement.Y;
        double x2 = _canvasElement.X2 ?? x1;
        double y2 = _canvasElement.Y2 ?? y1;

        switch (propertyName)
        {
            case "X1":
            case "X":
                // newValue is in mm, convert to pixels
                x1 = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                UpdateCanvasFromEndpoints(x1, y1, x2, y2);
                break;

            case "Y1":
            case "Y":
                y1 = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                UpdateCanvasFromEndpoints(x1, y1, x2, y2);
                break;

            case "X2":
                x2 = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                UpdateCanvasFromEndpoints(x1, y1, x2, y2);
                break;

            case "Y2":
                y2 = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                UpdateCanvasFromEndpoints(x1, y1, x2, y2);
                break;

            case "StrokeColor":
                var color = (System.Windows.Media.Color)newValue;
                _line.Stroke = new System.Windows.Media.SolidColorBrush(color);
                _canvasElement.StrokeColor = color.ToString();
                break;

            case "StrokeThickness":
                var thickness = Convert.ToDouble(newValue);
                _line.StrokeThickness = thickness;
                _canvasElement.StrokeThickness = thickness;
                break;

            default:
                // Unknown property - ignore silently
                return;
        }

        // Mark document as dirty
        if (_mainWindow != null)
        {
            _mainWindow.isDirty = true;
        }
    }

    #region Endpoint Helpers

    /// <summary>
    /// Gets the absolute start point (X1, Y1) of the line.
    /// </summary>
    /// <returns>The start point in absolute canvas coordinates.</returns>
    public Point GetStartPoint()
    {
        return new Point(_canvasElement.X, _canvasElement.Y);
    }

    /// <summary>
    /// Gets the absolute end point (X2, Y2) of the line.
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
    /// Gets the line length.
    /// </summary>
    /// <returns>The distance between start and end points.</returns>
    public double GetLength()
    {
        var (start, end) = GetEndpoints();
        return Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
    }

    /// <summary>
    /// Updates the canvas wrapper and internal line to reflect new absolute endpoints.
    /// This handles the coordinate conversion between absolute canvas coordinates and
    /// relative line coordinates within the canvas wrapper.
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

        // Add padding for better hit-testing (matches MainWindow.UpdateLineCanvas)
        const double padding = 10;
        double canvasWidth = Math.Max(width, padding * 2);
        double canvasHeight = Math.Max(height, padding * 2);

        // Calculate centering offset
        double offsetX = (canvasWidth - width) / 2;
        double offsetY = (canvasHeight - height) / 2;

        // Position canvas (adjusted for centering padding)
        double canvasLeft = minX - offsetX;
        double canvasTop = minY - offsetY;

        Canvas.SetLeft(_lineCanvas, canvasLeft);
        Canvas.SetTop(_lineCanvas, canvasTop);

        // Set canvas size
        _lineCanvas.Width = canvasWidth;
        _lineCanvas.Height = canvasHeight;

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
    }

    #endregion

    /// <summary>
    /// Handle resize operations for endpoint-based Line elements.
    /// Lines use StartPoint/EndPoint handles instead of corner handles.
    /// </summary>
    /// <param name="handle">The resize handle being dragged.</param>
    /// <param name="horizontalChange">The horizontal movement delta.</param>
    /// <param name="verticalChange">The vertical movement delta.</param>
    public override void HandleResize(ResizeHandle handle, double horizontalChange, double verticalChange)
    {
        // Calculate current absolute endpoints from canvas position and relative line coordinates
        // This is more reliable than reading from CanvasElement, which may not be in sync
        double canvasLeft = Canvas.GetLeft(_lineCanvas);
        double canvasTop = Canvas.GetTop(_lineCanvas);
        if (double.IsNaN(canvasLeft)) canvasLeft = 0.0;
        if (double.IsNaN(canvasTop)) canvasTop = 0.0;

        double x1 = canvasLeft + _line.X1;
        double y1 = canvasTop + _line.Y1;
        double x2 = canvasLeft + _line.X2;
        double y2 = canvasTop + _line.Y2;

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
                // Other handles don't apply to lines - ignore
                return;
        }

        // Enforce minimum length to prevent collapsing
        double length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        if (length < MinimumLength)
            return;

        // Update the canvas and line using the helper method
        UpdateCanvasFromEndpoints(x1, y1, x2, y2);
    }
}
