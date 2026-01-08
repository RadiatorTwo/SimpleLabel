using System.IO;
using System.Printing;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using SimpleLabel.Models;
using SimpleLabel.Commands;
using SimpleLabel.Controls;
using WpfColor = System.Windows.Media.Color;
using FormsColor = System.Drawing.Color;

namespace SimpleLabel;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool isDragging = false;
    private UIElement? draggedElement = null;
    private Point dragStartPoint;
    private Point dragInitialPosition; // For undo/redo - initial position before drag
    private Point dragInitialEndPosition; // For Line elements - stores X2/Y2
    private UIElement? selectedElement = null;
    private string? currentFilePath = null;
    private bool isDirty = false;
    private readonly Commands.CommandManager commandManager = new();

    // Properties panel initial values for undo/redo
    private double initialPropertyValue;
    private string? initialStringValue;
    private bool initialBoolValue;

    // Grid/Snap settings
    private bool snapToGrid = true;
    private double gridSize = 5; // Default 5 mm

    // Unit conversion constants (96 DPI)
    private const double MM_TO_PIXELS = 96.0 / 25.4;
    private const double PIXELS_TO_MM = 25.4 / 96.0;

    // Element control registry for IElementControl lookup
    private readonly Dictionary<UIElement, IElementControl> _elementControls = new();

    public MainWindow()
    {
        InitializeComponent();

        // Default canvas size: 100x150mm (378x567 pixels at 96 DPI)
        // Now configurable via Canvas menu

        // Handle window closing to prompt for unsaved changes
        Closing += Window_Closing;

        // Initialize status bar display
        UpdateStatusBar();

        // Populate font family ComboBox with system fonts
        propertyFontFamily.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(name => name)
            .ToList();

        // Set snap to grid menu item checked by default
        menuSnapToGrid.IsChecked = snapToGrid;

        // Initialize grid background and menu checks
        UpdateGridBackground();
        UpdateGridSizeMenuChecks();
    }

    #region Element Control Registry

    /// <summary>
    /// Registers an IElementControl for a UI element, enabling ResizeAdorner delegation.
    /// </summary>
    /// <param name="element">The UI element to register.</param>
    /// <param name="control">The IElementControl that manages this element.</param>
    public void RegisterElementControl(UIElement element, IElementControl control)
    {
        _elementControls[element] = control;
    }

    /// <summary>
    /// Unregisters an IElementControl for a UI element.
    /// </summary>
    /// <param name="element">The UI element to unregister.</param>
    public void UnregisterElementControl(UIElement element)
    {
        _elementControls.Remove(element);
    }

    /// <summary>
    /// Gets the IElementControl for a UI element, or null if not registered.
    /// </summary>
    /// <param name="element">The UI element to look up.</param>
    /// <returns>The IElementControl managing the element, or null.</returns>
    public IElementControl? GetElementControl(UIElement element)
    {
        return _elementControls.TryGetValue(element, out var control) ? control : null;
    }

    #endregion

    private void SelectElement(UIElement? element)
    {
        // Deselect previous element
        if (selectedElement != null)
        {
            var oldLayer = AdornerLayer.GetAdornerLayer(selectedElement);
            if (oldLayer != null)
            {
                var adorners = oldLayer.GetAdorners(selectedElement);
                if (adorners != null)
                {
                    foreach (var adorner in adorners)
                        oldLayer.Remove(adorner);
                }
            }
        }

        // Select new element
        selectedElement = element;
        if (selectedElement != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(selectedElement);
            if (layer != null)
                layer.Add(new ResizeAdorner(selectedElement));
        }

        // Update properties panel
        UpdatePropertiesPanel();
    }

    internal void UpdatePropertiesPanel()
    {
        if (selectedElement == null)
        {
            // Hide all section GroupBoxes
            groupTextFormatting.Visibility = Visibility.Collapsed;
            groupShapeStyling.Visibility = Visibility.Collapsed;
            groupImageFilters.Visibility = Visibility.Collapsed;
            groupArrowControls.Visibility = Visibility.Collapsed;

            // Hide X2/Y2 fields
            labelX2.Visibility = Visibility.Collapsed;
            propertyX2.Visibility = Visibility.Collapsed;
            labelY2.Visibility = Visibility.Collapsed;
            propertyY2.Visibility = Visibility.Collapsed;

            // Clear and disable Position & Size
            propertyX.Value = 0;
            propertyY.Value = 0;
            propertyWidth.Value = 0;
            propertyHeight.Value = 0;
            propertyX.IsEnabled = false;
            propertyY.IsEnabled = false;
            propertyWidth.IsEnabled = false;
            propertyHeight.IsEnabled = false;
        }
        else
        {
            // Get position
            double left = Canvas.GetLeft(selectedElement);
            double top = Canvas.GetTop(selectedElement);
            if (double.IsNaN(left)) left = 0.0;
            if (double.IsNaN(top)) top = 0.0;

            // Get size from FrameworkElement
            double width = 0;
            double height = 0;
            if (selectedElement is FrameworkElement fe)
            {
                width = fe.Width;
                height = fe.Height;
            }

            // Populate and enable Position & Size fields (convert pixels to mm)
            propertyX.Value = Math.Round(left * PIXELS_TO_MM, 2);
            propertyY.Value = Math.Round(top * PIXELS_TO_MM, 2);
            propertyWidth.Value = Math.Round(width * PIXELS_TO_MM, 2);
            propertyHeight.Value = Math.Round(height * PIXELS_TO_MM, 2);
            propertyX.IsEnabled = true;
            propertyY.IsEnabled = true;
            propertyWidth.IsEnabled = true;
            propertyHeight.IsEnabled = true;

            // Try to delegate to IElementControl if registered
            var elementControl = GetElementControl(selectedElement);
            if (elementControl != null)
            {
                elementControl.PopulatePropertiesPanel();
                return;
            }

            // Legacy fallback: Check if element is TextBlock for text formatting controls
            if (selectedElement is TextBlock textBlock)
            {
                groupTextFormatting.Visibility = Visibility.Visible;
                groupShapeStyling.Visibility = Visibility.Collapsed;
                groupImageFilters.Visibility = Visibility.Collapsed;

                // Enable and populate text formatting controls
                propertyText.Text = textBlock.Text;
                propertyFontFamily.SelectedItem = textBlock.FontFamily.Source;

                // Find the closest matching font size item
                double currentFontSize = textBlock.FontSize;
                ComboBoxItem? bestMatch = null;
                double smallestDifference = double.MaxValue;

                foreach (var item in propertyFontSize.Items)
                {
                    if (item is ComboBoxItem cbi && cbi.Tag != null)
                    {
                        if (double.TryParse(cbi.Tag.ToString(), out double fontSize))
                        {
                            double difference = Math.Abs(fontSize - currentFontSize);
                            if (difference < smallestDifference)
                            {
                                smallestDifference = difference;
                                bestMatch = cbi;
                            }
                        }
                    }
                }

                if (bestMatch != null && smallestDifference < 0.5)
                {
                    propertyFontSize.SelectedItem = bestMatch;
                }
                else
                {
                    // No close match, calculate and show mm equivalent
                    // Approximate actual character height: pt * 0.265 ≈ mm
                    // (accounts for cap height being ~70% of font size)
                    double mm = currentFontSize * 0.265;
                    propertyFontSize.SelectedItem = null;
                    propertyFontSize.Text = $"{mm:F0}mm ({currentFontSize:F0}pt)";
                }

                // Get foreground color and set preview
                if (textBlock.Foreground is SolidColorBrush brush)
                {
                    propertyColorPreview.Fill = brush;
                }

                // Set alignment
                propertyAlignment.SelectedIndex = textBlock.TextAlignment switch
                {
                    TextAlignment.Left => 0,
                    TextAlignment.Center => 1,
                    TextAlignment.Right => 2,
                    _ => 0
                };

                // Set bold and italic
                propertyBold.IsChecked = textBlock.FontWeight == FontWeights.Bold;
                propertyItalic.IsChecked = textBlock.FontStyle == FontStyles.Italic;

                // Enable controls
                propertyText.IsEnabled = true;
                propertyFontFamily.IsEnabled = true;
                propertyFontSize.IsEnabled = true;
                propertyColorButton.IsEnabled = true;
                propertyAlignment.IsEnabled = true;
                propertyBold.IsEnabled = true;
                propertyItalic.IsEnabled = true;
            }
            else if (selectedElement is Shape shape)
            {
                groupTextFormatting.Visibility = Visibility.Collapsed;
                groupShapeStyling.Visibility = Visibility.Visible;
                groupImageFilters.Visibility = Visibility.Collapsed;

                // Basic shape styling controls
                if (shape.Fill is SolidColorBrush fillBrush)
                {
                    propertyFillColorPreview.Fill = fillBrush;
                }
                if (shape.Stroke is SolidColorBrush strokeBrush)
                {
                    propertyStrokeColorPreview.Fill = strokeBrush;
                }
                propertyStrokeThickness.Value = shape.StrokeThickness;

                // Enable basic controls
                propertyFillColorButton.IsEnabled = true;
                propertyStrokeColorButton.IsEnabled = true;
                propertyStrokeThickness.IsEnabled = true;

                // Rectangle-specific: Corner radius
                bool isRectangle = shape is Rectangle;
                labelRadiusX.Visibility = isRectangle ? Visibility.Visible : Visibility.Collapsed;
                propertyRadiusX.Visibility = isRectangle ? Visibility.Visible : Visibility.Collapsed;
                labelRadiusY.Visibility = isRectangle ? Visibility.Visible : Visibility.Collapsed;
                propertyRadiusY.Visibility = isRectangle ? Visibility.Visible : Visibility.Collapsed;

                if (shape is Rectangle rect)
                {
                    propertyRadiusX.Value = rect.RadiusX;
                    propertyRadiusX.IsEnabled = true;
                    propertyRadiusY.Value = rect.RadiusY;
                    propertyRadiusY.IsEnabled = true;
                }

                // Polygon-specific: Make Width/Height read-only (calculated from bounds)
                if (shape is Polygon polygon)
                {
                    // Calculate bounds from points
                    if (polygon.Points.Count > 0)
                    {
                        double minX = polygon.Points.Min(p => p.X);
                        double maxX = polygon.Points.Max(p => p.X);
                        double minY = polygon.Points.Min(p => p.Y);
                        double maxY = polygon.Points.Max(p => p.Y);

                        width = maxX - minX;
                        height = maxY - minY;

                        propertyWidth.Value = Math.Round(width * PIXELS_TO_MM, 2);
                        propertyHeight.Value = Math.Round(height * PIXELS_TO_MM, 2);
                    }

                    // Make Width/Height read-only for polygons (geometry defined by points)
                    propertyWidth.IsEnabled = false;
                    propertyHeight.IsEnabled = false;
                }

                // Stroke dash pattern
                propertyStrokeDashPattern.SelectedIndex = DetectDashPatternIndex(shape.StrokeDashArray);
                propertyStrokeDashPattern.IsEnabled = true;

                // Gradient fill
                bool hasGradient = shape.Fill is LinearGradientBrush;
                propertyUseGradientFill.IsChecked = hasGradient;
                propertyUseGradientFill.IsEnabled = true;

                if (hasGradient)
                {
                    var gradientBrush = (LinearGradientBrush)shape.Fill;
                    panelGradientControls.Visibility = Visibility.Visible;

                    if (gradientBrush.GradientStops.Count >= 2)
                    {
                        propertyGradientStartPreview.Fill = new SolidColorBrush(gradientBrush.GradientStops[0].Color);
                        propertyGradientEndPreview.Fill = new SolidColorBrush(gradientBrush.GradientStops[^1].Color);
                    }

                    double angle = CalculateGradientAngle(gradientBrush);
                    propertyGradientAngle.Value = angle;
                    propertyGradientAngleValue.Text = $"{angle:F0}°";

                    propertyGradientStartButton.IsEnabled = true;
                    propertyGradientEndButton.IsEnabled = true;
                    propertyGradientAngle.IsEnabled = true;
                }
                else
                {
                    panelGradientControls.Visibility = Visibility.Collapsed;
                }
            }
            else if (selectedElement is Image image)
            {
                groupTextFormatting.Visibility = Visibility.Collapsed;
                groupShapeStyling.Visibility = Visibility.Collapsed;
                groupImageFilters.Visibility = Visibility.Visible;

                // Enable and populate image filter controls
                if (image.Tag is Tuple<CanvasElement, BitmapSource> tuple)
                {
                    var canvasElement = tuple.Item1;
                    bool monochromeEnabled = canvasElement.MonochromeEnabled ?? false;

                    propertyMonochromeEnabled.IsChecked = monochromeEnabled;
                    panelMonochromeControls.Visibility = monochromeEnabled ? Visibility.Visible : Visibility.Collapsed;

                    // Algorithm selection
                    string algorithm = canvasElement.MonochromeAlgorithm ?? "Threshold";
                    propertyAlgorithm.SelectedIndex = algorithm switch
                    {
                        "FloydSteinberg" => 1,
                        "Ordered" => 2,
                        "Atkinson" => 3,
                        _ => 0
                    };

                    // Threshold
                    propertyThresholdSlider.Value = canvasElement.Threshold ?? 128;
                    propertyThresholdValue.Text = (canvasElement.Threshold ?? 128).ToString();

                    // Brightness
                    propertyBrightness.Value = canvasElement.Brightness ?? 0;
                    propertyBrightnessValue.Text = (canvasElement.Brightness ?? 0).ToString("F0");

                    // Contrast
                    propertyContrast.Value = canvasElement.Contrast ?? 0;
                    propertyContrastValue.Text = (canvasElement.Contrast ?? 0).ToString("F0");

                    // Invert
                    propertyInvert.IsChecked = canvasElement.InvertColors ?? false;
                }
                else
                {
                    propertyMonochromeEnabled.IsChecked = false;
                    panelMonochromeControls.Visibility = Visibility.Collapsed;
                    propertyAlgorithm.SelectedIndex = 0;
                    propertyThresholdSlider.Value = 128;
                    propertyThresholdValue.Text = "128";
                    propertyBrightness.Value = 0;
                    propertyBrightnessValue.Text = "0";
                    propertyContrast.Value = 0;
                    propertyContrastValue.Text = "0";
                    propertyInvert.IsChecked = false;
                }

                // Enable controls
                propertyMonochromeEnabled.IsEnabled = true;
                propertyAlgorithm.IsEnabled = true;
                propertyThresholdSlider.IsEnabled = true;
                propertyBrightness.IsEnabled = true;
                propertyContrast.IsEnabled = true;
                propertyInvert.IsEnabled = true;
            }
            else if (selectedElement is Canvas lineCanvas && lineCanvas.Tag is Tuple<Line, CanvasElement>)
            {
                // Line element wrapped in Canvas
                var lineData = (Tuple<Line, CanvasElement>)lineCanvas.Tag;
                var line = lineData.Item1;
                var canvasElement = lineData.Item2;

                groupTextFormatting.Visibility = Visibility.Collapsed;
                groupShapeStyling.Visibility = Visibility.Visible;
                groupImageFilters.Visibility = Visibility.Collapsed;
                groupArrowControls.Visibility = Visibility.Collapsed;

                // Get canvas position
                double canvasLeft = Canvas.GetLeft(lineCanvas);
                double canvasTop = Canvas.GetTop(lineCanvas);
                if (double.IsNaN(canvasLeft)) canvasLeft = 0.0;
                if (double.IsNaN(canvasTop)) canvasTop = 0.0;

                // Calculate absolute coordinates
                double x1 = canvasLeft + line.X1;
                double y1 = canvasTop + line.Y1;
                double x2 = canvasLeft + line.X2;
                double y2 = canvasTop + line.Y2;

                // Show X1, Y1 (as X, Y), X2, Y2
                propertyX.Value = Math.Round(x1 * PIXELS_TO_MM, 2);
                propertyY.Value = Math.Round(y1 * PIXELS_TO_MM, 2);

                // Show X2, Y2 controls
                labelX2.Visibility = Visibility.Visible;
                propertyX2.Visibility = Visibility.Visible;
                labelY2.Visibility = Visibility.Visible;
                propertyY2.Visibility = Visibility.Visible;

                propertyX2.Value = Math.Round(line.X2 * PIXELS_TO_MM, 2);
                propertyY2.Value = Math.Round(line.Y2 * PIXELS_TO_MM, 2);
                propertyX2.IsEnabled = true;
                propertyY2.IsEnabled = true;

                // Hide Width/Height (not applicable for Line)
                propertyWidth.IsEnabled = false;
                propertyHeight.IsEnabled = false;

                // Show stroke color and thickness
                if (line.Stroke is SolidColorBrush strokeBrush)
                {
                    propertyStrokeColorPreview.Fill = strokeBrush;
                }
                propertyStrokeThickness.Value = line.StrokeThickness;

                // Enable stroke controls
                propertyStrokeColorButton.IsEnabled = true;
                propertyStrokeThickness.IsEnabled = true;

                // Hide fill color and other shape-specific controls
                propertyFillColorButton.IsEnabled = false;
                propertyStrokeDashPattern.IsEnabled = false;
                propertyUseGradientFill.IsEnabled = false;
                labelRadiusX.Visibility = Visibility.Collapsed;
                propertyRadiusX.Visibility = Visibility.Collapsed;
                labelRadiusY.Visibility = Visibility.Collapsed;
                propertyRadiusY.Visibility = Visibility.Collapsed;
                panelGradientControls.Visibility = Visibility.Collapsed;
            }
            else if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement>)
            {
                groupTextFormatting.Visibility = Visibility.Collapsed;
                groupShapeStyling.Visibility = Visibility.Visible;
                groupImageFilters.Visibility = Visibility.Collapsed;
                groupArrowControls.Visibility = Visibility.Visible;

                var arrowData = (Tuple<Line, Polygon, Polygon, CanvasElement>)arrowCanvas.Tag;
                var arrowLine = arrowData.Item1;
                var canvasElement = arrowData.Item4;

                // Get arrow position from Canvas positioning
                double canvasLeft = Canvas.GetLeft(arrowCanvas);
                double canvasTop = Canvas.GetTop(arrowCanvas);
                if (double.IsNaN(canvasLeft)) canvasLeft = 0;
                if (double.IsNaN(canvasTop)) canvasTop = 0;

                // Calculate X1, Y1, X2, Y2
                double x1 = canvasLeft;
                double y1 = canvasTop;
                double x2 = canvasLeft + arrowLine.X2;
                double y2 = canvasTop + arrowLine.Y2;

                propertyX.Value = Math.Round(x1 * PIXELS_TO_MM, 2);
                propertyY.Value = Math.Round(y1 * PIXELS_TO_MM, 2);

                // Show X2, Y2 controls
                labelX2.Visibility = Visibility.Visible;
                propertyX2.Visibility = Visibility.Visible;
                labelY2.Visibility = Visibility.Visible;
                propertyY2.Visibility = Visibility.Visible;

                propertyX2.Value = Math.Round(x2 * PIXELS_TO_MM, 2);
                propertyY2.Value = Math.Round(y2 * PIXELS_TO_MM, 2);
                propertyX2.IsEnabled = true;
                propertyY2.IsEnabled = true;

                // Hide Width/Height
                propertyWidth.IsEnabled = false;
                propertyHeight.IsEnabled = false;

                // Show stroke color and thickness
                if (arrowLine.Stroke is SolidColorBrush strokeBrush)
                {
                    propertyStrokeColorPreview.Fill = strokeBrush;
                }
                propertyStrokeThickness.Value = arrowLine.StrokeThickness;

                // Enable stroke controls
                propertyStrokeColorButton.IsEnabled = true;
                propertyStrokeThickness.IsEnabled = true;

                // Arrow-specific controls
                propertyHasStartArrow.IsChecked = canvasElement.HasStartArrow ?? false;
                propertyHasEndArrow.IsChecked = canvasElement.HasEndArrow ?? true;
                propertyArrowheadSize.Value = canvasElement.ArrowheadSize ?? 10;
                propertyArrowheadSizeValue.Text = (canvasElement.ArrowheadSize ?? 10).ToString("F0");

                propertyHasStartArrow.IsEnabled = true;
                propertyHasEndArrow.IsEnabled = true;
                propertyArrowheadSize.IsEnabled = true;

                // Hide other shape controls
                propertyFillColorButton.IsEnabled = false;
                propertyStrokeDashPattern.IsEnabled = false;
                propertyUseGradientFill.IsEnabled = false;
                labelRadiusX.Visibility = Visibility.Collapsed;
                propertyRadiusX.Visibility = Visibility.Collapsed;
                labelRadiusY.Visibility = Visibility.Collapsed;
                propertyRadiusY.Visibility = Visibility.Collapsed;
                panelGradientControls.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Default case: hide optional controls
                labelX2.Visibility = Visibility.Collapsed;
                propertyX2.Visibility = Visibility.Collapsed;
                labelY2.Visibility = Visibility.Collapsed;
                propertyY2.Visibility = Visibility.Collapsed;
                groupArrowControls.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Element_Select(object sender, MouseButtonEventArgs e)
    {
        var element = sender as UIElement;
        if (element != null)
        {
            SelectElement(element);
            // Don't set e.Handled = true to allow drag handlers to also work
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only deselect if clicking directly on canvas (not on child element)
        if (e.Source == designCanvas)
        {
            SelectElement(null);
        }
    }

    private void ClearCanvas_Click(object sender, RoutedEventArgs e)
    {
        isDirty = true;
        // Clear all element controls before clearing canvas
        _elementControls.Clear();
        designCanvas.Children.Clear();
        commandManager.Clear();
        UpdateUndoRedoButtons();
    }

    // Zoom functionality
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (canvasScaleTransform == null || zoomPercentText == null)
            return;

        double zoomLevel = zoomSlider.Value;
        canvasScaleTransform.ScaleX = zoomLevel;
        canvasScaleTransform.ScaleY = zoomLevel;
        zoomPercentText.Text = $"{(zoomLevel * 100):F0}%";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        double newZoom = Math.Min(zoomSlider.Value + 0.1, zoomSlider.Maximum);
        zoomSlider.Value = newZoom;
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        double newZoom = Math.Max(zoomSlider.Value - 0.1, zoomSlider.Minimum);
        zoomSlider.Value = newZoom;
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        zoomSlider.Value = 1.0;
    }

    private void AddText_Click(object sender, RoutedEventArgs e)
    {
        var textBlock = CreateTextElement("Sample Text", 50, 50);
        commandManager.ExecuteCommand(new AddElementCommand(textBlock, designCanvas));

        // Create and register element control
        var canvasElement = new CanvasElement { ElementType = "Text", X = 50, Y = 50, Width = textBlock.Width, Height = textBlock.Height };
        var control = new TextElementControl(textBlock, canvasElement, this);
        RegisterElementControl(textBlock, control);

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddRectangle_Click(object sender, RoutedEventArgs e)
    {
        var rectangle = CreateRectangleElement(100, 100, 80, 60);
        commandManager.ExecuteCommand(new AddElementCommand(rectangle, designCanvas));

        // Create and register element control
        var canvasElement = new CanvasElement { ElementType = "Rectangle", X = 100, Y = 100, Width = 80, Height = 60 };
        var control = new ShapeControl(rectangle, canvasElement, ElementType.Rectangle, this);
        RegisterElementControl(rectangle, control);

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddEllipse_Click(object sender, RoutedEventArgs e)
    {
        var ellipse = CreateEllipseElement(150, 150, 80, 80);
        commandManager.ExecuteCommand(new AddElementCommand(ellipse, designCanvas));

        // Create and register element control
        var canvasElement = new CanvasElement { ElementType = "Ellipse", X = 150, Y = 150, Width = 80, Height = 80 };
        var control = new ShapeControl(ellipse, canvasElement, ElementType.Ellipse, this);
        RegisterElementControl(ellipse, control);

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        // Create horizontal line 100 units long
        var lineCanvas = CreateLineElement(100, 100, 200, 100);
        commandManager.ExecuteCommand(new AddElementCommand(lineCanvas, designCanvas));

        // Register element control (Line already has CanvasElement in Tag)
        if (lineCanvas is Canvas lc && lc.Tag is Tuple<Line, CanvasElement> lineData)
        {
            var control = new LineControl(lc, lineData.Item1, lineData.Item2, this);
            RegisterElementControl(lineCanvas, control);
        }

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddArrow_Click(object sender, RoutedEventArgs e)
    {
        // Create horizontal arrow 100 units long pointing right
        var arrowCanvas = CreateArrowElement(100, 150, 200, 150, hasStartArrow: false, hasEndArrow: true, arrowheadSize: 10);
        commandManager.ExecuteCommand(new AddElementCommand(arrowCanvas, designCanvas));

        // Register element control (Arrow already has CanvasElement in Tag)
        if (arrowCanvas is Canvas ac && ac.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement> arrowData)
        {
            var control = new ArrowControl(ac, arrowData.Item1, arrowData.Item2, arrowData.Item3, arrowData.Item4, this);
            RegisterElementControl(arrowCanvas, control);
        }

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddTriangle_Click(object sender, RoutedEventArgs e)
    {
        var points = CreateTrianglePoints(80);
        var triangle = CreatePolygonElement(points, 100, 100);
        commandManager.ExecuteCommand(new AddElementCommand(triangle, designCanvas));

        // Create and register element control
        var canvasElement = new CanvasElement { ElementType = "Polygon", X = 100, Y = 100, Width = 80, Height = 80 };
        var control = new ShapeControl(triangle, canvasElement, ElementType.Triangle, this);
        RegisterElementControl(triangle, control);

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddStar_Click(object sender, RoutedEventArgs e)
    {
        var points = CreateStarPoints(80);
        var star = CreatePolygonElement(points, 150, 150);
        commandManager.ExecuteCommand(new AddElementCommand(star, designCanvas));

        // Create and register element control
        var canvasElement = new CanvasElement { ElementType = "Polygon", X = 150, Y = 150, Width = 80, Height = 80 };
        var control = new ShapeControl(star, canvasElement, ElementType.Star, this);
        RegisterElementControl(star, control);

        UpdateUndoRedoButtons();
        isDirty = true;
    }

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Title = "Select an Image"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var image = CreateImageElement(dialog.FileName, 200, 200);
                commandManager.ExecuteCommand(new AddElementCommand(image, designCanvas));

                // Register element control (Image has CanvasElement and BitmapSource in Tag)
                if (image.Tag is Tuple<CanvasElement, BitmapSource> imageData)
                {
                    var control = new ImageControl(image, imageData.Item1, imageData.Item2, this);
                    RegisterElementControl(image, control);
                }

                UpdateUndoRedoButtons();
                isDirty = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private TextBlock CreateTextElement(string text, double x, double y)
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

        textBlock.MouseLeftButtonDown += Element_Select;
        textBlock.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        textBlock.MouseMove += Element_MouseMove;
        textBlock.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return textBlock;
    }

    private Rectangle CreateRectangleElement(double x, double y, double width, double height)
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

        rectangle.MouseLeftButtonDown += Element_Select;
        rectangle.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        rectangle.MouseMove += Element_MouseMove;
        rectangle.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return rectangle;
    }

    private Ellipse CreateEllipseElement(double x, double y, double width, double height)
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

        ellipse.MouseLeftButtonDown += Element_Select;
        ellipse.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        ellipse.MouseMove += Element_MouseMove;
        ellipse.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return ellipse;
    }

    private Image CreateImageElement(string imagePath, double x, double y)
    {
        var bitmap = new BitmapImage(new Uri(imagePath, UriKind.Absolute));

        // Calculate size that fits in canvas while preserving aspect ratio
        double imageWidth = bitmap.PixelWidth;
        double imageHeight = bitmap.PixelHeight;
        double aspectRatio = imageWidth / imageHeight;

        // Maximum size: 80% of canvas dimensions to ensure it fits nicely
        double maxWidth = designCanvas.ActualWidth * 0.8;
        double maxHeight = designCanvas.ActualHeight * 0.8;

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

        image.MouseLeftButtonDown += Element_Select;
        image.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        image.MouseMove += Element_MouseMove;
        image.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return image;
    }

    private UIElement CreateLineElement(double x1, double y1, double x2, double y2)
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
        lineCanvas.MouseLeftButtonDown += Element_Select;
        lineCanvas.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        lineCanvas.MouseMove += Element_MouseMove;
        lineCanvas.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return lineCanvas;
    }

    // Helper method to update line canvas position and size to keep line centered
    public void UpdateLineCanvas(Canvas lineCanvas, double x1, double y1, double x2, double y2)
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

    private UIElement CreateArrowElement(double x1, double y1, double x2, double y2, bool hasStartArrow = false, bool hasEndArrow = true, double arrowheadSize = 10)
    {
        // Create a Canvas to group the line and arrowheads together
        var arrowCanvas = new Canvas
        {
            Cursor = Cursors.Hand,
            Tag = "draggable"
        };

        // Create the main line
        var line = new Line
        {
            X1 = 0,
            Y1 = 0,
            X2 = x2 - x1,
            Y2 = y2 - y1,
            Stroke = Brushes.Black,
            StrokeThickness = 2
        };

        // Calculate arrow angle
        double dx = x2 - x1;
        double dy = y2 - y1;
        double angle = Math.Atan2(dy, dx);

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

        Polygon? startArrowhead = null;
        Polygon? endArrowhead = null;

        // Create start arrowhead if needed
        if (hasStartArrow)
        {
            startArrowhead = CreateArrowhead(0, 0, angle + Math.PI, arrowheadSize);
            arrowCanvas.Children.Add(startArrowhead);
        }

        // Create end arrowhead if needed
        if (hasEndArrow)
        {
            endArrowhead = CreateArrowhead(x2 - x1, y2 - y1, angle, arrowheadSize);
            arrowCanvas.Children.Add(endArrowhead);
        }

        arrowCanvas.Children.Add(line);

        // Position the canvas
        Canvas.SetLeft(arrowCanvas, x1);
        Canvas.SetTop(arrowCanvas, y1);

        // Calculate bounds for the arrow (needed for resizing)
        double minX = Math.Min(0, x2 - x1);
        double minY = Math.Min(0, y2 - y1);
        double maxX = Math.Max(0, x2 - x1);
        double maxY = Math.Max(0, y2 - y1);

        arrowCanvas.Width = maxX - minX + arrowheadSize * 2;
        arrowCanvas.Height = maxY - minY + arrowheadSize * 2;

        // Store references in Tag
        arrowCanvas.Tag = Tuple.Create(line, startArrowhead, endArrowhead, canvasElement);

        arrowCanvas.MouseLeftButtonDown += Element_Select;
        arrowCanvas.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        arrowCanvas.MouseMove += Element_MouseMove;
        arrowCanvas.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return arrowCanvas;
    }

    private Polygon CreateArrowhead(double x, double y, double angle, double size)
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

    private Point RotatePoint(Point point, Point center, double angle)
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

    private Polygon CreatePolygonElement(PointCollection points, double x, double y)
    {
        var polygon = new Polygon
        {
            Points = points,
            Fill = Brushes.Transparent,
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = "draggable"
        };

        // Position using Canvas.SetLeft/Top
        Canvas.SetLeft(polygon, x);
        Canvas.SetTop(polygon, y);

        polygon.MouseLeftButtonDown += Element_Select;
        polygon.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        polygon.MouseMove += Element_MouseMove;
        polygon.MouseLeftButtonUp += Element_MouseLeftButtonUp;

        return polygon;
    }

    private PointCollection CreateTrianglePoints(double size)
    {
        // Equilateral triangle centered in a square of given size
        // Top point, bottom-left, bottom-right
        var points = new PointCollection
        {
            new Point(size / 2, size * 0.125),      // Top center (raised slightly)
            new Point(size * 0.125, size * 0.875),  // Bottom left
            new Point(size * 0.875, size * 0.875)   // Bottom right
        };
        return points;
    }

    private PointCollection CreateStarPoints(double size, int pointCount = 5)
    {
        var points = new PointCollection();
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

            points.Add(new Point(x, y));
        }

        return points;
    }

    private void UpdateArrowEndpoint(Canvas arrowCanvas, double newX2, double newY2, bool isX2)
    {
        if (arrowCanvas.Tag is not Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
            return;

        var line = arrowData.Item1;
        var startArrowhead = arrowData.Item2;
        var endArrowhead = arrowData.Item3;
        var canvasElement = arrowData.Item4;

        double canvasLeft = Canvas.GetLeft(arrowCanvas);
        double canvasTop = Canvas.GetTop(arrowCanvas);
        if (double.IsNaN(canvasLeft)) canvasLeft = 0;
        if (double.IsNaN(canvasTop)) canvasTop = 0;

        // Calculate new relative coordinates
        double newRelX2 = newX2 - canvasLeft;
        double newRelY2 = newY2 - canvasTop;

        // Update line endpoints
        line.X2 = newRelX2;
        line.Y2 = newRelY2;

        // Update CanvasElement
        canvasElement.X2 = newX2;
        canvasElement.Y2 = newY2;

        // Recalculate arrowheads
        double angle = Math.Atan2(newRelY2, newRelX2);
        double arrowheadSize = canvasElement.ArrowheadSize ?? 10;

        // Remove old arrowheads
        if (startArrowhead != null)
            arrowCanvas.Children.Remove(startArrowhead);
        if (endArrowhead != null)
            arrowCanvas.Children.Remove(endArrowhead);

        // Create new arrowheads
        Polygon? newStartArrowhead = null;
        Polygon? newEndArrowhead = null;

        if (canvasElement.HasStartArrow ?? false)
        {
            newStartArrowhead = CreateArrowhead(0, 0, angle + Math.PI, arrowheadSize);
            arrowCanvas.Children.Insert(0, newStartArrowhead);
        }

        if (canvasElement.HasEndArrow ?? true)
        {
            newEndArrowhead = CreateArrowhead(newRelX2, newRelY2, angle, arrowheadSize);
            arrowCanvas.Children.Insert(0, newEndArrowhead);
        }

        // Update Tag with new arrowheads
        arrowCanvas.Tag = Tuple.Create(line, newStartArrowhead, newEndArrowhead, canvasElement);
    }

    private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        draggedElement = sender as UIElement;
        dragStartPoint = e.GetPosition(designCanvas);

        // Store initial position for undo/redo
        if (draggedElement != null)
        {
            if (draggedElement is Line line)
            {
                // For Line, store X1/Y1 and X2/Y2
                dragInitialPosition = new Point(line.X1, line.Y1);
                dragInitialEndPosition = new Point(line.X2, line.Y2);
            }
            else
            {
                double left = Canvas.GetLeft(draggedElement);
                double top = Canvas.GetTop(draggedElement);
                if (double.IsNaN(left)) left = 0.0;
                if (double.IsNaN(top)) top = 0.0;
                dragInitialPosition = new Point(left, top);
            }
        }

        isDragging = true;
        draggedElement?.CaptureMouse();
        e.Handled = true;
    }

    private void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isDragging)
            return;

        Point currentPos = e.GetPosition(designCanvas);
        Vector offset = currentPos - dragStartPoint;

        // Special handling for Line elements
        if (draggedElement is Line line)
        {
            // For lines, move both endpoints together
            double newX1 = dragInitialPosition.X + offset.X;
            double newY1 = dragInitialPosition.Y + offset.Y;
            double newX2 = dragInitialEndPosition.X + offset.X;
            double newY2 = dragInitialEndPosition.Y + offset.Y;

            // Apply snap to grid if enabled (snap X1/Y1)
            if (snapToGrid)
            {
                double x1Mm = newX1 * PIXELS_TO_MM;
                double y1Mm = newY1 * PIXELS_TO_MM;
                x1Mm = Math.Round(x1Mm / gridSize) * gridSize;
                y1Mm = Math.Round(y1Mm / gridSize) * gridSize;

                double snapDeltaX = (x1Mm * MM_TO_PIXELS) - newX1;
                double snapDeltaY = (y1Mm * MM_TO_PIXELS) - newY1;

                newX1 += snapDeltaX;
                newY1 += snapDeltaY;
                newX2 += snapDeltaX;
                newY2 += snapDeltaY;
            }

            // Move both endpoints by the offset
            line.X1 = newX1;
            line.Y1 = newY1;
            line.X2 = newX2;
            line.Y2 = newY2;

            // Update adorner position if selected
            if (selectedElement == line)
            {
                var layer = AdornerLayer.GetAdornerLayer(line);
                if (layer != null)
                {
                    var adorners = layer.GetAdorners(line);
                    if (adorners != null)
                    {
                        foreach (var adorner in adorners)
                        {
                            adorner.InvalidateArrange();
                            adorner.InvalidateVisual();
                        }
                    }
                }
            }

            UpdatePropertiesPanel();
            e.Handled = true;
            return;
        }

        // Calculate new position relative to initial position
        double newLeft = dragInitialPosition.X + offset.X;
        double newTop = dragInitialPosition.Y + offset.Y;

        // Get element dimensions
        double elementWidth = 0;
        double elementHeight = 0;
        if (draggedElement is FrameworkElement fe)
        {
            elementWidth = fe.ActualWidth;
            elementHeight = fe.ActualHeight;
        }

        // Minimum visible portion (pixels that must remain inside canvas)
        const double minVisible = 20;

        // Constrain position to keep at least minVisible pixels inside canvas
        // Left boundary: element can go left but at least minVisible must be visible
        if (newLeft < -(elementWidth - minVisible))
            newLeft = -(elementWidth - minVisible);

        // Right boundary: at least minVisible must be visible
        if (newLeft > designCanvas.ActualWidth - minVisible)
            newLeft = designCanvas.ActualWidth - minVisible;

        // Top boundary: at least minVisible must be visible
        if (newTop < -(elementHeight - minVisible))
            newTop = -(elementHeight - minVisible);

        // Bottom boundary: at least minVisible must be visible
        if (newTop > designCanvas.ActualHeight - minVisible)
            newTop = designCanvas.ActualHeight - minVisible;

        // Apply snap to grid if enabled (gridSize is in mm, positions in pixels)
        if (snapToGrid)
        {
            // Convert pixels to mm, round to grid, convert back to pixels
            double leftMm = newLeft * PIXELS_TO_MM;
            double topMm = newTop * PIXELS_TO_MM;
            leftMm = Math.Round(leftMm / gridSize) * gridSize;
            topMm = Math.Round(topMm / gridSize) * gridSize;
            newLeft = leftMm * MM_TO_PIXELS;
            newTop = topMm * MM_TO_PIXELS;
        }

        Canvas.SetLeft(draggedElement, newLeft);
        Canvas.SetTop(draggedElement, newTop);

        // Update properties panel in real-time during drag
        UpdatePropertiesPanel();

        e.Handled = true;
    }

    private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDragging)
            return;

        // Create MoveElementCommand if position changed
        if (draggedElement != null)
        {
            double finalLeft = Canvas.GetLeft(draggedElement);
            double finalTop = Canvas.GetTop(draggedElement);
            if (double.IsNaN(finalLeft)) finalLeft = 0.0;
            if (double.IsNaN(finalTop)) finalTop = 0.0;

            // Apply snap to grid if enabled (gridSize is in mm, positions in pixels)
            if (snapToGrid)
            {
                // Convert pixels to mm, round to grid, convert back to pixels
                double leftMm = finalLeft * PIXELS_TO_MM;
                double topMm = finalTop * PIXELS_TO_MM;
                leftMm = Math.Round(leftMm / gridSize) * gridSize;
                topMm = Math.Round(topMm / gridSize) * gridSize;
                finalLeft = leftMm * MM_TO_PIXELS;
                finalTop = topMm * MM_TO_PIXELS;
            }

            Point finalPosition = new Point(finalLeft, finalTop);

            // Only create command if position actually changed
            if (dragInitialPosition != finalPosition)
            {
                // Need to undo the current position first, then execute command
                Canvas.SetLeft(draggedElement, dragInitialPosition.X);
                Canvas.SetTop(draggedElement, dragInitialPosition.Y);
                commandManager.ExecuteCommand(new MoveElementCommand(draggedElement, dragInitialPosition, finalPosition));
                UpdateUndoRedoButtons();
                UpdatePropertiesPanel(); // Update properties panel after drag
                isDirty = true;
            }
        }

        draggedElement?.ReleaseMouseCapture();
        isDragging = false;
        draggedElement = null;
        e.Handled = true;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isDirty)
        {
            var result = MessageBox.Show("Save changes before closing?", "Unsaved Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                FileSave_Click(this, new RoutedEventArgs());
                // If still dirty after save attempt, user cancelled save dialog
                if (isDirty)
                    e.Cancel = true;
            }
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }

    private void FileNew_Click(object sender, RoutedEventArgs e)
    {
        if (isDirty)
        {
            var result = MessageBox.Show("Save changes before creating new label?", "Unsaved Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                FileSave_Click(sender, e);
                // If still dirty after save attempt, user cancelled save dialog
                if (isDirty)
                    return;
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        designCanvas.Children.Clear();
        SelectElement(null);
        currentFilePath = null;
        isDirty = false;
        commandManager.Clear();
        UpdateUndoRedoButtons();
    }

    private void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        if (isDirty)
        {
            var result = MessageBox.Show("Save changes before opening another file?", "Unsaved Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                FileSave_Click(sender, e);
                // If still dirty after save attempt, user cancelled save dialog
                if (isDirty)
                    return;
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Label Files|*.slbl|All Files|*.*",
            Title = "Open Label Design"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var doc = JsonSerializer.Deserialize<LabelDocument>(json);
                if (doc != null)
                {
                    DeserializeCanvas(doc);
                    currentFilePath = dialog.FileName;
                    isDirty = false;
                    commandManager.Clear();
                    UpdateUndoRedoButtons();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileSave_Click(object sender, RoutedEventArgs e)
    {
        if (currentFilePath == null)
        {
            FileSaveAs_Click(sender, e);
        }
        else
        {
            try
            {
                var doc = SerializeCanvas();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(doc, options);
                File.WriteAllText(currentFilePath, json);
                isDirty = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Label Files|*.slbl|All Files|*.*",
            DefaultExt = ".slbl",
            Title = "Save Label Design"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var doc = SerializeCanvas();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(doc, options);
                File.WriteAllText(dialog.FileName, json);
                currentFilePath = dialog.FileName;
                isDirty = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
            return;

        // Save original transform (includes zoom)
        Transform originalTransform = designCanvas.LayoutTransform;

        try
        {
            // Get printer capabilities
            PrintCapabilities capabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);

            // Calculate scale factor for accurate dimensions
            double scaleX = capabilities.PageImageableArea.ExtentWidth / designCanvas.ActualWidth;
            double scaleY = capabilities.PageImageableArea.ExtentHeight / designCanvas.ActualHeight;
            double scale = Math.Min(scaleX, scaleY); // Preserve aspect ratio

            // Apply print transform (temporarily replaces zoom transform)
            designCanvas.LayoutTransform = new ScaleTransform(scale, scale);

            // Arrange to printable area
            Size printSize = new Size(
                capabilities.PageImageableArea.ExtentWidth,
                capabilities.PageImageableArea.ExtentHeight
            );
            designCanvas.Measure(printSize);
            designCanvas.Arrange(new Rect(
                new Point(capabilities.PageImageableArea.OriginWidth,
                         capabilities.PageImageableArea.OriginHeight),
                printSize
            ));

            // Print
            printDialog.PrintVisual(designCanvas, "Label Design");
        }
        finally
        {
            // Restore original transform (including zoom)
            designCanvas.LayoutTransform = originalTransform;

            // Force layout update to restore on-screen appearance
            designCanvas.UpdateLayout();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle Ctrl+Z for Undo (PreviewKeyDown intercepts before TextBox handles it)
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (commandManager.CanUndo)
            {
                Undo_Click(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        // Handle Ctrl+Y for Redo
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (commandManager.CanRedo)
            {
                Redo_Click(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        // Handle Ctrl+N for New
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FileNew_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Ctrl+O for Open
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FileOpen_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Ctrl+S for Save
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FileSave_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Ctrl+P for Print
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Print_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Ctrl+D for Duplicate
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Duplicate_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Delete key - but only if focus is not in a text input control
        else if (e.Key == Key.Delete)
        {
            // Check if focus is in a TextBox or other text input control
            var focusedElement = Keyboard.FocusedElement as DependencyObject;
            if (focusedElement is TextBox || focusedElement is ComboBox)
            {
                // Let the text control handle the delete key
                return;
            }

            Delete_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Page Up for Bring to Front
        else if (e.Key == Key.PageUp)
        {
            BringToFront_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Page Down for Send to Back
        else if (e.Key == Key.PageDown)
        {
            SendToBack_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Ctrl+G for Snap to Grid toggle
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SnapToGrid_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Handle Arrow keys for precise movement - but only if focus is not in a text input control
        else if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
        {
            // Check if focus is in a TextBox or other text input control
            var focusedElement = Keyboard.FocusedElement as DependencyObject;
            if (focusedElement is TextBox || focusedElement is ComboBox)
            {
                // Let the text control handle the arrow keys
                return;
            }

            if (selectedElement != null)
            {
                // If snap to grid is enabled, use grid size as step (in mm, convert to pixels)
                // Otherwise use 1 pixel (or 10 with Shift)
                double step;
                if (snapToGrid)
                {
                    // gridSize is in mm, convert to pixels
                    double stepMm = Keyboard.Modifiers == ModifierKeys.Shift ? gridSize * 2 : gridSize;
                    step = stepMm * MM_TO_PIXELS;
                }
                else
                {
                    step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
                }

                double dx = 0, dy = 0;

                if (e.Key == Key.Left) dx = -step;
                else if (e.Key == Key.Right) dx = step;
                else if (e.Key == Key.Up) dy = -step;
                else if (e.Key == Key.Down) dy = step;

                MoveElementByOffset(selectedElement, dx, dy);
                e.Handled = true;
            }
        }
    }

    private LabelDocument SerializeCanvas()
    {
        var doc = new LabelDocument
        {
            CanvasWidth = designCanvas.ActualWidth,
            CanvasHeight = designCanvas.ActualHeight,
            Elements = new List<CanvasElement>()
        };

        foreach (UIElement child in designCanvas.Children)
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
                element.PolygonPoints = string.Join(" ", polygon.Points.Select(p => $"{p.X},{p.Y}"));
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
                element = lineData.Item2; // Get the stored CanvasElement
                // Update position from Canvas
                element.X = double.IsNaN(left) ? 0.0 : left;
                element.Y = double.IsNaN(top) ? 0.0 : top;
                // Update X2/Y2 from internal line coordinates
                var internalLine = lineData.Item1;
                element.X2 = element.X + internalLine.X2;
                element.Y2 = element.Y + internalLine.Y2;
                // Update stroke properties from internal line
                if (internalLine.Stroke is SolidColorBrush strokeBrush)
                    element.StrokeColor = strokeBrush.Color.ToString();
                element.StrokeThickness = internalLine.StrokeThickness;
            }
            else if (child is Canvas arrowCanvasWrapper && arrowCanvasWrapper.Tag is Tuple<Line, Polygon, Polygon, CanvasElement>)
            {
                // Arrow element (stored as Canvas with Line + Polygons)
                var arrowData = (Tuple<Line, Polygon, Polygon, CanvasElement>)arrowCanvasWrapper.Tag;
                element = arrowData.Item4; // Get the stored CanvasElement
                // Update position from Canvas
                element.X = double.IsNaN(left) ? 0.0 : left;
                element.Y = double.IsNaN(top) ? 0.0 : top;
            }
            else if (child is Image image)
            {
                // Get CanvasElement from Tag if it exists (Phase 9+)
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
                    // Legacy support for images without Tag (pre-Phase 9)
                    element.ElementType = "Image";
                    element.Width = image.Width;
                    element.Height = image.Height;
                    if (image.Source is BitmapImage bitmapImage)
                        element.ImagePath = bitmapImage.UriSource?.AbsolutePath;
                }
            }

            if (!string.IsNullOrEmpty(element.ElementType))
                doc.Elements.Add(element);
        }

        return doc;
    }

    private void DeserializeCanvas(LabelDocument doc)
    {
        // Clear element controls before clearing canvas
        _elementControls.Clear();
        designCanvas.Children.Clear();
        SelectElement(null);

        designCanvas.Width = doc.CanvasWidth;
        designCanvas.Height = doc.CanvasHeight;
        UpdateStatusBar();
        UpdateGridBackground();

        foreach (var element in doc.Elements)
        {
            UIElement? uiElement = null;

            switch (element.ElementType)
            {
                case "Text":
                    uiElement = CreateTextElement(
                        element.Text ?? "Text",
                        element.X,
                        element.Y
                    );
                    if (uiElement is TextBlock tb)
                    {
                        tb.Width = element.Width;
                        tb.Height = element.Height;
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
                    break;

                case "Rectangle":
                    uiElement = CreateRectangleElement(
                        element.X,
                        element.Y,
                        element.Width,
                        element.Height
                    );
                    if (uiElement is Rectangle rect)
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
                    break;

                case "Ellipse":
                    uiElement = CreateEllipseElement(
                        element.X,
                        element.Y,
                        element.Width,
                        element.Height
                    );
                    if (uiElement is Ellipse ellipse)
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
                    break;

                case "Polygon":
                    if (!string.IsNullOrEmpty(element.PolygonPoints))
                    {
                        // Parse points from "x1,y1 x2,y2 x3,y3..." format
                        var points = new PointCollection();
                        var pointPairs = element.PolygonPoints.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pair in pointPairs)
                        {
                            var coords = pair.Split(',');
                            if (coords.Length == 2 &&
                                double.TryParse(coords[0], out double x) &&
                                double.TryParse(coords[1], out double y))
                            {
                                points.Add(new Point(x, y));
                            }
                        }

                        uiElement = CreatePolygonElement(points, element.X, element.Y);
                        if (uiElement is Polygon poly)
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
                    }
                    break;

                case "Image":
                    if (!string.IsNullOrEmpty(element.ImagePath))
                    {
                        uiElement = CreateImageElement(
                            element.ImagePath,
                            element.X,
                            element.Y
                        );
                        if (uiElement is Image img)
                        {
                            img.Width = element.Width;
                            img.Height = element.Height;

                            // Restore filter settings if present
                            if (img.Tag is Tuple<CanvasElement, BitmapSource> tuple)
                            {
                                var canvasElement = tuple.Item1;
                                // Copy filter properties from deserialized element
                                canvasElement.MonochromeEnabled = element.MonochromeEnabled;
                                canvasElement.Threshold = element.Threshold;
                                canvasElement.MonochromeAlgorithm = element.MonochromeAlgorithm;
                                canvasElement.InvertColors = element.InvertColors;
                                canvasElement.Brightness = element.Brightness;
                                canvasElement.Contrast = element.Contrast;

                                // Apply filter if enabled
                                if (canvasElement.MonochromeEnabled == true)
                                {
                                    ApplyImageFilter(img);
                                }
                            }
                        }
                    }
                    break;

                case "Line":
                    uiElement = CreateLineElement(
                        element.X,
                        element.Y,
                        element.X2 ?? element.X + 100,
                        element.Y2 ?? element.Y
                    );
                    // Line is now wrapped in Canvas - extract internal line to apply properties
                    if (uiElement is Canvas lineCanvas && lineCanvas.Tag is Tuple<Line, CanvasElement>)
                    {
                        var lineData = (Tuple<Line, CanvasElement>)lineCanvas.Tag;
                        var ln = lineData.Item1;

                        if (!string.IsNullOrEmpty(element.StrokeColor))
                        {
                            var color = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
                            ln.Stroke = new SolidColorBrush(color);
                        }
                        if (element.StrokeThickness.HasValue)
                            ln.StrokeThickness = element.StrokeThickness.Value;
                    }
                    break;

                case "Arrow":
                    uiElement = CreateArrowElement(
                        element.X,
                        element.Y,
                        element.X2 ?? element.X + 100,
                        element.Y2 ?? element.Y,
                        element.HasStartArrow ?? false,
                        element.HasEndArrow ?? true,
                        element.ArrowheadSize ?? 10
                    );
                    if (uiElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
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
                    break;
            }

            if (uiElement != null)
            {
                designCanvas.Children.Add(uiElement);

                // Register element control for loaded elements
                RegisterElementControlForLoadedElement(uiElement, element);
            }
        }

        isDirty = false;
    }

    /// <summary>
    /// Registers the appropriate IElementControl for a loaded element during deserialization.
    /// </summary>
    private void RegisterElementControlForLoadedElement(UIElement uiElement, CanvasElement element)
    {
        IElementControl? control = null;

        switch (element.ElementType)
        {
            case "Text" when uiElement is TextBlock tb:
                var textCanvasElement = new CanvasElement
                {
                    ElementType = "Text",
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    Height = element.Height
                };
                control = new TextElementControl(tb, textCanvasElement, this);
                break;

            case "Rectangle" when uiElement is Rectangle rect:
                var rectCanvasElement = new CanvasElement
                {
                    ElementType = "Rectangle",
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    Height = element.Height
                };
                control = new ShapeControl(rect, rectCanvasElement, ElementType.Rectangle, this);
                break;

            case "Ellipse" when uiElement is Ellipse ellipse:
                var ellipseCanvasElement = new CanvasElement
                {
                    ElementType = "Ellipse",
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    Height = element.Height
                };
                control = new ShapeControl(ellipse, ellipseCanvasElement, ElementType.Ellipse, this);
                break;

            case "Polygon" when uiElement is Polygon polygon:
                var polygonCanvasElement = new CanvasElement
                {
                    ElementType = "Polygon",
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    Height = element.Height,
                    PolygonPoints = element.PolygonPoints
                };
                control = new ShapeControl(polygon, polygonCanvasElement, ElementType.Polygon, this);
                break;

            case "Image" when uiElement is Image img && img.Tag is Tuple<CanvasElement, BitmapSource> imageData:
                control = new ImageControl(img, imageData.Item1, imageData.Item2, this);
                break;

            case "Line" when uiElement is Canvas lineCanvas && lineCanvas.Tag is Tuple<Line, CanvasElement> lineData:
                control = new LineControl(lineCanvas, lineData.Item1, lineData.Item2, this);
                break;

            case "Arrow" when uiElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement> arrowData:
                control = new ArrowControl(arrowCanvas, arrowData.Item1, arrowData.Item2, arrowData.Item3, arrowData.Item4, this);
                break;
        }

        if (control != null)
        {
            RegisterElementControl(uiElement, control);
        }
    }

    // Canvas sizing methods
    private void UpdateStatusBar()
    {
        double widthMm = designCanvas.Width * 25.4 / 96;
        double heightMm = designCanvas.Height * 25.4 / 96;
        double widthIn = designCanvas.Width / 96;
        double heightIn = designCanvas.Height / 96;

        canvasSizeText.Text = $"Canvas: {widthMm:F1} x {heightMm:F1} mm ({widthIn:F2} x {heightIn:F2} in)";
    }

    private void SetCanvasSize(double widthUnits, double heightUnits)
    {
        // Mark as dirty if canvas had content
        if (designCanvas.Children.Count > 0)
            isDirty = true;

        designCanvas.Width = widthUnits;
        designCanvas.Height = heightUnits;
        UpdateStatusBar();
        UpdateGridBackground();
    }

    private void CanvasSize50x25mm_Click(object sender, RoutedEventArgs e)
    {
        // 50mm = 50 * 96 / 25.4 ≈ 189 units
        // 25mm = 25 * 96 / 25.4 ≈ 94 units
        SetCanvasSize(50 * 96 / 25.4, 25 * 96 / 25.4);
    }

    private void CanvasSize100x50mm_Click(object sender, RoutedEventArgs e)
    {
        // 100mm = 100 * 96 / 25.4 ≈ 378 units
        // 50mm = 50 * 96 / 25.4 ≈ 189 units
        SetCanvasSize(100 * 96 / 25.4, 50 * 96 / 25.4);
    }

    private void CanvasSize100x150mm_Click(object sender, RoutedEventArgs e)
    {
        // 100mm = 100 * 96 / 25.4 ≈ 378 units
        // 150mm = 150 * 96 / 25.4 ≈ 567 units
        SetCanvasSize(100 * 96 / 25.4, 150 * 96 / 25.4);
    }

    private void CanvasSize4x6in_Click(object sender, RoutedEventArgs e)
    {
        // 4 inches = 4 * 96 = 384 units
        // 6 inches = 6 * 96 = 576 units
        SetCanvasSize(4 * 96, 6 * 96);
    }

    private void CanvasSize3x5in_Click(object sender, RoutedEventArgs e)
    {
        // 3 inches = 3 * 96 = 288 units
        // 5 inches = 5 * 96 = 480 units
        SetCanvasSize(3 * 96, 5 * 96);
    }

    private void CanvasSize2x4in_Click(object sender, RoutedEventArgs e)
    {
        // 2 inches = 2 * 96 = 192 units
        // 4 inches = 4 * 96 = 384 units
        SetCanvasSize(2 * 96, 4 * 96);
    }

    private void CanvasSizeCustom_Click(object sender, RoutedEventArgs e)
    {
        // Create custom size dialog
        var dialog = new Window
        {
            Title = "Custom Canvas Size",
            Width = 350,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Margin = new Thickness(15);

        // Width input
        var widthLabel = new TextBlock { Text = "Width:", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(widthLabel, 0);
        var widthText = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(widthText, 0);
        widthText.Margin = new Thickness(60, 0, 0, 10);
        widthText.Width = 100;
        widthText.HorizontalAlignment = HorizontalAlignment.Left;

        // Height input
        var heightLabel = new TextBlock { Text = "Height:", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(heightLabel, 1);
        var heightText = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(heightText, 1);
        heightText.Margin = new Thickness(60, 0, 0, 10);
        heightText.Width = 100;
        heightText.HorizontalAlignment = HorizontalAlignment.Left;

        // Unit selection
        var unitLabel = new TextBlock { Text = "Units:", Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetRow(unitLabel, 2);
        var unitCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(unitCombo, 2);
        unitCombo.Margin = new Thickness(60, 0, 0, 10);
        unitCombo.Width = 150;
        unitCombo.HorizontalAlignment = HorizontalAlignment.Left;
        unitCombo.Items.Add("Millimeters");
        unitCombo.Items.Add("Inches");
        unitCombo.SelectedIndex = 0;

        // Buttons
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(buttonPanel, 4);

        var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };

        bool dialogResult = false;
        okButton.Click += (s, args) =>
        {
            // Validate inputs
            if (!double.TryParse(widthText.Text, out double width) || width <= 0)
            {
                MessageBox.Show("Please enter a valid positive width.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(heightText.Text, out double height) || height <= 0)
            {
                MessageBox.Show("Please enter a valid positive height.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isMillimeters = unitCombo.SelectedIndex == 0;

            // Validate ranges
            if (isMillimeters)
            {
                if (width < 10 || width > 500 || height < 10 || height > 500)
                {
                    MessageBox.Show("Dimensions must be between 10 and 500 mm.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else // Inches
            {
                if (width < 0.5 || width > 20 || height < 0.5 || height > 20)
                {
                    MessageBox.Show("Dimensions must be between 0.5 and 20 inches.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Convert to WPF units and set canvas size
            double widthUnits = isMillimeters ? width * 96 / 25.4 : width * 96;
            double heightUnits = isMillimeters ? height * 96 / 25.4 : height * 96;

            SetCanvasSize(widthUnits, heightUnits);
            dialogResult = true;
            dialog.Close();
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(widthLabel);
        grid.Children.Add(widthText);
        grid.Children.Add(heightLabel);
        grid.Children.Add(heightText);
        grid.Children.Add(unitLabel);
        grid.Children.Add(unitCombo);
        grid.Children.Add(buttonPanel);

        dialog.Content = grid;
        dialog.ShowDialog();
    }

    // Undo/Redo functionality
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        commandManager.Undo();
        UpdateUndoRedoButtons();
        UpdatePropertiesPanel();
        isDirty = true;
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        commandManager.Redo();
        UpdateUndoRedoButtons();
        UpdatePropertiesPanel();
        isDirty = true;
    }

    private void UpdateUndoRedoButtons()
    {
        undoMenuItem.IsEnabled = commandManager.CanUndo;
        redoMenuItem.IsEnabled = commandManager.CanRedo;
    }

    // Called by ResizeAdorner when resize operation completes
    public void ExecuteResizeCommand(FrameworkElement element, Size oldSize, Size newSize)
    {
        // Reset to old size first, then execute command to apply new size
        element.Width = oldSize.Width;
        element.Height = oldSize.Height;
        commandManager.ExecuteCommand(new ResizeElementCommand(element, oldSize, newSize));
        UpdateUndoRedoButtons();
        UpdatePropertiesPanel(); // Update properties panel after resize
        isDirty = true;
    }

    // Called by ResizeAdorner when Line resize operation completes
    public void ExecuteLineResizeCommand(Line line, Point oldStart, Point oldEnd, Point newStart, Point newEnd)
    {
        // Reset to old coordinates first, then execute command to apply new coordinates
        line.X1 = oldStart.X;
        line.Y1 = oldStart.Y;
        line.X2 = oldEnd.X;
        line.Y2 = oldEnd.Y;
        commandManager.ExecuteCommand(new ResizeLineCommand(line, oldStart, oldEnd, newStart, newEnd));
        UpdateUndoRedoButtons();
        UpdatePropertiesPanel();
        isDirty = true;
    }

    // Properties panel event handlers
    private void Property_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Store initial value for numeric properties
            if (double.TryParse(textBox.Text, out double value))
            {
                initialPropertyValue = value;
            }
            // Store initial string value for text properties
            initialStringValue = textBox.Text;
        }
        else if (sender is ComboBox comboBox)
        {
            // Store initial string value for combo boxes
            if (comboBox.SelectedItem != null)
            {
                if (comboBox.SelectedItem is ComboBoxItem item)
                {
                    string displayText = item.Content.ToString()!;
                    // For alignment combo box, store the enum value, not the display text
                    if (comboBox.Name == "propertyAlignment")
                    {
                        initialStringValue = displayText switch
                        {
                            "Links" => "Left",
                            "Mitte" => "Center",
                            "Rechts" => "Right",
                            _ => displayText
                        };
                    }
                    // For font size combo box, store as numeric value
                    else if (comboBox.Name == "propertyFontSize")
                    {
                        initialStringValue = displayText;
                        if (double.TryParse(displayText, out double fontSize))
                        {
                            initialPropertyValue = fontSize;
                        }
                    }
                    else
                    {
                        initialStringValue = displayText;
                    }
                }
                else
                {
                    initialStringValue = comboBox.SelectedItem.ToString();
                    // For editable combo boxes, also try to parse numeric value
                    if (comboBox.Name == "propertyFontSize" && double.TryParse(comboBox.Text, out double fontSize))
                    {
                        initialPropertyValue = fontSize;
                    }
                }
            }
            else
            {
                // SelectedItem is null, use the current Text value
                initialStringValue = comboBox.Text;
                // For font size combo box, also store as numeric value
                if (comboBox.Name == "propertyFontSize" && double.TryParse(comboBox.Text, out double fontSize))
                {
                    initialPropertyValue = fontSize;
                }
            }
        }
        else if (sender is Slider slider)
        {
            // Store initial value for slider
            initialPropertyValue = slider.Value;
        }
    }

    private void Property_ValueChanged(object sender, double newValue)
    {
        if (selectedElement == null) return;

        // Determine which property was changed and apply immediately
        if (sender == propertyX)
        {
            double oldValue = Canvas.GetLeft(selectedElement);
            if (double.IsNaN(oldValue)) oldValue = 0;
            if (Math.Abs(newValue - oldValue) > 0.01)
            {
                Canvas.SetLeft(selectedElement, oldValue);
                commandManager.ExecuteCommand(new ChangePropertyCommand(selectedElement, "X", oldValue, newValue));
                UpdateUndoRedoButtons();
                isDirty = true;
            }
        }
        else if (sender == propertyY)
        {
            double oldValue = Canvas.GetTop(selectedElement);
            if (double.IsNaN(oldValue)) oldValue = 0;
            if (Math.Abs(newValue - oldValue) > 0.01)
            {
                Canvas.SetTop(selectedElement, oldValue);
                commandManager.ExecuteCommand(new ChangePropertyCommand(selectedElement, "Y", oldValue, newValue));
                UpdateUndoRedoButtons();
                isDirty = true;
            }
        }
        else if (sender == propertyWidth)
        {
            if (selectedElement is FrameworkElement fe)
            {
                double oldValue = fe.Width;
                if (Math.Abs(newValue - oldValue) > 0.01)
                {
                    fe.Width = oldValue;
                    commandManager.ExecuteCommand(new ChangePropertyCommand(selectedElement, "Width", oldValue, newValue));
                    UpdateUndoRedoButtons();
                    isDirty = true;
                }
            }
        }
        else if (sender == propertyHeight)
        {
            if (selectedElement is FrameworkElement fe)
            {
                double oldValue = fe.Height;
                if (Math.Abs(newValue - oldValue) > 0.01)
                {
                    fe.Height = oldValue;
                    commandManager.ExecuteCommand(new ChangePropertyCommand(selectedElement, "Height", oldValue, newValue));
                    UpdateUndoRedoButtons();
                    isDirty = true;
                }
            }
        }
        else if (sender == propertyFontSize && selectedElement is TextBlock textBlock)
        {
            double oldValue = textBlock.FontSize;
            if (Math.Abs(newValue - oldValue) > 0.01)
            {
                textBlock.FontSize = oldValue;
                commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontSize", oldValue, newValue));
                UpdateUndoRedoButtons();
                isDirty = true;
            }
        }
        else if (sender == propertyStrokeThickness && selectedElement is Shape shape)
        {
            double oldValue = shape.StrokeThickness;
            if (Math.Abs(newValue - oldValue) > 0.01)
            {
                shape.StrokeThickness = oldValue;
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "StrokeThickness", oldValue, newValue));
                UpdateUndoRedoButtons();
                isDirty = true;
            }
        }
        else if (sender == propertyRadiusX && selectedElement is Rectangle rect)
        {
            double oldValue = rect.RadiusX;
            if (Math.Abs(newValue - oldValue) > 0.01)
            {
                rect.RadiusX = oldValue;
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect, "RadiusX", oldValue, newValue));
                UpdateUndoRedoButtons();
                isDirty = true;
            }
        }
        else if (sender == propertyRadiusY && selectedElement is Rectangle rect2)
        {
            double oldValue = rect2.RadiusY;
            if (Math.Abs(newValue - oldValue) > 0.01)
            {
                rect2.RadiusY = oldValue;
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect2, "RadiusY", oldValue, newValue));
                UpdateUndoRedoButtons();
                isDirty = true;
            }
        }
    }

    private void PropertyX_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;

        double newValueMm = propertyX.Value;

        if (Math.Abs(newValueMm - initialPropertyValue) > 0.01)
        {
            // Value changed - create command (convert mm to pixels)
            double oldValuePixels = initialPropertyValue * MM_TO_PIXELS;
            double newValuePixels = newValueMm * MM_TO_PIXELS;
            Canvas.SetLeft(selectedElement, oldValuePixels);
            commandManager.ExecuteCommand(new ChangePropertyCommand(selectedElement, "X", oldValuePixels, newValuePixels));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyY_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;

        double newValueMm = propertyY.Value;

        if (Math.Abs(newValueMm - initialPropertyValue) > 0.01)
        {
            // Value changed - create command (convert mm to pixels)
            double oldValuePixels = initialPropertyValue * MM_TO_PIXELS;
            double newValuePixels = newValueMm * MM_TO_PIXELS;
            Canvas.SetTop(selectedElement, oldValuePixels);
            commandManager.ExecuteCommand(new ChangePropertyCommand(selectedElement, "Y", oldValuePixels, newValuePixels));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyWidth_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not FrameworkElement element) return;

        double newValueMm = propertyWidth.Value;

        if (Math.Abs(newValueMm - initialPropertyValue) > 0.01)
        {
            // Value changed - create command (convert mm to pixels)
            double oldValuePixels = initialPropertyValue * MM_TO_PIXELS;
            double newValuePixels = newValueMm * MM_TO_PIXELS;
            element.Width = oldValuePixels;
            commandManager.ExecuteCommand(new ChangePropertyCommand(element, "Width", oldValuePixels, newValuePixels));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyHeight_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not FrameworkElement element) return;

        double newValueMm = propertyHeight.Value;

        if (Math.Abs(newValueMm - initialPropertyValue) > 0.01)
        {
            // Value changed - create command (convert mm to pixels)
            double oldValuePixels = initialPropertyValue * MM_TO_PIXELS;
            double newValuePixels = newValueMm * MM_TO_PIXELS;
            element.Height = oldValuePixels;
            commandManager.ExecuteCommand(new ChangePropertyCommand(element, "Height", oldValuePixels, newValuePixels));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyX2_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;

        double newValueMm = propertyX2.Value;

        if (Math.Abs(newValueMm - initialPropertyValue) > 0.01)
        {
            // Value changed - create command (convert mm to pixels)
            double oldValuePixels = initialPropertyValue * MM_TO_PIXELS;
            double newValuePixels = newValueMm * MM_TO_PIXELS;

            if (selectedElement is Line line)
            {
                line.X2 = oldValuePixels;
                commandManager.ExecuteCommand(new ChangePropertyCommand(line, "X2", oldValuePixels, newValuePixels));
            }
            else if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement>)
            {
                // For arrows, update the line's X2 and recalculate arrowheads
                var arrowData = (Tuple<Line, Polygon, Polygon, CanvasElement>)arrowCanvas.Tag;
                UpdateArrowEndpoint(arrowCanvas, newValuePixels, propertyY2.Value * MM_TO_PIXELS, isX2: true);
            }

            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyY2_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;

        double newValueMm = propertyY2.Value;

        if (Math.Abs(newValueMm - initialPropertyValue) > 0.01)
        {
            // Value changed - create command (convert mm to pixels)
            double oldValuePixels = initialPropertyValue * MM_TO_PIXELS;
            double newValuePixels = newValueMm * MM_TO_PIXELS;

            if (selectedElement is Line line)
            {
                line.Y2 = oldValuePixels;
                commandManager.ExecuteCommand(new ChangePropertyCommand(line, "Y2", oldValuePixels, newValuePixels));
            }
            else if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement>)
            {
                // For arrows, update the line's Y2 and recalculate arrowheads
                var arrowData = (Tuple<Line, Polygon, Polygon, CanvasElement>)arrowCanvas.Tag;
                UpdateArrowEndpoint(arrowCanvas, propertyX2.Value * MM_TO_PIXELS, newValuePixels, isX2: false);
            }

            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    // Text formatting event handlers
    private void PropertyText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not TextBox textBox)
            return;

        string newValue = textBox.Text;
        string oldValue = initialStringValue ?? textBlock.Text;

        if (newValue != oldValue)
        {
            // Value changed - create command
            commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "Text", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            // Trigger the LostFocus logic by moving focus away
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void PropertyFontFamily_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not ComboBox comboBox)
            return;

        if (comboBox.SelectedItem == null)
            return;

        string newValue = comboBox.SelectedItem.ToString()!;
        string oldValue = initialStringValue ?? textBlock.FontFamily.Source;

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.FontFamily = new FontFamily(oldValue);
            commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontFamily", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyFontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not ComboBox comboBox)
            return;

        // Get the font size from the selected item's Tag
        if (comboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            if (double.TryParse(item.Tag.ToString(), out double newValue))
            {
                double oldValue = textBlock.FontSize;

                if (Math.Abs(newValue - oldValue) > 0.01)
                {
                    // Value changed - create command
                    textBlock.FontSize = oldValue;
                    commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontSize", oldValue, newValue));
                    UpdateUndoRedoButtons();
                    UpdatePropertiesPanel();
                    isDirty = true;
                }
            }
        }
    }

    private void PropertyFontSize_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not ComboBox comboBox)
            return;

        // Handle manual text input (when user types directly)
        string text = comboBox.Text;

        // Try to parse as direct pt value
        if (!double.TryParse(text, out double newValue))
            return;

        // If initialPropertyValue is invalid (0 or negative), use the current FontSize
        double oldValue = initialPropertyValue > 0 ? initialPropertyValue : textBlock.FontSize;

        if (Math.Abs(newValue - oldValue) > 0.01)
        {
            // Value changed - create command
            textBlock.FontSize = oldValue;
            commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontSize", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyColor_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock) return;

        var currentBrush = textBlock.Foreground as SolidColorBrush;
        WpfColor currentColor = currentBrush?.Color ?? Colors.Black;

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = FormsColor.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
            FullOpen = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            string newValue = newColor.ToString();
            string oldValue = currentColor.ToString();

            if (newValue != oldValue)
            {
                commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "Foreground", oldValue, newValue));
                UpdateUndoRedoButtons();
                UpdatePropertiesPanel();
                isDirty = true;
            }
        }
    }

    private void PropertyAlignment_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not ComboBox comboBox)
            return;

        if (comboBox.SelectedItem == null)
            return;

        // Map from displayed text to enum value
        string displayText = ((ComboBoxItem)comboBox.SelectedItem).Content.ToString()!;
        string newValue = displayText switch
        {
            "Links" => "Left",
            "Mitte" => "Center",
            "Rechts" => "Right",
            _ => "Left"
        };

        string oldValue = initialStringValue ?? textBlock.TextAlignment.ToString();

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.TextAlignment = Enum.Parse<TextAlignment>(oldValue);
            commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "TextAlignment", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyBold_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not CheckBox checkBox)
            return;

        bool newChecked = checkBox.IsChecked == true;
        string newValue = newChecked ? "Bold" : "Normal";
        string oldValue = (textBlock.FontWeight == FontWeights.Bold) ? "Bold" : "Normal";

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.FontWeight = oldValue == "Bold" ? FontWeights.Bold : FontWeights.Normal;
            commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontWeight", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyItalic_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not TextBlock textBlock || sender is not CheckBox checkBox)
            return;

        bool newChecked = checkBox.IsChecked == true;
        string newValue = newChecked ? "Italic" : "Normal";
        string oldValue = (textBlock.FontStyle == FontStyles.Italic) ? "Italic" : "Normal";

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.FontStyle = oldValue == "Italic" ? FontStyles.Italic : FontStyles.Normal;
            commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontStyle", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    // Shape styling event handlers
    private void PropertyFillColor_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Shape shape) return;

        var currentBrush = shape.Fill as SolidColorBrush;
        WpfColor currentColor = currentBrush?.Color ?? Colors.White;

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = FormsColor.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
            FullOpen = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            string newValue = newColor.ToString();
            string oldValue = currentColor.ToString();

            if (newValue != oldValue)
            {
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "Fill", oldValue, newValue));
                UpdateUndoRedoButtons();
                UpdatePropertiesPanel();
                isDirty = true;
            }
        }
    }

    private void PropertyStrokeColor_Click(object sender, RoutedEventArgs e)
    {
        // Handle Arrow elements (Canvas containing Line)
        if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
        {
            var line = arrowData.Item1;
            var currentBrush = line.Stroke as SolidColorBrush;
            WpfColor currentColor = currentBrush?.Color ?? Colors.Black;

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = FormsColor.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
                FullOpen = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                var newBrush = new SolidColorBrush(newColor);

                // Update line and arrowheads
                line.Stroke = newBrush;
                if (arrowData.Item2 != null) // start arrow
                {
                    arrowData.Item2.Fill = newBrush;
                    arrowData.Item2.Stroke = newBrush;
                }
                if (arrowData.Item3 != null) // end arrow
                {
                    arrowData.Item3.Fill = newBrush;
                    arrowData.Item3.Stroke = newBrush;
                }

                UpdatePropertiesPanel();
                isDirty = true;
            }
            return;
        }

        // Handle Line elements
        if (selectedElement is Line lineElement)
        {
            var currentBrush = lineElement.Stroke as SolidColorBrush;
            WpfColor currentColor = currentBrush?.Color ?? Colors.Black;

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = FormsColor.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
                FullOpen = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                var newBrush = new SolidColorBrush(newColor);
                lineElement.Stroke = newBrush;
                UpdatePropertiesPanel();
                isDirty = true;
            }
            return;
        }

        // Handle regular Shape elements
        if (selectedElement is not Shape shape) return;

        var shapeCurrentBrush = shape.Stroke as SolidColorBrush;
        WpfColor shapeCurrentColor = shapeCurrentBrush?.Color ?? Colors.Black;

        using var shapeDialog = new System.Windows.Forms.ColorDialog
        {
            Color = FormsColor.FromArgb(shapeCurrentColor.A, shapeCurrentColor.R, shapeCurrentColor.G, shapeCurrentColor.B),
            FullOpen = true
        };

        if (shapeDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var newColor = WpfColor.FromArgb(shapeDialog.Color.A, shapeDialog.Color.R, shapeDialog.Color.G, shapeDialog.Color.B);
            string newValue = newColor.ToString();
            string oldValue = shapeCurrentColor.ToString();

            if (newValue != oldValue)
            {
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "Stroke", oldValue, newValue));
                UpdateUndoRedoButtons();
                UpdatePropertiesPanel();
                isDirty = true;
            }
        }
    }

    private void PropertyStrokeThickness_LostFocus(object sender, RoutedEventArgs e)
    {
        double newValue = propertyStrokeThickness.Value;

        // Handle Arrow elements (Canvas containing Line)
        if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement>  arrowData)
        {
            var line = arrowData.Item1;
            if (Math.Abs(newValue - initialPropertyValue) > 0.01)
            {
                line.StrokeThickness = newValue;
                UpdatePropertiesPanel();
                isDirty = true;
            }
            return;
        }

        // Handle Line elements
        if (selectedElement is Line lineElement)
        {
            if (Math.Abs(newValue - initialPropertyValue) > 0.01)
            {
                lineElement.StrokeThickness = newValue;
                UpdatePropertiesPanel();
                isDirty = true;
            }
            return;
        }

        // Handle regular Shape elements
        if (selectedElement is not Shape shape) return;

        if (Math.Abs(newValue - initialPropertyValue) > 0.01)
        {
            // Value changed - create command
            shape.StrokeThickness = initialPropertyValue;
            commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "StrokeThickness", initialPropertyValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    // Extended shape property event handlers
    private void PropertyRadiusX_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Rectangle rect) return;

        double newValue = propertyRadiusX.Value;

        if (Math.Abs(newValue - initialPropertyValue) > 0.01)
        {
            // Value changed - create command
            rect.RadiusX = initialPropertyValue;
            commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect, "RadiusX", initialPropertyValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyRadiusY_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Rectangle rect) return;

        double newValue = propertyRadiusY.Value;

        if (Math.Abs(newValue - initialPropertyValue) > 0.01)
        {
            // Value changed - create command
            rect.RadiusY = initialPropertyValue;
            commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect, "RadiusY", initialPropertyValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyStrokeDashPattern_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (selectedElement is not Shape shape || propertyStrokeDashPattern.SelectedItem is not ComboBoxItem item)
            return;

        string newPattern = item.Tag?.ToString() ?? "Solid";
        string oldPattern = DetectDashPattern(shape.StrokeDashArray);

        if (oldPattern != newPattern)
        {
            commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "StrokeDashPattern", oldPattern, newPattern));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyUseGradientFill_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Shape shape) return;

        bool newValue = propertyUseGradientFill.IsChecked == true;
        bool oldValue = shape.Fill is LinearGradientBrush;

        if (oldValue != newValue)
        {
            commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "UseGradientFill", oldValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void PropertyGradientStart_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Shape shape || shape.Fill is not LinearGradientBrush gradientBrush)
            return;
        if (gradientBrush.GradientStops.Count < 1)
            return;

        WpfColor currentColor = gradientBrush.GradientStops[0].Color;

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = FormsColor.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
            FullOpen = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            string newValue = newColor.ToString();
            string oldValue = currentColor.ToString();

            if (newValue != oldValue)
            {
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "GradientStartColor", oldValue, newValue));
                UpdateUndoRedoButtons();
                UpdatePropertiesPanel();
                isDirty = true;
            }
        }
    }

    private void PropertyGradientEnd_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Shape shape || shape.Fill is not LinearGradientBrush gradientBrush)
            return;
        if (gradientBrush.GradientStops.Count < 2)
            return;

        WpfColor currentColor = gradientBrush.GradientStops[^1].Color;

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = FormsColor.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
            FullOpen = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var newColor = WpfColor.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            string newValue = newColor.ToString();
            string oldValue = currentColor.ToString();

            if (newValue != oldValue)
            {
                commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "GradientEndColor", oldValue, newValue));
                UpdateUndoRedoButtons();
                UpdatePropertiesPanel();
                isDirty = true;
            }
        }
    }

    private void PropertyGradientAngle_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Shape shape) return;

        double newValue = propertyGradientAngle.Value;

        if (Math.Abs(newValue - initialPropertyValue) > 0.01)
        {
            commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "GradientAngle", initialPropertyValue, newValue));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    // Helper methods for extended shape properties
    private string DetectDashPattern(DoubleCollection? dashArray)
    {
        if (dashArray == null || dashArray.Count == 0) return "Solid";

        // Compare with known patterns
        if (IsArrayEqual(dashArray, new[] { 2.0, 2.0 })) return "Dash";
        if (IsArrayEqual(dashArray, new[] { 1.0, 2.0 })) return "Dot";
        if (IsArrayEqual(dashArray, new[] { 2.0, 2.0, 1.0, 2.0 })) return "DashDot";

        return "Solid";
    }

    private int DetectDashPatternIndex(DoubleCollection? dashArray)
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

    private bool IsArrayEqual(DoubleCollection array, double[] pattern)
    {
        if (array.Count != pattern.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (Math.Abs(array[i] - pattern[i]) > 0.01) return false;
        }
        return true;
    }

    private void ApplyDashPattern(Shape shape, string pattern)
    {
        shape.StrokeDashArray = pattern switch
        {
            "Dash" => new DoubleCollection { 2, 2 },
            "Dot" => new DoubleCollection { 1, 2 },
            "DashDot" => new DoubleCollection { 2, 2, 1, 2 },
            _ => null
        };
    }

    private double CalculateGradientAngle(LinearGradientBrush brush)
    {
        // Calculate angle from StartPoint/EndPoint
        double dx = brush.EndPoint.X - brush.StartPoint.X;
        double dy = brush.EndPoint.Y - brush.StartPoint.Y;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        return (angle + 360) % 360;
    }

    private LinearGradientBrush CreateGradientBrush(WpfColor startColor, WpfColor endColor, double angle)
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

    // Image filter event handlers
    private void PropertyMonochromeEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Image imageElement || sender is not CheckBox checkBox)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        bool oldValue = canvasElement.MonochromeEnabled ?? false;
        bool newValue = checkBox.IsChecked ?? false;

        if (newValue != oldValue)
        {
            // Show/hide monochrome controls
            panelMonochromeControls.Visibility = newValue ? Visibility.Visible : Visibility.Collapsed;

            // Value changed - create command
            commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "MonochromeEnabled", oldValue, newValue, ApplyImageFilter));
            UpdateUndoRedoButtons();
            isDirty = true;
        }
    }

    private void PropertyThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (selectedElement is not Image imageElement || sender is not Slider slider)
            return;

        // Update the value display
        propertyThresholdValue.Text = ((int)slider.Value).ToString();

        // If monochrome is enabled, apply filter immediately for real-time preview
        if (imageElement.Tag is Tuple<CanvasElement, BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;
            if (canvasElement.MonochromeEnabled == true)
            {
                // Update the threshold value (without creating undo command yet)
                canvasElement.Threshold = (byte)slider.Value;
                ApplyImageFilter(imageElement);
            }
        }
    }

    private void PropertyThreshold_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Image imageElement || sender is not Slider slider)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        byte newValue = (byte)slider.Value;
        byte oldValue = (byte)initialPropertyValue;

        if (newValue != oldValue)
        {
            // Value changed - create command for undo/redo
            canvasElement.Threshold = oldValue; // Reset to old value
            commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Threshold", oldValue, newValue, ApplyImageFilter));
            UpdateUndoRedoButtons();
            isDirty = true;
        }
    }

    private void PropertyAlgorithm_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (selectedElement is not Image imageElement || sender is not ComboBox comboBox)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        if (comboBox.SelectedItem is not ComboBoxItem item)
            return;

        var canvasElement = tuple.Item1;
        string newValue = item.Tag?.ToString() ?? "Threshold";
        string oldValue = canvasElement.MonochromeAlgorithm ?? "Threshold";

        if (newValue != oldValue)
        {
            commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Algorithm", oldValue, newValue, ApplyImageFilter));
            UpdateUndoRedoButtons();
            isDirty = true;
        }
    }

    private void PropertyBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (selectedElement is not Image imageElement || sender is not Slider slider)
            return;

        // Update the value display
        propertyBrightnessValue.Text = ((int)slider.Value).ToString();

        // If monochrome is enabled, apply filter immediately for real-time preview
        if (imageElement.Tag is Tuple<CanvasElement, BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;
            if (canvasElement.MonochromeEnabled == true)
            {
                canvasElement.Brightness = slider.Value;
                ApplyImageFilter(imageElement);
            }
        }
    }

    private void PropertyBrightness_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Image imageElement || sender is not Slider slider)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        double newValue = slider.Value;
        double oldValue = initialPropertyValue;

        if (Math.Abs(newValue - oldValue) > 0.01)
        {
            canvasElement.Brightness = oldValue;
            commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Brightness", oldValue, newValue, ApplyImageFilter));
            UpdateUndoRedoButtons();
            isDirty = true;
        }
    }

    private void PropertyContrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (selectedElement is not Image imageElement || sender is not Slider slider)
            return;

        // Update the value display
        propertyContrastValue.Text = ((int)slider.Value).ToString();

        // If monochrome is enabled, apply filter immediately for real-time preview
        if (imageElement.Tag is Tuple<CanvasElement, BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;
            if (canvasElement.MonochromeEnabled == true)
            {
                canvasElement.Contrast = slider.Value;
                ApplyImageFilter(imageElement);
            }
        }
    }

    private void PropertyContrast_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Image imageElement || sender is not Slider slider)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        double newValue = slider.Value;
        double oldValue = initialPropertyValue;

        if (Math.Abs(newValue - oldValue) > 0.01)
        {
            canvasElement.Contrast = oldValue;
            commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Contrast", oldValue, newValue, ApplyImageFilter));
            UpdateUndoRedoButtons();
            isDirty = true;
        }
    }

    private void PropertyInvert_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Image imageElement || sender is not CheckBox checkBox)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        bool oldValue = canvasElement.InvertColors ?? false;
        bool newValue = checkBox.IsChecked ?? false;

        if (newValue != oldValue)
        {
            commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Invert", oldValue, newValue, ApplyImageFilter));
            UpdateUndoRedoButtons();
            isDirty = true;
        }
    }

    // Helper method to apply image filter
    internal void ApplyImageFilter(Image imageElement)
    {
        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        var originalSource = tuple.Item2;

        if (canvasElement.MonochromeEnabled == true)
        {
            // Apply monochrome filter with all parameters
            string algorithm = canvasElement.MonochromeAlgorithm ?? "Threshold";
            byte threshold = canvasElement.Threshold ?? 128;
            bool invert = canvasElement.InvertColors ?? false;
            double brightness = canvasElement.Brightness ?? 0;
            double contrast = canvasElement.Contrast ?? 0;

            var filteredImage = Utilities.ImageProcessing.ProcessImage(
                originalSource,
                algorithm,
                threshold,
                invert,
                brightness,
                contrast
            );
            imageElement.Source = filteredImage;
        }
        else
        {
            // Restore original image
            imageElement.Source = originalSource;
        }
    }

    // Arrow property event handlers
    private void PropertyHasStartArrow_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Canvas arrowCanvas || sender is not CheckBox checkBox)
            return;

        if (arrowCanvas.Tag is not Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
            return;

        var canvasElement = arrowData.Item4;
        bool newValue = checkBox.IsChecked ?? false;
        canvasElement.HasStartArrow = newValue;

        // Recreate arrow with updated arrowheads
        RecreateArrowArrowheads(arrowCanvas);
        isDirty = true;
    }

    private void PropertyHasEndArrow_Changed(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Canvas arrowCanvas || sender is not CheckBox checkBox)
            return;

        if (arrowCanvas.Tag is not Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
            return;

        var canvasElement = arrowData.Item4;
        bool newValue = checkBox.IsChecked ?? false;
        canvasElement.HasEndArrow = newValue;

        // Recreate arrow with updated arrowheads
        RecreateArrowArrowheads(arrowCanvas);
        isDirty = true;
    }

    private void PropertyArrowheadSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (selectedElement is not Canvas arrowCanvas)
            return;

        if (arrowCanvas.Tag is not Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
            return;

        propertyArrowheadSizeValue.Text = e.NewValue.ToString("F0");
    }

    private void PropertyArrowheadSize_LostFocus(object sender, RoutedEventArgs e)
    {
        if (selectedElement is not Canvas arrowCanvas)
            return;

        if (arrowCanvas.Tag is not Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
            return;

        var canvasElement = arrowData.Item4;
        double newValue = propertyArrowheadSize.Value;

        if (Math.Abs(newValue - initialPropertyValue) > 0.01)
        {
            canvasElement.ArrowheadSize = newValue;
            RecreateArrowArrowheads(arrowCanvas);
            isDirty = true;
        }
    }

    public void RecreateArrowArrowheads(Canvas arrowCanvas)
    {
        if (arrowCanvas.Tag is not Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
            return;

        var line = arrowData.Item1;
        var oldStartArrowhead = arrowData.Item2;
        var oldEndArrowhead = arrowData.Item3;
        var canvasElement = arrowData.Item4;

        // Remove old arrowheads
        if (oldStartArrowhead != null)
            arrowCanvas.Children.Remove(oldStartArrowhead);
        if (oldEndArrowhead != null)
            arrowCanvas.Children.Remove(oldEndArrowhead);

        // Calculate angle
        double angle = Math.Atan2(line.Y2, line.X2);
        double arrowheadSize = canvasElement.ArrowheadSize ?? 10;

        // Create new arrowheads
        Polygon? newStartArrowhead = null;
        Polygon? newEndArrowhead = null;

        if (canvasElement.HasStartArrow ?? false)
        {
            newStartArrowhead = CreateArrowhead(0, 0, angle + Math.PI, arrowheadSize);
            arrowCanvas.Children.Insert(0, newStartArrowhead);
        }

        if (canvasElement.HasEndArrow ?? true)
        {
            newEndArrowhead = CreateArrowhead(line.X2, line.Y2, angle, arrowheadSize);
            arrowCanvas.Children.Insert(0, newEndArrowhead);
        }

        // Update Tag
        arrowCanvas.Tag = Tuple.Create(line, newStartArrowhead, newEndArrowhead, canvasElement);
    }

    // ============================================================================
    // QoL Improvements: Arrange, Align, Z-Order, Grid, Duplicate, Delete
    // ============================================================================

    // Duplicate selected element
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null)
            return;

        UIElement? newElement = null;
        double offsetX = 20; // Offset for visual feedback
        double offsetY = 20;

        if (selectedElement is TextBlock textBlock)
        {
            newElement = CreateTextElement(
                textBlock.Text,
                Canvas.GetLeft(textBlock) + offsetX,
                Canvas.GetTop(textBlock) + offsetY
            );
            if (newElement is TextBlock newText)
            {
                newText.Width = textBlock.Width;
                newText.Height = textBlock.Height;
                newText.FontSize = textBlock.FontSize;
                newText.FontFamily = textBlock.FontFamily;
                newText.Foreground = textBlock.Foreground;
                newText.TextAlignment = textBlock.TextAlignment;
                newText.FontWeight = textBlock.FontWeight;
                newText.FontStyle = textBlock.FontStyle;
            }
        }
        else if (selectedElement is Rectangle rect)
        {
            newElement = CreateRectangleElement(
                Canvas.GetLeft(rect) + offsetX,
                Canvas.GetTop(rect) + offsetY,
                rect.Width,
                rect.Height
            );
            if (newElement is Rectangle newRect)
            {
                newRect.Width = rect.Width;
                newRect.Height = rect.Height;
                newRect.Fill = rect.Fill;
                newRect.Stroke = rect.Stroke;
                newRect.StrokeThickness = rect.StrokeThickness;
                newRect.RadiusX = rect.RadiusX;
                newRect.RadiusY = rect.RadiusY;
                newRect.StrokeDashArray = rect.StrokeDashArray;
            }
        }
        else if (selectedElement is Ellipse ellipse)
        {
            newElement = CreateEllipseElement(
                Canvas.GetLeft(ellipse) + offsetX,
                Canvas.GetTop(ellipse) + offsetY,
                ellipse.Width,
                ellipse.Height
            );
            if (newElement is Ellipse newEllipse)
            {
                newEllipse.Width = ellipse.Width;
                newEllipse.Height = ellipse.Height;
                newEllipse.Fill = ellipse.Fill;
                newEllipse.Stroke = ellipse.Stroke;
                newEllipse.StrokeThickness = ellipse.StrokeThickness;
                newEllipse.StrokeDashArray = ellipse.StrokeDashArray;
            }
        }
        else if (selectedElement is Polygon polygon)
        {
            // Copy points collection
            var pointsCopy = new PointCollection();
            foreach (var point in polygon.Points)
            {
                pointsCopy.Add(new Point(point.X, point.Y));
            }

            newElement = CreatePolygonElement(
                pointsCopy,
                Canvas.GetLeft(polygon) + offsetX,
                Canvas.GetTop(polygon) + offsetY
            );
            if (newElement is Polygon newPolygon)
            {
                newPolygon.Fill = polygon.Fill;
                newPolygon.Stroke = polygon.Stroke;
                newPolygon.StrokeThickness = polygon.StrokeThickness;
            }
        }
        else if (selectedElement is Image image)
        {
            if (image.Tag is Tuple<CanvasElement, BitmapSource> tuple)
            {
                var canvasElement = tuple.Item1;
                newElement = CreateImageElement(
                    canvasElement.ImagePath ?? "",
                    Canvas.GetLeft(image) + offsetX,
                    Canvas.GetTop(image) + offsetY
                );
                if (newElement is Image newImage)
                {
                    newImage.Width = image.Width;
                    newImage.Height = image.Height;

                    // Copy filter settings
                    if (newImage.Tag is Tuple<CanvasElement, BitmapSource> newTuple)
                    {
                        var newCanvasElement = newTuple.Item1;
                        newCanvasElement.MonochromeEnabled = canvasElement.MonochromeEnabled;
                        newCanvasElement.Threshold = canvasElement.Threshold;
                        newCanvasElement.MonochromeAlgorithm = canvasElement.MonochromeAlgorithm;
                        newCanvasElement.InvertColors = canvasElement.InvertColors;
                        newCanvasElement.Brightness = canvasElement.Brightness;
                        newCanvasElement.Contrast = canvasElement.Contrast;

                        if (newCanvasElement.MonochromeEnabled == true)
                        {
                            ApplyImageFilter(newImage);
                        }
                    }
                }
            }
        }
        else if (selectedElement is Canvas lineCanvas && lineCanvas.Tag is Tuple<Line, CanvasElement>)
        {
            // Line element wrapped in Canvas
            var lineData = (Tuple<Line, CanvasElement>)lineCanvas.Tag;
            var line = lineData.Item1;

            // Get canvas position
            double canvasLeft = Canvas.GetLeft(lineCanvas);
            double canvasTop = Canvas.GetTop(lineCanvas);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            // Calculate absolute coordinates
            double x1 = canvasLeft + line.X1;
            double y1 = canvasTop + line.Y1;
            double x2 = canvasLeft + line.X2;
            double y2 = canvasTop + line.Y2;

            // Create new line with offset
            newElement = CreateLineElement(
                x1 + offsetX,
                y1 + offsetY,
                x2 + offsetX,
                y2 + offsetY
            );

            // Copy stroke properties
            if (newElement is Canvas newLineCanvas && newLineCanvas.Tag is Tuple<Line, CanvasElement>)
            {
                var newLineData = (Tuple<Line, CanvasElement>)newLineCanvas.Tag;
                var newLine = newLineData.Item1;
                newLine.Stroke = line.Stroke;
                newLine.StrokeThickness = line.StrokeThickness;
            }
        }
        else if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement>)
        {
            var arrowData = (Tuple<Line, Polygon, Polygon, CanvasElement>)arrowCanvas.Tag;
            var canvasElement = arrowData.Item4;

            double canvasLeft = Canvas.GetLeft(arrowCanvas);
            double canvasTop = Canvas.GetTop(arrowCanvas);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            newElement = CreateArrowElement(
                canvasLeft + offsetX,
                canvasTop + offsetY,
                (canvasElement.X2 ?? 0) + offsetX,
                (canvasElement.Y2 ?? 0) + offsetY,
                canvasElement.HasStartArrow ?? false,
                canvasElement.HasEndArrow ?? true,
                canvasElement.ArrowheadSize ?? 10
            );
            if (newElement is Canvas newArrowCanvas && newArrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement> newArrowData)
            {
                var newLine = newArrowData.Item1;
                var origLine = arrowData.Item1;
                newLine.Stroke = origLine.Stroke;
                newLine.StrokeThickness = origLine.StrokeThickness;

                // Update arrowhead colors
                if (newArrowData.Item2 != null && arrowData.Item2 != null)
                {
                    newArrowData.Item2.Fill = arrowData.Item2.Fill;
                    newArrowData.Item2.Stroke = arrowData.Item2.Stroke;
                }
                if (newArrowData.Item3 != null && arrowData.Item3 != null)
                {
                    newArrowData.Item3.Fill = arrowData.Item3.Fill;
                    newArrowData.Item3.Stroke = arrowData.Item3.Stroke;
                }
            }
        }

        if (newElement != null)
        {
            commandManager.ExecuteCommand(new AddElementCommand(newElement, designCanvas));
            UpdateUndoRedoButtons();
            isDirty = true;

            // Select the new element
            SelectElement(newElement);
        }
    }

    // Delete selected element
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null)
            return;

        var elementToDelete = selectedElement;
        int index = designCanvas.Children.IndexOf(elementToDelete);
        SelectElement(null);

        // Unregister element control before deletion
        UnregisterElementControl(elementToDelete);

        commandManager.ExecuteCommand(new DeleteElementCommand(elementToDelete, designCanvas, index));
        UpdateUndoRedoButtons();
        isDirty = true;
    }

    // Alignment commands
    private void AlignLeft_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;
        MoveElementTo(selectedElement, 0, null);
    }

    private void AlignRight_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null || selectedElement is not FrameworkElement fe) return;
        MoveElementTo(selectedElement, designCanvas.ActualWidth - fe.ActualWidth, null);
    }

    private void AlignTop_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;
        MoveElementTo(selectedElement, null, 0);
    }

    private void AlignBottom_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null || selectedElement is not FrameworkElement fe) return;
        MoveElementTo(selectedElement, null, designCanvas.ActualHeight - fe.ActualHeight);
    }

    private void AlignCenterH_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null || selectedElement is not FrameworkElement fe) return;
        MoveElementTo(selectedElement, (designCanvas.ActualWidth - fe.ActualWidth) / 2, null);
    }

    private void AlignCenterV_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null || selectedElement is not FrameworkElement fe) return;
        MoveElementTo(selectedElement, null, (designCanvas.ActualHeight - fe.ActualHeight) / 2);
    }

    private void AlignCenter_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null || selectedElement is not FrameworkElement fe) return;
        MoveElementTo(selectedElement,
            (designCanvas.ActualWidth - fe.ActualWidth) / 2,
            (designCanvas.ActualHeight - fe.ActualHeight) / 2);
    }

    // Z-Order commands
    private void BringToFront_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;
        int currentIndex = designCanvas.Children.IndexOf(selectedElement);
        if (currentIndex < designCanvas.Children.Count - 1)
        {
            designCanvas.Children.Remove(selectedElement);
            designCanvas.Children.Add(selectedElement);
            isDirty = true;
        }
    }

    private void SendToBack_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;
        int currentIndex = designCanvas.Children.IndexOf(selectedElement);
        if (currentIndex > 0)
        {
            designCanvas.Children.Remove(selectedElement);
            designCanvas.Children.Insert(0, selectedElement);
            isDirty = true;
        }
    }

    private void BringForward_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;
        int currentIndex = designCanvas.Children.IndexOf(selectedElement);
        if (currentIndex < designCanvas.Children.Count - 1)
        {
            designCanvas.Children.Remove(selectedElement);
            designCanvas.Children.Insert(currentIndex + 1, selectedElement);
            isDirty = true;
        }
    }

    private void SendBackward_Click(object sender, RoutedEventArgs e)
    {
        if (selectedElement == null) return;
        int currentIndex = designCanvas.Children.IndexOf(selectedElement);
        if (currentIndex > 0)
        {
            designCanvas.Children.Remove(selectedElement);
            designCanvas.Children.Insert(currentIndex - 1, selectedElement);
            isDirty = true;
        }
    }

    // Grid/Snap settings
    private void SnapToGrid_Click(object sender, RoutedEventArgs e)
    {
        snapToGrid = !snapToGrid;
        menuSnapToGrid.IsChecked = snapToGrid;
    }

    private void GridSize5_Click(object sender, RoutedEventArgs e)
    {
        gridSize = 5; // 5 mm
        UpdateGridBackground();
        UpdateGridSizeMenuChecks();
    }

    private void GridSize10_Click(object sender, RoutedEventArgs e)
    {
        gridSize = 10; // 10 mm
        UpdateGridBackground();
        UpdateGridSizeMenuChecks();
    }

    private void GridSize20_Click(object sender, RoutedEventArgs e)
    {
        gridSize = 20; // 20 mm
        UpdateGridBackground();
        UpdateGridSizeMenuChecks();
    }

    private void GridSize50_Click(object sender, RoutedEventArgs e)
    {
        gridSize = 50; // 50 mm
        UpdateGridBackground();
        UpdateGridSizeMenuChecks();
    }

    private void UpdateGridSizeMenuChecks()
    {
        menuGridSize5.IsChecked = (gridSize == 5);
        menuGridSize10.IsChecked = (gridSize == 10);
        menuGridSize20.IsChecked = (gridSize == 20);
        menuGridSize50.IsChecked = (gridSize == 50);
    }

    private void UpdateGridBackground()
    {
        var drawingGroup = new DrawingGroup();

        // White background
        drawingGroup.Children.Add(new GeometryDrawing(
            Brushes.White,
            null,
            new RectangleGeometry(new Rect(0, 0, designCanvas.Width, designCanvas.Height))
        ));

        // Draw grid lines (gridSize is in mm, convert to pixels)
        double gridSizePixels = gridSize * MM_TO_PIXELS;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(80, 200, 200, 200)), 0.5);
        pen.Freeze();

        for (double x = 0; x <= designCanvas.Width; x += gridSizePixels)
        {
            drawingGroup.Children.Add(new GeometryDrawing(null, pen,
                new LineGeometry(new Point(x, 0), new Point(x, designCanvas.Height))));
        }

        for (double y = 0; y <= designCanvas.Height; y += gridSizePixels)
        {
            drawingGroup.Children.Add(new GeometryDrawing(null, pen,
                new LineGeometry(new Point(0, y), new Point(designCanvas.Width, y))));
        }

        var drawingBrush = new DrawingBrush(drawingGroup);
        drawingBrush.Freeze();

        designCanvas.Background = drawingBrush;
    }

    // Helper method to move element to absolute position with undo/redo
    private void MoveElementTo(UIElement element, double? newX, double? newY)
    {
        double oldX = Canvas.GetLeft(element);
        double oldY = Canvas.GetTop(element);
        if (double.IsNaN(oldX)) oldX = 0;
        if (double.IsNaN(oldY)) oldY = 0;

        double finalX = newX ?? oldX;
        double finalY = newY ?? oldY;

        if (Math.Abs(finalX - oldX) > 0.01 || Math.Abs(finalY - oldY) > 0.01)
        {
            commandManager.ExecuteCommand(new MoveElementCommand(element, new Point(oldX, oldY), new Point(finalX, finalY)));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    // Helper method to move element by offset (for arrow keys)
    private void MoveElementByOffset(UIElement element, double dx, double dy)
    {
        double oldX = Canvas.GetLeft(element);
        double oldY = Canvas.GetTop(element);
        if (double.IsNaN(oldX)) oldX = 0;
        if (double.IsNaN(oldY)) oldY = 0;

        double newX = oldX + dx;
        double newY = oldY + dy;

        // Apply snap to grid if enabled (gridSize is in mm, positions in pixels)
        if (snapToGrid)
        {
            // Convert pixels to mm, round to grid, convert back to pixels
            double xMm = newX * PIXELS_TO_MM;
            double yMm = newY * PIXELS_TO_MM;
            xMm = Math.Round(xMm / gridSize) * gridSize;
            yMm = Math.Round(yMm / gridSize) * gridSize;
            newX = xMm * MM_TO_PIXELS;
            newY = yMm * MM_TO_PIXELS;
        }

        if (Math.Abs(newX - oldX) > 0.01 || Math.Abs(newY - oldY) > 0.01)
        {
            commandManager.ExecuteCommand(new MoveElementCommand(element, new Point(oldX, oldY), new Point(newX, newY)));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }
}