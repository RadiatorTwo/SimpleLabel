using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Element control for Rectangle and Ellipse shape elements.
/// Uses bounds-based resize inherited from ElementControlBase.
/// </summary>
public class ShapeControl : ElementControlBase
{
    private readonly Shape _shape;
    private readonly ElementType _elementType;

    /// <summary>
    /// Gets the type of element this control manages (Rectangle or Ellipse).
    /// </summary>
    public override ElementType ElementType => _elementType;

    /// <summary>
    /// Gets the underlying Shape (Rectangle or Ellipse) being controlled.
    /// </summary>
    public Shape Shape => _shape;

    /// <summary>
    /// Initializes a new instance of the ShapeControl class.
    /// </summary>
    /// <param name="shape">The Rectangle, Ellipse, or Polygon shape to control.</param>
    /// <param name="canvasElement">The data model for serialization.</param>
    /// <param name="elementType">The type of shape (Rectangle, Ellipse, Polygon, Triangle, or Star).</param>
    /// <param name="mainWindow">Optional reference to the MainWindow for property panel access.</param>
    /// <exception cref="ArgumentNullException">Thrown when shape is null.</exception>
    /// <exception cref="ArgumentException">Thrown when elementType is not a supported shape type.</exception>
    public ShapeControl(Shape shape, CanvasElement canvasElement, ElementType elementType, MainWindow? mainWindow = null)
        : base(shape, canvasElement, mainWindow)
    {
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));

        // Validate element type is a supported shape
        if (elementType != ElementType.Rectangle && elementType != ElementType.Ellipse &&
            elementType != ElementType.Polygon && elementType != ElementType.Triangle && elementType != ElementType.Star)
            throw new ArgumentException("ShapeControl only supports Rectangle, Ellipse, Polygon, Triangle, and Star", nameof(elementType));

        _elementType = elementType;
    }

    #region Shape Property Helpers

    /// <summary>
    /// Gets the current fill brush of the shape.
    /// </summary>
    /// <returns>The fill brush, or null if transparent.</returns>
    public Brush? GetFill() => _shape.Fill;

    /// <summary>
    /// Sets the fill brush of the shape and updates CanvasElement.
    /// </summary>
    /// <param name="brush">The brush to set, or null for transparent.</param>
    public void SetFill(Brush? brush)
    {
        _shape.Fill = brush;
        // Note: CanvasElement sync will be done in Phase 6 property panel integration
    }

    /// <summary>
    /// Gets the current stroke brush of the shape.
    /// </summary>
    /// <returns>The stroke brush, or null if not set.</returns>
    public Brush? GetStroke() => _shape.Stroke;

    /// <summary>
    /// Sets the stroke brush of the shape.
    /// </summary>
    /// <param name="brush">The brush to set.</param>
    public void SetStroke(Brush? brush)
    {
        _shape.Stroke = brush;
    }

    /// <summary>
    /// Gets the current stroke thickness.
    /// </summary>
    /// <returns>The stroke thickness value.</returns>
    public double GetStrokeThickness() => _shape.StrokeThickness;

    /// <summary>
    /// Sets the stroke thickness of the shape.
    /// </summary>
    /// <param name="thickness">The thickness to set.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when thickness is negative.</exception>
    public void SetStrokeThickness(double thickness)
    {
        if (thickness < 0)
            throw new ArgumentOutOfRangeException(nameof(thickness), "Stroke thickness cannot be negative");
        _shape.StrokeThickness = thickness;
        _canvasElement.StrokeThickness = thickness;
    }

    /// <summary>
    /// Gets the stroke dash array for dashed lines.
    /// </summary>
    /// <returns>The dash array, or null for solid.</returns>
    public DoubleCollection? GetStrokeDashArray() => _shape.StrokeDashArray;

    /// <summary>
    /// Sets the stroke dash array for dashed lines.
    /// </summary>
    /// <param name="dashArray">The dash array, or null for solid.</param>
    public void SetStrokeDashArray(DoubleCollection? dashArray)
    {
        _shape.StrokeDashArray = dashArray;
    }

    /// <summary>
    /// Gets the bounds of the shape (position and size).
    /// </summary>
    /// <returns>A Rect containing the shape's position and dimensions.</returns>
    public Rect GetBounds()
    {
        return new Rect(
            GetCanvasLeft(),
            GetCanvasTop(),
            _shape.Width,
            _shape.Height);
    }

    /// <summary>
    /// Updates CanvasElement from current shape position and size.
    /// Call after resize operations to sync the data model.
    /// </summary>
    public void SyncCanvasElement()
    {
        _canvasElement.X = GetCanvasLeft();
        _canvasElement.Y = GetCanvasTop();
        _canvasElement.Width = _shape.Width;
        _canvasElement.Height = _shape.Height;
    }

    #endregion

    #region Resize Handling

    /// <summary>
    /// Handle resize operations for bounds-based shape elements.
    /// For Rectangle/Ellipse, uses base class implementation.
    /// For Polygon, scales all points proportionally.
    /// </summary>
    /// <param name="handle">The resize handle being dragged.</param>
    /// <param name="horizontalChange">The horizontal drag delta.</param>
    /// <param name="verticalChange">The vertical drag delta.</param>
    public override void HandleResize(ResizeHandle handle, double horizontalChange, double verticalChange)
    {
        // Polygon requires special handling - scale all points
        if (_shape is Polygon polygon)
        {
            HandlePolygonResize(polygon, handle, horizontalChange, verticalChange);
            SyncCanvasElement();
            return;
        }

        // Rectangle/Ellipse: use base class implementation
        base.HandleResize(handle, horizontalChange, verticalChange);

        // Sync CanvasElement with new position/size for serialization
        SyncCanvasElement();
    }

    /// <summary>
    /// Handles resize for Polygon elements by scaling all points proportionally.
    /// </summary>
    private void HandlePolygonResize(Polygon polygon, ResizeHandle handle, double horizontalChange, double verticalChange)
    {
        if (polygon.Points.Count == 0) return;

        // Calculate current bounds
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in polygon.Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        double currentWidth = maxX - minX;
        double currentHeight = maxY - minY;
        if (currentWidth < 1) currentWidth = 1;
        if (currentHeight < 1) currentHeight = 1;

        double newWidth = currentWidth;
        double newHeight = currentHeight;
        double positionDeltaX = 0;
        double positionDeltaY = 0;

        const double minSize = 20.0;

        switch (handle)
        {
            case ResizeHandle.BottomRight:
                newWidth = currentWidth + horizontalChange;
                newHeight = currentHeight + verticalChange;
                break;

            case ResizeHandle.BottomLeft:
                newWidth = currentWidth - horizontalChange;
                newHeight = currentHeight + verticalChange;
                positionDeltaX = horizontalChange;
                break;

            case ResizeHandle.TopRight:
                newWidth = currentWidth + horizontalChange;
                newHeight = currentHeight - verticalChange;
                positionDeltaY = verticalChange;
                break;

            case ResizeHandle.TopLeft:
                newWidth = currentWidth - horizontalChange;
                newHeight = currentHeight - verticalChange;
                positionDeltaX = horizontalChange;
                positionDeltaY = verticalChange;
                break;

            case ResizeHandle.MiddleRight:
                newWidth = currentWidth + horizontalChange;
                break;

            case ResizeHandle.MiddleLeft:
                newWidth = currentWidth - horizontalChange;
                positionDeltaX = horizontalChange;
                break;

            case ResizeHandle.BottomCenter:
                newHeight = currentHeight + verticalChange;
                break;

            case ResizeHandle.TopCenter:
                newHeight = currentHeight - verticalChange;
                positionDeltaY = verticalChange;
                break;

            default:
                return;
        }

        // Enforce minimum size
        if (newWidth < minSize) newWidth = minSize;
        if (newHeight < minSize) newHeight = minSize;

        // Calculate scale factors
        double scaleX = newWidth / currentWidth;
        double scaleY = newHeight / currentHeight;

        // Scale all points relative to the polygon's origin (minX, minY)
        var newPoints = new PointCollection();
        foreach (var p in polygon.Points)
        {
            double newX = minX + (p.X - minX) * scaleX;
            double newY = minY + (p.Y - minY) * scaleY;
            newPoints.Add(new Point(newX, newY));
        }
        polygon.Points = newPoints;

        // Update position if needed (for handles that move origin)
        if (Math.Abs(positionDeltaX) > 0.001 || Math.Abs(positionDeltaY) > 0.001)
        {
            SetCanvasLeft(GetCanvasLeft() + positionDeltaX);
            SetCanvasTop(GetCanvasTop() + positionDeltaY);
        }

        // Update Width/Height for hit-testing (use new bounds)
        polygon.Width = newWidth;
        polygon.Height = newHeight;
    }

    #endregion

    #region IElementControl Implementation

    /// <summary>
    /// Update the properties panel with this element's values.
    /// </summary>
    /// <remarks>
    /// Populates Shape-specific properties: fill color, stroke color, stroke thickness,
    /// dash pattern, gradient fill options. Handles Rectangle-specific controls (corner radius)
    /// and Polygon-specific behavior (read-only Width/Height).
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

        // Hide X2, Y2 controls (not applicable for shapes)
        _mainWindow.labelX2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyX2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.labelY2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyY2.Visibility = System.Windows.Visibility.Collapsed;

        // Basic shape styling controls
        if (_shape.Fill is SolidColorBrush fillBrush)
        {
            _mainWindow.propertyFillColorPreview.Fill = fillBrush;
        }
        if (_shape.Stroke is SolidColorBrush strokeBrush)
        {
            _mainWindow.propertyStrokeColorPreview.Fill = strokeBrush;
        }
        _mainWindow.propertyStrokeThickness.Value = _shape.StrokeThickness;

        // Enable basic controls
        _mainWindow.propertyFillColorButton.IsEnabled = true;
        _mainWindow.propertyStrokeColorButton.IsEnabled = true;
        _mainWindow.propertyStrokeThickness.IsEnabled = true;

        // Rectangle-specific: Corner radius
        bool isRectangle = _shape is Rectangle;
        _mainWindow.labelRadiusX.Visibility = isRectangle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        _mainWindow.propertyRadiusX.Visibility = isRectangle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        _mainWindow.labelRadiusY.Visibility = isRectangle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        _mainWindow.propertyRadiusY.Visibility = isRectangle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        if (_shape is Rectangle rect)
        {
            _mainWindow.propertyRadiusX.Value = rect.RadiusX;
            _mainWindow.propertyRadiusX.IsEnabled = true;
            _mainWindow.propertyRadiusY.Value = rect.RadiusY;
            _mainWindow.propertyRadiusY.IsEnabled = true;
        }

        // Polygon-specific: Make Width/Height read-only (calculated from bounds)
        if (_shape is Polygon polygon)
        {
            // Calculate bounds from points
            if (polygon.Points.Count > 0)
            {
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                foreach (var p in polygon.Points)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }

                double width = maxX - minX;
                double height = maxY - minY;

                _mainWindow.propertyWidth.Value = Math.Round(width * PIXELS_TO_MM, 2);
                _mainWindow.propertyHeight.Value = Math.Round(height * PIXELS_TO_MM, 2);
            }

            // Make Width/Height read-only for polygons (geometry defined by points)
            _mainWindow.propertyWidth.IsEnabled = false;
            _mainWindow.propertyHeight.IsEnabled = false;
        }

        // Stroke dash pattern
        _mainWindow.propertyStrokeDashPattern.SelectedIndex = DetectDashPatternIndex(_shape.StrokeDashArray);
        _mainWindow.propertyStrokeDashPattern.IsEnabled = true;

        // Gradient fill
        bool hasGradient = _shape.Fill is LinearGradientBrush;
        _mainWindow.propertyUseGradientFill.IsChecked = hasGradient;
        _mainWindow.propertyUseGradientFill.IsEnabled = true;

        if (hasGradient)
        {
            var gradientBrush = (LinearGradientBrush)_shape.Fill;
            _mainWindow.panelGradientControls.Visibility = System.Windows.Visibility.Visible;

            if (gradientBrush.GradientStops.Count >= 2)
            {
                _mainWindow.propertyGradientStartPreview.Fill = new SolidColorBrush(gradientBrush.GradientStops[0].Color);
                _mainWindow.propertyGradientEndPreview.Fill = new SolidColorBrush(gradientBrush.GradientStops[^1].Color);
            }

            double angle = CalculateGradientAngle(gradientBrush);
            _mainWindow.propertyGradientAngle.Value = angle;
            _mainWindow.propertyGradientAngleValue.Text = $"{angle:F0}";

            _mainWindow.propertyGradientStartButton.IsEnabled = true;
            _mainWindow.propertyGradientEndButton.IsEnabled = true;
            _mainWindow.propertyGradientAngle.IsEnabled = true;
        }
        else
        {
            _mainWindow.panelGradientControls.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Detects the dash pattern index for combo box selection.
    /// </summary>
    private int DetectDashPatternIndex(DoubleCollection? dashArray)
    {
        if (dashArray == null || dashArray.Count == 0) return 0; // Solid

        // Compare with known patterns
        if (IsArrayEqual(dashArray, new[] { 2.0, 2.0 })) return 1; // Dash
        if (IsArrayEqual(dashArray, new[] { 1.0, 2.0 })) return 2; // Dot
        if (IsArrayEqual(dashArray, new[] { 2.0, 2.0, 1.0, 2.0 })) return 3; // DashDot

        return 0; // Solid as default
    }

    /// <summary>
    /// Compares a DoubleCollection with a pattern array.
    /// </summary>
    private bool IsArrayEqual(DoubleCollection array, double[] pattern)
    {
        if (array.Count != pattern.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (Math.Abs(array[i] - pattern[i]) > 0.01) return false;
        }
        return true;
    }

    /// <summary>
    /// Calculates the gradient angle from the brush's start and end points.
    /// </summary>
    private double CalculateGradientAngle(LinearGradientBrush brush)
    {
        double dx = brush.EndPoint.X - brush.StartPoint.X;
        double dy = brush.EndPoint.Y - brush.StartPoint.Y;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        return (angle + 360) % 360;
    }

    /// <summary>
    /// Apply property changes from the properties panel.
    /// </summary>
    /// <param name="propertyName">The name of the property being changed.</param>
    /// <param name="newValue">The new value for the property.</param>
    public override void ApplyPropertyChanges(string propertyName, object newValue)
    {
        throw new NotImplementedException("Property panel integration will be implemented in Phase 6.");
    }

    #endregion
}
