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
using SimpleLabel.Controllers;
using SimpleLabel.Services;
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
    internal bool isDirty = false;
    private readonly Commands.CommandManager commandManager = new();

    // Property panel controller for handling property changes
    private PropertyPanelController? _propertyPanelController;

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

        // Initialize property panel controller
        _propertyPanelController = new PropertyPanelController(
            commandManager,
            UpdatePropertiesPanel,
            UpdateUndoRedoButtons,
            () => isDirty = true,
            GetElementControl);

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

        // Sync with property panel controller
        if (_propertyPanelController != null)
        {
            _propertyPanelController.SelectedElement = element;
        }

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

            // Delegate to IElementControl if registered
            var elementControl = GetElementControl(selectedElement);
            if (elementControl != null)
            {
                elementControl.PopulatePropertiesPanel();
                return;
            }

            // No IElementControl registered - hide all type-specific controls
            groupTextFormatting.Visibility = Visibility.Collapsed;
            groupShapeStyling.Visibility = Visibility.Collapsed;
            groupImageFilters.Visibility = Visibility.Collapsed;
            groupArrowControls.Visibility = Visibility.Collapsed;
            labelX2.Visibility = Visibility.Collapsed;
            propertyX2.Visibility = Visibility.Collapsed;
            labelY2.Visibility = Visibility.Collapsed;
            propertyY2.Visibility = Visibility.Collapsed;
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
        var textBlock = ElementFactory.CreateTextElement("Sample Text", 50, 50,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
        var rectangle = ElementFactory.CreateRectangleElement(100, 100, 80, 60,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
        var ellipse = ElementFactory.CreateEllipseElement(150, 150, 80, 80,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
        var lineCanvas = ElementFactory.CreateLineElement(100, 100, 200, 100,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
        var arrowCanvas = ElementFactory.CreateArrowElement(100, 150, 200, 150, false, true, 10,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
        var points = ElementFactory.CreateTrianglePoints(80);
        var triangle = ElementFactory.CreatePolygonElement(points, 100, 100,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
        var points = ElementFactory.CreateStarPoints(80);
        var star = ElementFactory.CreatePolygonElement(points, 150, 150,
            Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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
                var image = ElementFactory.CreateImageElement(dialog.FileName, 200, 200,
                    designCanvas.ActualWidth, designCanvas.ActualHeight,
                    Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp);
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

                // Update CanvasElement coordinates for Line/Arrow after move
                // The CanvasElement stores absolute coordinates used for serialization
                UpdateLineArrowCanvasElement(draggedElement);
            }
        }

        draggedElement?.ReleaseMouseCapture();
        isDragging = false;
        draggedElement = null;
        e.Handled = true;
    }

    /// <summary>
    /// Updates the CanvasElement coordinates for Line/Arrow elements after a move operation.
    /// The CanvasElement stores absolute coordinates used for serialization.
    /// </summary>
    private void UpdateLineArrowCanvasElement(UIElement element)
    {
        if (element is Canvas lineCanvas && lineCanvas.Tag is Tuple<Line, CanvasElement> lineData)
        {
            var line = lineData.Item1;
            var canvasElement = lineData.Item2;
            double canvasLeft = Canvas.GetLeft(lineCanvas);
            double canvasTop = Canvas.GetTop(lineCanvas);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            // Calculate absolute coordinates from canvas position + internal line coordinates
            canvasElement.X = canvasLeft + line.X1;
            canvasElement.Y = canvasTop + line.Y1;
            canvasElement.X2 = canvasLeft + line.X2;
            canvasElement.Y2 = canvasTop + line.Y2;
        }
        else if (element is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement> arrowData)
        {
            var line = arrowData.Item1;
            var canvasElement = arrowData.Item4;
            double canvasLeft = Canvas.GetLeft(arrowCanvas);
            double canvasTop = Canvas.GetTop(arrowCanvas);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            // Calculate absolute coordinates from canvas position + internal line coordinates
            canvasElement.X = canvasLeft + line.X1;
            canvasElement.Y = canvasTop + line.Y1;
            canvasElement.X2 = canvasLeft + line.X2;
            canvasElement.Y2 = canvasTop + line.Y2;
        }
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
        return LabelSerializer.SerializeCanvas(designCanvas);
    }

    private void DeserializeCanvas(LabelDocument doc)
    {
        // Clear element controls before clearing canvas
        _elementControls.Clear();
        designCanvas.Children.Clear();
        SelectElement(null);

        UpdateStatusBar();
        UpdateGridBackground();

        // Use LabelSerializer with callbacks for element creation and post-processing
        LabelSerializer.DeserializeToCanvas(
            doc,
            designCanvas,
            CreateElementFromCanvasElement,
            (uiElement, element) =>
            {
                // Apply element-specific properties and register control
                ApplyDeserializedProperties(uiElement, element);
                RegisterElementControlForLoadedElement(uiElement, element);
            }
        );

        isDirty = false;
    }

    /// <summary>
    /// Creates a UIElement from a CanvasElement with proper event handler wiring.
    /// </summary>
    private UIElement? CreateElementFromCanvasElement(CanvasElement element)
    {
        // Validate dimensions - use defaults for invalid values
        double width = LabelSerializer.ValidateSize(element.Width, 80);
        double height = LabelSerializer.ValidateSize(element.Height, 60);

        return element.ElementType switch
        {
            "Text" => ElementFactory.CreateTextElement(
                element.Text ?? "Text",
                element.X,
                element.Y,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            "Rectangle" => ElementFactory.CreateRectangleElement(
                element.X,
                element.Y,
                width > 0 ? width : 80,
                height > 0 ? height : 60,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            "Ellipse" => ElementFactory.CreateEllipseElement(
                element.X,
                element.Y,
                width > 0 ? width : 80,
                height > 0 ? height : 80,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            "Polygon" when !string.IsNullOrEmpty(element.PolygonPoints) =>
                ElementFactory.CreatePolygonElement(
                    LabelSerializer.ParsePolygonPoints(element.PolygonPoints),
                    element.X,
                    element.Y,
                    Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            "Image" when !string.IsNullOrEmpty(element.ImagePath) =>
                ElementFactory.CreateImageElement(
                    element.ImagePath,
                    element.X,
                    element.Y,
                    designCanvas.ActualWidth,
                    designCanvas.ActualHeight,
                    Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            "Line" => ElementFactory.CreateLineElement(
                element.X,
                element.Y,
                element.X2 ?? element.X + 100,
                element.Y2 ?? element.Y,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            "Arrow" => ElementFactory.CreateArrowElement(
                element.X,
                element.Y,
                element.X2 ?? element.X + 100,
                element.Y2 ?? element.Y,
                element.HasStartArrow ?? false,
                element.HasEndArrow ?? true,
                element.ArrowheadSize ?? 10,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp),

            _ => null
        };
    }

    /// <summary>
    /// Applies deserialized properties to a created UIElement.
    /// </summary>
    private void ApplyDeserializedProperties(UIElement uiElement, CanvasElement element)
    {
        switch (element.ElementType)
        {
            case "Text" when uiElement is TextBlock tb:
                LabelSerializer.ApplyTextProperties(tb, element);
                break;

            case "Rectangle" when uiElement is Rectangle rect:
                LabelSerializer.ApplyRectangleProperties(rect, element);
                break;

            case "Ellipse" when uiElement is Ellipse ellipse:
                LabelSerializer.ApplyEllipseProperties(ellipse, element);
                break;

            case "Polygon" when uiElement is Polygon poly:
                LabelSerializer.ApplyPolygonProperties(poly, element);
                break;

            case "Image" when uiElement is Image img:
                double imgWidth = LabelSerializer.ValidateSize(element.Width);
                double imgHeight = LabelSerializer.ValidateSize(element.Height);
                if (imgWidth > 0) img.Width = imgWidth;
                if (imgHeight > 0) img.Height = imgHeight;
                // Restore filter settings if present
                if (img.Tag is Tuple<CanvasElement, BitmapSource> tuple)
                {
                    var canvasElement = tuple.Item1;
                    canvasElement.MonochromeEnabled = element.MonochromeEnabled;
                    canvasElement.Threshold = element.Threshold;
                    canvasElement.MonochromeAlgorithm = element.MonochromeAlgorithm;
                    canvasElement.InvertColors = element.InvertColors;
                    canvasElement.Brightness = element.Brightness;
                    canvasElement.Contrast = element.Contrast;

                    if (canvasElement.MonochromeEnabled == true)
                    {
                        ApplyImageFilter(img);
                    }
                }
                break;

            case "Line" when uiElement is Canvas lineCanvas:
                LabelSerializer.ApplyLineProperties(lineCanvas, element);
                break;

            case "Arrow" when uiElement is Canvas arrowCanvas:
                LabelSerializer.ApplyArrowProperties(arrowCanvas, element);
                break;
        }
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

    #region Properties Panel Event Handlers - Delegated to PropertyPanelController

    // GotFocus handler - captures initial values for undo/redo
    private void Property_GotFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandleGotFocus(sender, e);

    // ValueChanged handler for NumericUpDown controls
    private void Property_ValueChanged(object sender, double newValue)
        => _propertyPanelController?.HandleValueChanged(sender, newValue,
            propertyX, propertyY, propertyWidth, propertyHeight,
            propertyStrokeThickness, propertyRadiusX, propertyRadiusY);

    // Position/Size property handlers
    private void PropertyX_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyXLostFocus(propertyX);

    private void PropertyY_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyYLostFocus(propertyY);

    private void PropertyWidth_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyWidthLostFocus(propertyWidth);

    private void PropertyHeight_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyHeightLostFocus(propertyHeight);

    private void PropertyX2_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyX2LostFocus(propertyX2);

    private void PropertyY2_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyY2LostFocus(propertyY2);

    // Text formatting event handlers
    private void PropertyText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            _propertyPanelController?.HandlePropertyTextLostFocus(textBox);
    }

    private void PropertyText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox)
            _propertyPanelController?.HandlePropertyTextPreviewKeyDown(textBox, e);
    }

    private void PropertyFontFamily_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _propertyPanelController?.HandlePropertyFontFamilyLostFocus(comboBox);
    }

    private void PropertyFontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _propertyPanelController?.HandlePropertyFontSizeChanged(comboBox);
    }

    private void PropertyFontSize_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _propertyPanelController?.HandlePropertyFontSizeLostFocus(comboBox);
    }

    private void PropertyColor_Click(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyColorClick();

    private void PropertyAlignment_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _propertyPanelController?.HandlePropertyAlignmentLostFocus(comboBox);
    }

    private void PropertyBold_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyBoldChanged(checkBox);
    }

    private void PropertyItalic_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyItalicChanged(checkBox);
    }

    // Shape styling event handlers
    private void PropertyFillColor_Click(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyFillColorClick();

    private void PropertyStrokeColor_Click(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyStrokeColorClick();

    private void PropertyStrokeThickness_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyStrokeThicknessLostFocus(propertyStrokeThickness);

    // Extended shape property event handlers
    private void PropertyRadiusX_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyRadiusXLostFocus(propertyRadiusX);

    private void PropertyRadiusY_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyRadiusYLostFocus(propertyRadiusY);

    private void PropertyStrokeDashPattern_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _propertyPanelController?.HandlePropertyStrokeDashPatternChanged(comboBox);
    }

    private void PropertyUseGradientFill_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyUseGradientFillChanged(checkBox);
    }

    private void PropertyGradientStart_Click(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyGradientStartClick();

    private void PropertyGradientEnd_Click(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyGradientEndClick();

    private void PropertyGradientAngle_LostFocus(object sender, RoutedEventArgs e)
        => _propertyPanelController?.HandlePropertyGradientAngleLostFocus(propertyGradientAngle);

    // Image filter event handlers
    private void PropertyMonochromeEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyMonochromeEnabledChanged(checkBox, panelMonochromeControls, ApplyImageFilter);
    }

    private void PropertyThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyThresholdValueChanged(slider, propertyThresholdValue, ApplyImageFilter);
    }

    private void PropertyThreshold_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyThresholdLostFocus(slider, ApplyImageFilter);
    }

    private void PropertyAlgorithm_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _propertyPanelController?.HandlePropertyAlgorithmChanged(comboBox, ApplyImageFilter);
    }

    private void PropertyBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyBrightnessValueChanged(slider, propertyBrightnessValue, ApplyImageFilter);
    }

    private void PropertyBrightness_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyBrightnessLostFocus(slider, ApplyImageFilter);
    }

    private void PropertyContrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyContrastValueChanged(slider, propertyContrastValue, ApplyImageFilter);
    }

    private void PropertyContrast_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyContrastLostFocus(slider, ApplyImageFilter);
    }

    private void PropertyInvert_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyInvertChanged(checkBox, ApplyImageFilter);
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
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyHasStartArrowChanged(checkBox);
    }

    private void PropertyHasEndArrow_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            _propertyPanelController?.HandlePropertyHasEndArrowChanged(checkBox);
    }

    private void PropertyArrowheadSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => _propertyPanelController?.HandlePropertyArrowheadSizeValueChanged(e.NewValue, propertyArrowheadSizeValue);

    private void PropertyArrowheadSize_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
            _propertyPanelController?.HandlePropertyArrowheadSizeLostFocus(slider);
    }

    #endregion

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
            newElement = ElementFactory.CreateTextElement(
                textBlock.Text,
                Canvas.GetLeft(textBlock) + offsetX,
                Canvas.GetTop(textBlock) + offsetY,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
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
            newElement = ElementFactory.CreateRectangleElement(
                Canvas.GetLeft(rect) + offsetX,
                Canvas.GetTop(rect) + offsetY,
                rect.Width,
                rect.Height,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
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
            newElement = ElementFactory.CreateEllipseElement(
                Canvas.GetLeft(ellipse) + offsetX,
                Canvas.GetTop(ellipse) + offsetY,
                ellipse.Width,
                ellipse.Height,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
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

            newElement = ElementFactory.CreatePolygonElement(
                pointsCopy,
                Canvas.GetLeft(polygon) + offsetX,
                Canvas.GetTop(polygon) + offsetY,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
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
                newElement = ElementFactory.CreateImageElement(
                    canvasElement.ImagePath ?? "",
                    Canvas.GetLeft(image) + offsetX,
                    Canvas.GetTop(image) + offsetY,
                    designCanvas.ActualWidth,
                    designCanvas.ActualHeight,
                    Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
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
            newElement = ElementFactory.CreateLineElement(
                x1 + offsetX,
                y1 + offsetY,
                x2 + offsetX,
                y2 + offsetY,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
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
        else if (selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement>)
        {
            var arrowData = (Tuple<Line, Polygon?, Polygon?, CanvasElement>)arrowCanvas.Tag;
            var canvasElement = arrowData.Item4;

            double canvasLeft = Canvas.GetLeft(arrowCanvas);
            double canvasTop = Canvas.GetTop(arrowCanvas);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            newElement = ElementFactory.CreateArrowElement(
                canvasLeft + offsetX,
                canvasTop + offsetY,
                (canvasElement.X2 ?? 0) + offsetX,
                (canvasElement.Y2 ?? 0) + offsetY,
                canvasElement.HasStartArrow ?? false,
                canvasElement.HasEndArrow ?? true,
                canvasElement.ArrowheadSize ?? 10,
                Element_Select, Element_MouseLeftButtonDown, Element_MouseMove, Element_MouseLeftButtonUp
            );
            if (newElement is Canvas newArrowCanvas && newArrowCanvas.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement> newArrowData)
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