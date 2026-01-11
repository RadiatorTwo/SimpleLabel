using System.Collections.ObjectModel;
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

    // Multi-selection support
    private readonly HashSet<UIElement> _selectedElements = new();

    // Marquee selection support
    private bool _isMarqueeSelecting = false;
    private Point _marqueeStartPoint;
    private Rectangle? _marqueeRectangle;

    // Multi-element drag support
    private Dictionary<UIElement, Point> _dragInitialPositions = new();
    private Dictionary<UIElement, Point> _dragInitialEndPositions = new(); // For Lines (X2/Y2)

    private string? currentFilePath = null;
    internal bool isDirty = false;
    private readonly Commands.CommandManager commandManager = new();

    // Property panel controller for handling property changes
    private PropertyPanelController? _propertyPanelController;

    // Grid/Snap settings
    private bool snapToGrid = true;
    private bool snapToElements = true;
    private double gridSize = 5; // Default 5 mm

    // Snap-to-element guidelines
    private Line? _verticalSnapGuideline;
    private Line? _horizontalSnapGuideline;
    private const double SNAP_THRESHOLD_PIXELS = 8;

    // Unit conversion constants (96 DPI)
    private const double MM_TO_PIXELS = 96.0 / 25.4;
    private const double PIXELS_TO_MM = 25.4 / 96.0;

    // Element control registry for IElementControl lookup
    private readonly Dictionary<UIElement, IElementControl> _elementControls = new();

    // Layers collection for Z-order management
    public ObservableCollection<LayerItem> Layers { get; } = new();

    // Guard variable to prevent selection sync loops
    private bool _syncingSelection = false;

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

        // Initialize Layers panel
        layersPanel.ItemsSource = Layers;
        layersPanel.ReorderRequested += LayersPanel_ReorderRequested;
        layersPanel.SelectionChanged += LayersPanel_SelectionChanged;

        // Initialize marquee rectangle for selection
        _marqueeRectangle = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)), // #0078D7
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)), // Semi-transparent
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        // Initialize snap-to-element guidelines
        InitializeSnapGuidelines();

        // Set snap to elements menu item checked
        menuSnapToElements.IsChecked = snapToElements;
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

    #region Layers Panel Integration

    /// <summary>
    /// Handles selection changes from the Layers panel.
    /// </summary>
    private void LayersPanel_SelectionChanged(object? sender, LayerItem? layerItem)
    {
        if (_syncingSelection)
            return;

        _syncingSelection = true;
        try
        {
            // Get all selected items from layers panel
            var selectedItems = layersPanel.SelectedLayerItems;

            // Clear existing selection
            foreach (var element in _selectedElements.ToList())
            {
                RemoveAdornerFromElement(element);
            }
            _selectedElements.Clear();

            // Add all selected layers to selection
            foreach (var item in selectedItems)
            {
                if (item is LayerItem layer && layer.Element != null)
                {
                    _selectedElements.Add(layer.Element);
                    AddAdornerToElement(layer.Element);
                }
            }

            // Update primary selection (last selected or first in collection)
            if (layerItem?.Element != null && _selectedElements.Contains(layerItem.Element))
            {
                selectedElement = layerItem.Element;
            }
            else if (_selectedElements.Count > 0)
            {
                selectedElement = _selectedElements.Last();
            }
            else
            {
                selectedElement = null;
            }

            // Sync with property panel controller
            if (_propertyPanelController != null)
            {
                _propertyPanelController.SelectedElement = selectedElement;
            }

            UpdatePropertiesPanel();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Handles reorder requests from the Layers panel.
    /// </summary>
    private void LayersPanel_ReorderRequested(object? sender, ReorderEventArgs e)
    {
        if (e.OldIndex < 0 || e.NewIndex < 0 || e.OldIndex >= Layers.Count || e.NewIndex >= Layers.Count)
            return;

        // Move in Layers collection
        Layers.Move(e.OldIndex, e.NewIndex);

        // Sync Canvas from Layers
        SyncCanvasFromLayers();

        isDirty = true;
    }

    /// <summary>
    /// Synchronizes the Canvas.Children order to match the Layers collection.
    /// Layers[0] = top (visually) = last in Canvas.Children.
    /// </summary>
    private void SyncCanvasFromLayers()
    {
        if (Layers.Count == 0)
            return;

        // Remove all elements temporarily
        var elements = Layers.Select(l => l.Element).Where(e => e != null).ToList();

        designCanvas.Children.Clear();

        // Re-add in reverse order (Layers[0] is top, should be last in Children)
        for (int i = Layers.Count - 1; i >= 0; i--)
        {
            if (Layers[i].Element != null)
            {
                designCanvas.Children.Add(Layers[i].Element!);
            }
        }
    }

    /// <summary>
    /// Rebuilds the Layers collection from the current Canvas.Children.
    /// Canvas.Children last = top (visually) = Layers[0].
    /// </summary>
    public void SyncLayersFromCanvas()
    {
        Layers.Clear();

        // Iterate from end to start (top to bottom visually)
        for (int i = designCanvas.Children.Count - 1; i >= 0; i--)
        {
            var element = designCanvas.Children[i];
            var elementType = GetElementTypeForLayer(element);
            var displayName = LayerItem.GetDisplayName(element, elementType);

            Layers.Add(new LayerItem
            {
                Element = element,
                ElementType = elementType,
                DisplayName = displayName
            });
        }
    }

    /// <summary>
    /// Adds a layer for a newly created element (inserts at top = index 0).
    /// </summary>
    private void AddLayerForElement(UIElement element, ElementType elementType)
    {
        var displayName = LayerItem.GetDisplayName(element, elementType);
        Layers.Insert(0, new LayerItem
        {
            Element = element,
            ElementType = elementType,
            DisplayName = displayName
        });
    }

    /// <summary>
    /// Removes the layer for a deleted element.
    /// </summary>
    private void RemoveLayerForElement(UIElement element)
    {
        var layerItem = Layers.FirstOrDefault(l => l.Element == element);
        if (layerItem != null)
        {
            Layers.Remove(layerItem);
        }
    }

    /// <summary>
    /// Determines the ElementType for a given UIElement for layer display.
    /// </summary>
    private ElementType GetElementTypeForLayer(UIElement element)
    {
        // First check if we have an IElementControl registered
        if (_elementControls.TryGetValue(element, out var control))
        {
            return control.ElementType;
        }

        // Fallback based on element type
        return element switch
        {
            TextBlock => ElementType.Text,
            Rectangle => ElementType.Rectangle,
            Ellipse => ElementType.Ellipse,
            Polygon => ElementType.Polygon,
            Image => ElementType.Image,
            Canvas c when c.Tag is Tuple<Line, CanvasElement> => ElementType.Line,
            Canvas c when c.Tag is Tuple<Line, Polygon?, Polygon?, CanvasElement> => ElementType.Arrow,
            _ => ElementType.Rectangle
        };
    }

    #endregion

    private void SelectElement(UIElement? element, bool addToSelection = false)
    {
        if (element == null)
        {
            // Clear all selection
            ClearSelection();
            return;
        }

        if (addToSelection)
        {
            // Toggle selection - add if not present, remove if present
            if (_selectedElements.Contains(element))
            {
                // Remove from selection
                RemoveAdornerFromElement(element);
                _selectedElements.Remove(element);

                // Update primary selection to last remaining element or null
                if (_selectedElements.Count > 0)
                {
                    selectedElement = _selectedElements.Last();
                }
                else
                {
                    selectedElement = null;
                }
            }
            else
            {
                // Add to selection
                _selectedElements.Add(element);
                AddAdornerToElement(element);
                selectedElement = element; // Last added becomes primary
            }
        }
        else
        {
            // Single selection - clear others first
            ClearSelection();
            _selectedElements.Add(element);
            AddAdornerToElement(element);
            selectedElement = element;
        }

        // Sync with property panel controller
        if (_propertyPanelController != null)
        {
            _propertyPanelController.SelectedElement = selectedElement;
        }

        // Sync with Layers panel (Canvas → Layers direction)
        SyncLayersPanelSelection();

        // Update properties panel
        UpdatePropertiesPanel();
    }

    /// <summary>
    /// Clears all selected elements and removes their adorners.
    /// </summary>
    private void ClearSelection()
    {
        // Remove adorners from all selected elements
        foreach (var element in _selectedElements)
        {
            RemoveAdornerFromElement(element);
        }
        _selectedElements.Clear();
        selectedElement = null;

        // Sync with property panel controller
        if (_propertyPanelController != null)
        {
            _propertyPanelController.SelectedElement = null;
        }

        // Sync with Layers panel
        SyncLayersPanelSelection();

        // Update properties panel
        UpdatePropertiesPanel();
    }

    /// <summary>
    /// Adds a ResizeAdorner to the specified element.
    /// </summary>
    private void AddAdornerToElement(UIElement element)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer != null)
        {
            layer.Add(new ResizeAdorner(element));
        }
    }

    /// <summary>
    /// Removes all adorners from the specified element.
    /// </summary>
    private void RemoveAdornerFromElement(UIElement element)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer != null)
        {
            var adorners = layer.GetAdorners(element);
            if (adorners != null)
            {
                foreach (var adorner in adorners)
                {
                    layer.Remove(adorner);
                }
            }
        }
    }

    /// <summary>
    /// Synchronizes the Layers panel selection with the internal selection state.
    /// </summary>
    private void SyncLayersPanelSelection()
    {
        if (_syncingSelection)
            return;

        _syncingSelection = true;
        try
        {
            // Clear current layers panel selection
            layersPanel.SelectedLayerItems.Clear();

            // Add all selected elements to layers panel selection
            foreach (var element in _selectedElements)
            {
                var layerItem = Layers.FirstOrDefault(l => l.Element == element);
                if (layerItem != null)
                {
                    layersPanel.SelectedLayerItems.Add(layerItem);
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
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
            // Check if Shift is held for multi-selection
            bool addToSelection = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            // If clicking on an already-selected element in a multi-selection (without Shift),
            // keep the selection intact for group dragging
            if (!addToSelection && _selectedElements.Contains(element) && _selectedElements.Count > 1)
            {
                // Just update primary selection for properties panel, don't change the selection
                selectedElement = element;
                if (_propertyPanelController != null)
                {
                    _propertyPanelController.SelectedElement = element;
                }
                UpdatePropertiesPanel();
                return;
            }

            SelectElement(element, addToSelection);
            // Don't set e.Handled = true to allow drag handlers to also work
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only start marquee selection if clicking directly on canvas (not on child element)
        if (e.Source == designCanvas)
        {
            _marqueeStartPoint = e.GetPosition(designCanvas);
            _isMarqueeSelecting = true;

            // Initialize marquee rectangle position and size
            if (_marqueeRectangle != null)
            {
                Canvas.SetLeft(_marqueeRectangle, _marqueeStartPoint.X);
                Canvas.SetTop(_marqueeRectangle, _marqueeStartPoint.Y);
                _marqueeRectangle.Width = 0;
                _marqueeRectangle.Height = 0;
                _marqueeRectangle.Visibility = Visibility.Visible;

                if (!designCanvas.Children.Contains(_marqueeRectangle))
                {
                    designCanvas.Children.Add(_marqueeRectangle);
                }
            }

            designCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMarqueeSelecting || _marqueeRectangle == null)
            return;

        Point currentPos = e.GetPosition(designCanvas);

        // Calculate rectangle bounds (handle dragging in any direction)
        double x = Math.Min(_marqueeStartPoint.X, currentPos.X);
        double y = Math.Min(_marqueeStartPoint.Y, currentPos.Y);
        double width = Math.Abs(currentPos.X - _marqueeStartPoint.X);
        double height = Math.Abs(currentPos.Y - _marqueeStartPoint.Y);

        Canvas.SetLeft(_marqueeRectangle, x);
        Canvas.SetTop(_marqueeRectangle, y);
        _marqueeRectangle.Width = width;
        _marqueeRectangle.Height = height;
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMarqueeSelecting || _marqueeRectangle == null)
            return;

        _isMarqueeSelecting = false;
        designCanvas.ReleaseMouseCapture();

        // Hide and remove marquee rectangle
        _marqueeRectangle.Visibility = Visibility.Collapsed;
        designCanvas.Children.Remove(_marqueeRectangle);

        // Select elements within the marquee bounds
        SelectElementsInMarquee();
    }

    /// <summary>
    /// Selects all elements that intersect with the marquee rectangle bounds.
    /// </summary>
    private void SelectElementsInMarquee()
    {
        if (_marqueeRectangle == null)
            return;

        // Get marquee bounds
        double marqueeLeft = Canvas.GetLeft(_marqueeRectangle);
        double marqueeTop = Canvas.GetTop(_marqueeRectangle);
        if (double.IsNaN(marqueeLeft)) marqueeLeft = 0;
        if (double.IsNaN(marqueeTop)) marqueeTop = 0;

        Rect marqueeBounds = new Rect(marqueeLeft, marqueeTop,
                                      _marqueeRectangle.Width, _marqueeRectangle.Height);

        // Small marquee (click without drag) - clear selection
        if (marqueeBounds.Width < 5 && marqueeBounds.Height < 5)
        {
            ClearSelection();
            return;
        }

        // Check if Shift is held for adding to existing selection
        bool addToSelection = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        if (!addToSelection)
        {
            ClearSelection();
        }

        // Find all elements intersecting with marquee
        foreach (UIElement child in designCanvas.Children)
        {
            // Skip the marquee rectangle itself
            if (child == _marqueeRectangle)
                continue;

            // Get element bounds
            Rect elementBounds = GetElementBounds(child);

            // Check intersection
            if (marqueeBounds.IntersectsWith(elementBounds))
            {
                SelectElement(child, true); // Always addToSelection=true here
            }
        }
    }

    /// <summary>
    /// Gets the bounding rectangle for a UI element on the canvas.
    /// </summary>
    private Rect GetElementBounds(UIElement element)
    {
        if (element is Line line)
        {
            double minX = Math.Min(line.X1, line.X2);
            double minY = Math.Min(line.Y1, line.Y2);
            double maxX = Math.Max(line.X1, line.X2);
            double maxY = Math.Max(line.Y1, line.Y2);
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
        else if (element is FrameworkElement fe)
        {
            double left = Canvas.GetLeft(fe);
            double top = Canvas.GetTop(fe);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            return new Rect(left, top, Math.Max(1, fe.ActualWidth), Math.Max(1, fe.ActualHeight));
        }
        return Rect.Empty;
    }

    private void ClearCanvas_Click(object sender, RoutedEventArgs e)
    {
        isDirty = true;
        // Clear all element controls before clearing canvas
        _elementControls.Clear();
        designCanvas.Children.Clear();
        // Clear Layers panel
        Layers.Clear();
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

        // Add to Layers panel
        AddLayerForElement(textBlock, ElementType.Text);

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

        // Add to Layers panel
        AddLayerForElement(rectangle, ElementType.Rectangle);

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

        // Add to Layers panel
        AddLayerForElement(ellipse, ElementType.Ellipse);

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

        // Add to Layers panel
        AddLayerForElement(lineCanvas, ElementType.Line);

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

        // Add to Layers panel
        AddLayerForElement(arrowCanvas, ElementType.Arrow);

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

        // Add to Layers panel
        AddLayerForElement(triangle, ElementType.Triangle);

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

        // Add to Layers panel
        AddLayerForElement(star, ElementType.Star);

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

                // Add to Layers panel
                AddLayerForElement(image, ElementType.Image);

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
        _dragInitialPositions.Clear();
        _dragInitialEndPositions.Clear();

        if (draggedElement != null)
        {
            // Check if dragged element is part of multi-selection
            if (_selectedElements.Contains(draggedElement) && _selectedElements.Count > 1)
            {
                // Store positions of ALL selected elements
                foreach (var element in _selectedElements)
                {
                    StoreElementPosition(element);
                }
            }
            else
            {
                // Single element drag - store only this element's position
                StoreElementPosition(draggedElement);

                // Also keep legacy dragInitialPosition for compatibility
                if (draggedElement is Line line)
                {
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
        }

        isDragging = true;
        draggedElement?.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Stores the initial position of an element for drag operations.
    /// </summary>
    private void StoreElementPosition(UIElement element)
    {
        if (element is Line line)
        {
            _dragInitialPositions[element] = new Point(line.X1, line.Y1);
            _dragInitialEndPositions[element] = new Point(line.X2, line.Y2);
        }
        else
        {
            double left = Canvas.GetLeft(element);
            double top = Canvas.GetTop(element);
            if (double.IsNaN(left)) left = 0.0;
            if (double.IsNaN(top)) top = 0.0;
            _dragInitialPositions[element] = new Point(left, top);
        }
    }

    private void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isDragging || draggedElement == null)
            return;

        // Only process events from the element that has mouse capture
        if (sender != draggedElement)
            return;

        Point currentPos = e.GetPosition(designCanvas);
        Vector offset = currentPos - dragStartPoint;

        // Check if we're moving multiple elements
        if (_dragInitialPositions.Count > 1)
        {
            // Multi-element drag - use absolute positioning from initial positions
            MoveMultipleElementsAbsolute(offset);
            UpdatePropertiesPanel();
            e.Handled = true;
            return;
        }

        // Single element drag - existing logic below

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

        // Snap to elements (after grid snap, element snap takes precedence)
        HideSnapGuidelines();
        if (snapToElements && _selectedElements.Count <= 1)
        {
            var (snappedLeft, snappedTop, guideX, guideY) = CalculateElementSnap(
                newLeft, newTop, elementWidth, elementHeight, draggedElement);

            newLeft = snappedLeft;
            newTop = snappedTop;

            if (guideX.HasValue) ShowVerticalSnapGuideline(guideX.Value);
            if (guideY.HasValue) ShowHorizontalSnapGuideline(guideY.Value);
        }

        Canvas.SetLeft(draggedElement, newLeft);
        Canvas.SetTop(draggedElement, newTop);

        // Update properties panel in real-time during drag
        UpdatePropertiesPanel();

        e.Handled = true;
    }

    /// <summary>
    /// Moves all elements in _dragInitialPositions by the given offset from their initial positions.
    /// Uses absolute positioning: newPos = initialPos + offset
    /// Applies snap-to-grid if enabled.
    /// </summary>
    private void MoveMultipleElementsAbsolute(Vector offset)
    {
        // Apply snap to grid to the offset if enabled
        Vector snappedOffset = offset;
        if (snapToGrid)
        {
            // Snap the offset to grid units
            double offsetXMm = offset.X * PIXELS_TO_MM;
            double offsetYMm = offset.Y * PIXELS_TO_MM;
            offsetXMm = Math.Round(offsetXMm / gridSize) * gridSize;
            offsetYMm = Math.Round(offsetYMm / gridSize) * gridSize;
            snappedOffset = new Vector(offsetXMm * MM_TO_PIXELS, offsetYMm * MM_TO_PIXELS);
        }

        // Calculate selection bounding box for snap-to-elements
        HideSnapGuidelines();
        if (snapToElements && _selectedElements.Count > 1)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var kvp in _dragInitialPositions)
            {
                UIElement elem = kvp.Key;
                Point initialPos = kvp.Value;

                // Calculate where this element will be after applying offset
                double elemLeft = initialPos.X + snappedOffset.X;
                double elemTop = initialPos.Y + snappedOffset.Y;

                if (elem is Line line)
                {
                    Point initialEnd = _dragInitialEndPositions.GetValueOrDefault(elem, new Point(0, 0));
                    double elemRight = initialEnd.X + snappedOffset.X;
                    double elemBottom = initialEnd.Y + snappedOffset.Y;

                    minX = Math.Min(minX, Math.Min(elemLeft, elemRight));
                    minY = Math.Min(minY, Math.Min(elemTop, elemBottom));
                    maxX = Math.Max(maxX, Math.Max(elemLeft, elemRight));
                    maxY = Math.Max(maxY, Math.Max(elemTop, elemBottom));
                }
                else
                {
                    var bounds = GetElementBounds(elem);
                    if (bounds != Rect.Empty)
                    {
                        minX = Math.Min(minX, elemLeft);
                        minY = Math.Min(minY, elemTop);
                        maxX = Math.Max(maxX, elemLeft + bounds.Width);
                        maxY = Math.Max(maxY, elemTop + bounds.Height);
                    }
                }
            }

            if (minX < double.MaxValue)
            {
                double selWidth = maxX - minX;
                double selHeight = maxY - minY;

                var (snappedLeft, snappedTop, guideX, guideY) = CalculateElementSnapForSelection(
                    minX, minY, selWidth, selHeight);

                // Calculate snap delta and apply to offset
                double snapDeltaX = snappedLeft - minX;
                double snapDeltaY = snappedTop - minY;
                snappedOffset = new Vector(snappedOffset.X + snapDeltaX, snappedOffset.Y + snapDeltaY);

                if (guideX.HasValue) ShowVerticalSnapGuideline(guideX.Value);
                if (guideY.HasValue) ShowHorizontalSnapGuideline(guideY.Value);
            }
        }

        foreach (var kvp in _dragInitialPositions)
        {
            UIElement element = kvp.Key;
            Point initialPos = kvp.Value;

            if (element is Line line)
            {
                Point initialEnd = _dragInitialEndPositions.GetValueOrDefault(element, new Point(0, 0));

                line.X1 = initialPos.X + snappedOffset.X;
                line.Y1 = initialPos.Y + snappedOffset.Y;
                line.X2 = initialEnd.X + snappedOffset.X;
                line.Y2 = initialEnd.Y + snappedOffset.Y;

                // Update adorner
                UpdateElementAdorner(line);
            }
            else
            {
                double newLeft = initialPos.X + snappedOffset.X;
                double newTop = initialPos.Y + snappedOffset.Y;
                Canvas.SetLeft(element, newLeft);
                Canvas.SetTop(element, newTop);
            }
        }
    }

    /// <summary>
    /// Updates the adorner for a specific element (invalidates layout).
    /// </summary>
    private void UpdateElementAdorner(UIElement element)
    {
        if (_selectedElements.Contains(element))
        {
            var layer = AdornerLayer.GetAdornerLayer(element);
            if (layer != null)
            {
                var adorners = layer.GetAdorners(element);
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
    }

    private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDragging)
            return;

        // Check if we moved multiple elements
        if (_dragInitialPositions.Count > 1)
        {
            // Create commands for all moved elements
            CreateMoveCommandsForMultipleElements();
        }
        else if (draggedElement != null)
        {
            // Single element - existing logic
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

        // IMPORTANT: Set isDragging = false BEFORE clearing positions and releasing capture
        // to prevent stray MouseMove events from taking the SINGLE element path
        isDragging = false;
        _dragInitialPositions.Clear();
        _dragInitialEndPositions.Clear();
        draggedElement?.ReleaseMouseCapture();
        draggedElement = null;

        // Hide snap guidelines after drag ends
        HideSnapGuidelines();

        e.Handled = true;
    }

    /// <summary>
    /// Creates move commands for all elements that were dragged in a multi-selection.
    /// Elements are already at their final positions - we just record commands for undo/redo.
    /// </summary>
    private void CreateMoveCommandsForMultipleElements()
    {
        bool anyMoved = false;

        foreach (var kvp in _dragInitialPositions)
        {
            UIElement element = kvp.Key;
            Point initialPos = kvp.Value;

            if (element is Line line)
            {
                // Line element - get current (final) position
                Point finalPos = new Point(line.X1, line.Y1);
                Point initialEnd = _dragInitialEndPositions.GetValueOrDefault(element, new Point(0, 0));
                Point finalEnd = new Point(line.X2, line.Y2);

                if (initialPos != finalPos || initialEnd != finalEnd)
                {
                    // Element already at final position - just add command for undo/redo
                    commandManager.AddExecutedCommand(new ResizeLineCommand(line, initialPos, initialEnd, finalPos, finalEnd));
                    anyMoved = true;
                }
            }
            else
            {
                // Regular element - get current (final) position
                double finalLeft = Canvas.GetLeft(element);
                double finalTop = Canvas.GetTop(element);
                if (double.IsNaN(finalLeft)) finalLeft = 0;
                if (double.IsNaN(finalTop)) finalTop = 0;
                Point finalPos = new Point(finalLeft, finalTop);

                if (initialPos != finalPos)
                {
                    // Element already at final position - just add command for undo/redo
                    commandManager.AddExecutedCommand(new MoveElementCommand(element, initialPos, finalPos));
                    anyMoved = true;

                    // Update CanvasElement coordinates for Line/Arrow if needed
                    UpdateLineArrowCanvasElement(element);
                }
            }
        }

        if (anyMoved)
        {
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
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
        // Clear Layers panel
        Layers.Clear();
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

        // Sync Layers panel from loaded Canvas
        SyncLayersFromCanvas();

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

        // Remove from Layers panel
        RemoveLayerForElement(elementToDelete);

        commandManager.ExecuteCommand(new DeleteElementCommand(elementToDelete, designCanvas, index));
        UpdateUndoRedoButtons();
        isDirty = true;
    }

    // Alignment commands
    private void AlignLeft_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: align to leftmost element's left edge
            double minLeft = double.MaxValue;
            foreach (var element in _selectedElements)
            {
                var bounds = GetElementBounds(element);
                if (bounds.Left < minLeft) minLeft = bounds.Left;
            }
            AlignElementsTo(el => (minLeft, null));
        }
        else if (selectedElement != null)
        {
            // Single element: align to canvas edge
            MoveElementTo(selectedElement, 0, null);
        }
    }

    private void AlignRight_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: align to rightmost element's right edge
            double maxRight = double.MinValue;
            foreach (var element in _selectedElements)
            {
                var bounds = GetElementBounds(element);
                if (bounds.Right > maxRight) maxRight = bounds.Right;
            }
            AlignElementsTo(el =>
            {
                var bounds = GetElementBounds(el);
                return (maxRight - bounds.Width, null);
            });
        }
        else if (selectedElement != null && selectedElement is FrameworkElement fe)
        {
            MoveElementTo(selectedElement, designCanvas.ActualWidth - fe.ActualWidth, null);
        }
    }

    private void AlignTop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: align to topmost element's top edge
            double minTop = double.MaxValue;
            foreach (var element in _selectedElements)
            {
                var bounds = GetElementBounds(element);
                if (bounds.Top < minTop) minTop = bounds.Top;
            }
            AlignElementsTo(el => (null, minTop));
        }
        else if (selectedElement != null)
        {
            MoveElementTo(selectedElement, null, 0);
        }
    }

    private void AlignBottom_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: align to bottommost element's bottom edge
            double maxBottom = double.MinValue;
            foreach (var element in _selectedElements)
            {
                var bounds = GetElementBounds(element);
                if (bounds.Bottom > maxBottom) maxBottom = bounds.Bottom;
            }
            AlignElementsTo(el =>
            {
                var bounds = GetElementBounds(el);
                return (null, maxBottom - bounds.Height);
            });
        }
        else if (selectedElement != null && selectedElement is FrameworkElement fe)
        {
            MoveElementTo(selectedElement, null, designCanvas.ActualHeight - fe.ActualHeight);
        }
    }

    private void AlignCenterH_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: align to horizontal center of bounding box
            double minLeft = double.MaxValue;
            double maxRight = double.MinValue;
            foreach (var element in _selectedElements)
            {
                var bounds = GetElementBounds(element);
                if (bounds.Left < minLeft) minLeft = bounds.Left;
                if (bounds.Right > maxRight) maxRight = bounds.Right;
            }
            double centerX = (minLeft + maxRight) / 2;
            AlignElementsTo(el =>
            {
                var bounds = GetElementBounds(el);
                return (centerX - bounds.Width / 2, null);
            });
        }
        else if (selectedElement != null && selectedElement is FrameworkElement fe)
        {
            MoveElementTo(selectedElement, (designCanvas.ActualWidth - fe.ActualWidth) / 2, null);
        }
    }

    private void AlignCenterV_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: align to vertical center of bounding box
            double minTop = double.MaxValue;
            double maxBottom = double.MinValue;
            foreach (var element in _selectedElements)
            {
                var bounds = GetElementBounds(element);
                if (bounds.Top < minTop) minTop = bounds.Top;
                if (bounds.Bottom > maxBottom) maxBottom = bounds.Bottom;
            }
            double centerY = (minTop + maxBottom) / 2;
            AlignElementsTo(el =>
            {
                var bounds = GetElementBounds(el);
                return (null, centerY - bounds.Height / 2);
            });
        }
        else if (selectedElement != null && selectedElement is FrameworkElement fe)
        {
            MoveElementTo(selectedElement, null, (designCanvas.ActualHeight - fe.ActualHeight) / 2);
        }
    }

    private void AlignCenter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count > 1)
        {
            // Multi-selection: center all elements on the canvas
            AlignElementsTo(el =>
            {
                if (el is FrameworkElement fe)
                    return ((designCanvas.ActualWidth - fe.ActualWidth) / 2, (designCanvas.ActualHeight - fe.ActualHeight) / 2);
                return (null, null);
            });
        }
        else if (selectedElement != null && selectedElement is FrameworkElement fe)
        {
            MoveElementTo(selectedElement,
                (designCanvas.ActualWidth - fe.ActualWidth) / 2,
                (designCanvas.ActualHeight - fe.ActualHeight) / 2);
        }
    }

    /// <summary>
    /// Aligns all selected elements using a position calculator function.
    /// </summary>
    /// <param name="positionCalculator">Function that takes an element and returns (newX, newY) - null means keep current</param>
    private void AlignElementsTo(Func<UIElement, (double? x, double? y)> positionCalculator)
    {
        if (_selectedElements.Count == 0) return;

        var moves = new List<Commands.ICommand>();

        foreach (var element in _selectedElements)
        {
            var bounds = GetElementBounds(element);
            double oldX = bounds.Left;
            double oldY = bounds.Top;

            var (newX, newY) = positionCalculator(element);
            double finalX = newX ?? oldX;
            double finalY = newY ?? oldY;

            if (Math.Abs(finalX - oldX) > 0.01 || Math.Abs(finalY - oldY) > 0.01)
            {
                Canvas.SetLeft(element, finalX);
                Canvas.SetTop(element, finalY);
                moves.Add(new MoveElementCommand(element, new Point(oldX, oldY), new Point(finalX, finalY)));
            }
        }

        if (moves.Count > 0)
        {
            commandManager.AddExecutedCommand(new CompositeCommand(moves));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    // Distribute commands
    private void DistributeHorizontal_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count < 3) return;

        // Sort elements by their left edge position
        var sortedElements = _selectedElements
            .Select(el => (Element: el, Bounds: GetElementBounds(el)))
            .OrderBy(x => x.Bounds.Left)
            .ToList();

        // Calculate spacing based on element centers
        double firstCenter = sortedElements.First().Bounds.Left + sortedElements.First().Bounds.Width / 2;
        double lastCenter = sortedElements.Last().Bounds.Left + sortedElements.Last().Bounds.Width / 2;
        double totalSpan = lastCenter - firstCenter;
        double spacing = totalSpan / (sortedElements.Count - 1);

        var moves = new List<Commands.ICommand>();

        // First and last elements stay in place, distribute middle elements
        for (int i = 1; i < sortedElements.Count - 1; i++)
        {
            var item = sortedElements[i];
            double oldX = item.Bounds.Left;
            double oldY = item.Bounds.Top;
            double newCenterX = firstCenter + (i * spacing);
            double newLeft = newCenterX - item.Bounds.Width / 2;

            if (Math.Abs(newLeft - oldX) > 0.01)
            {
                Canvas.SetLeft(item.Element, newLeft);
                moves.Add(new MoveElementCommand(item.Element, new Point(oldX, oldY), new Point(newLeft, oldY)));
            }
        }

        if (moves.Count > 0)
        {
            commandManager.AddExecutedCommand(new CompositeCommand(moves));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
    }

    private void DistributeVertical_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElements.Count < 3) return;

        // Sort elements by their top edge position
        var sortedElements = _selectedElements
            .Select(el => (Element: el, Bounds: GetElementBounds(el)))
            .OrderBy(x => x.Bounds.Top)
            .ToList();

        // Calculate spacing based on element centers
        double firstCenter = sortedElements.First().Bounds.Top + sortedElements.First().Bounds.Height / 2;
        double lastCenter = sortedElements.Last().Bounds.Top + sortedElements.Last().Bounds.Height / 2;
        double totalSpan = lastCenter - firstCenter;
        double spacing = totalSpan / (sortedElements.Count - 1);

        var moves = new List<Commands.ICommand>();

        // First and last elements stay in place, distribute middle elements
        for (int i = 1; i < sortedElements.Count - 1; i++)
        {
            var item = sortedElements[i];
            double oldX = item.Bounds.Left;
            double oldY = item.Bounds.Top;
            double newCenterY = firstCenter + (i * spacing);
            double newTop = newCenterY - item.Bounds.Height / 2;

            if (Math.Abs(newTop - oldY) > 0.01)
            {
                Canvas.SetTop(item.Element, newTop);
                moves.Add(new MoveElementCommand(item.Element, new Point(oldX, oldY), new Point(oldX, newTop)));
            }
        }

        if (moves.Count > 0)
        {
            commandManager.AddExecutedCommand(new CompositeCommand(moves));
            UpdateUndoRedoButtons();
            UpdatePropertiesPanel();
            isDirty = true;
        }
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

    private void SnapToElements_Click(object sender, RoutedEventArgs e)
    {
        snapToElements = !snapToElements;
        menuSnapToElements.IsChecked = snapToElements;
    }

    #region Snap-to-Element Guidelines

    /// <summary>
    /// Initializes the snap-to-element guideline Line objects.
    /// </summary>
    private void InitializeSnapGuidelines()
    {
        _verticalSnapGuideline = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 0, 128)), // Magenta
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_verticalSnapGuideline, int.MaxValue);
        designCanvas.Children.Add(_verticalSnapGuideline);

        _horizontalSnapGuideline = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 0, 128)), // Magenta
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_horizontalSnapGuideline, int.MaxValue);
        designCanvas.Children.Add(_horizontalSnapGuideline);
    }

    /// <summary>
    /// Shows a vertical snap guideline at the specified X coordinate.
    /// </summary>
    private void ShowVerticalSnapGuideline(double x)
    {
        if (_verticalSnapGuideline == null) return;
        _verticalSnapGuideline.X1 = x;
        _verticalSnapGuideline.X2 = x;
        _verticalSnapGuideline.Y1 = 0;
        _verticalSnapGuideline.Y2 = designCanvas.ActualHeight;
        _verticalSnapGuideline.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows a horizontal snap guideline at the specified Y coordinate.
    /// </summary>
    private void ShowHorizontalSnapGuideline(double y)
    {
        if (_horizontalSnapGuideline == null) return;
        _horizontalSnapGuideline.X1 = 0;
        _horizontalSnapGuideline.X2 = designCanvas.ActualWidth;
        _horizontalSnapGuideline.Y1 = y;
        _horizontalSnapGuideline.Y2 = y;
        _horizontalSnapGuideline.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hides all snap guidelines.
    /// </summary>
    private void HideSnapGuidelines()
    {
        if (_verticalSnapGuideline != null)
            _verticalSnapGuideline.Visibility = Visibility.Collapsed;
        if (_horizontalSnapGuideline != null)
            _horizontalSnapGuideline.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Gets all snap points (X and Y coordinates) from elements, excluding the specified element and selected elements.
    /// </summary>
    private (List<double> xPoints, List<double> yPoints) GetAllSnapPoints(UIElement excludeElement)
    {
        var xPoints = new List<double>();
        var yPoints = new List<double>();

        foreach (UIElement child in designCanvas.Children)
        {
            if (child == excludeElement) continue;
            if (child == _verticalSnapGuideline || child == _horizontalSnapGuideline) continue;
            if (_selectedElements.Contains(child)) continue;

            var bounds = GetElementBounds(child);
            if (bounds == Rect.Empty) continue;

            // X-Snap-Punkte (vertikale Linien)
            xPoints.Add(bounds.Left);
            xPoints.Add(bounds.Left + bounds.Width / 2);
            xPoints.Add(bounds.Right);

            // Y-Snap-Punkte (horizontale Linien)
            yPoints.Add(bounds.Top);
            yPoints.Add(bounds.Top + bounds.Height / 2);
            yPoints.Add(bounds.Bottom);
        }

        return (xPoints, yPoints);
    }

    /// <summary>
    /// Gets all snap points excluding all currently selected elements.
    /// </summary>
    private (List<double> xPoints, List<double> yPoints) GetAllSnapPointsExcludingSelection()
    {
        var xPoints = new List<double>();
        var yPoints = new List<double>();

        foreach (UIElement child in designCanvas.Children)
        {
            if (_selectedElements.Contains(child)) continue;
            if (child == _verticalSnapGuideline || child == _horizontalSnapGuideline) continue;

            var bounds = GetElementBounds(child);
            if (bounds == Rect.Empty) continue;

            xPoints.Add(bounds.Left);
            xPoints.Add(bounds.Left + bounds.Width / 2);
            xPoints.Add(bounds.Right);

            yPoints.Add(bounds.Top);
            yPoints.Add(bounds.Top + bounds.Height / 2);
            yPoints.Add(bounds.Bottom);
        }

        return (xPoints, yPoints);
    }

    /// <summary>
    /// Finds the nearest snap point within SNAP_THRESHOLD_PIXELS distance.
    /// </summary>
    private double? FindNearestSnapPoint(double position, List<double> snapPoints)
    {
        double closestDistance = double.MaxValue;
        double? closestSnapPoint = null;

        foreach (var snapPoint in snapPoints)
        {
            double distance = Math.Abs(position - snapPoint);
            if (distance < SNAP_THRESHOLD_PIXELS && distance < closestDistance)
            {
                closestDistance = distance;
                closestSnapPoint = snapPoint;
            }
        }

        return closestSnapPoint;
    }

    /// <summary>
    /// Calculates snap position for an element based on nearby elements.
    /// Returns adjusted position and guideline positions.
    /// </summary>
    private (double newLeft, double newTop, double? guideX, double? guideY) CalculateElementSnap(
        double currentLeft, double currentTop, double width, double height, UIElement excludeElement)
    {
        if (!snapToElements)
            return (currentLeft, currentTop, null, null);

        var (xPoints, yPoints) = GetAllSnapPoints(excludeElement);

        double newLeft = currentLeft;
        double newTop = currentTop;
        double? guideX = null;
        double? guideY = null;

        // Element-Snap-Punkte
        double elemLeft = currentLeft;
        double elemCenterX = currentLeft + width / 2;
        double elemRight = currentLeft + width;

        double elemTop = currentTop;
        double elemCenterY = currentTop + height / 2;
        double elemBottom = currentTop + height;

        // X-Snap prüfen (linke Kante, Mitte, rechte Kante)
        var snapLeft = FindNearestSnapPoint(elemLeft, xPoints);
        var snapCenterX = FindNearestSnapPoint(elemCenterX, xPoints);
        var snapRight = FindNearestSnapPoint(elemRight, xPoints);

        // Nächsten X-Snap wählen
        double bestXDistance = double.MaxValue;

        if (snapLeft.HasValue)
        {
            double dist = Math.Abs(elemLeft - snapLeft.Value);
            if (dist < bestXDistance) { bestXDistance = dist; newLeft = snapLeft.Value; guideX = snapLeft.Value; }
        }
        if (snapCenterX.HasValue)
        {
            double dist = Math.Abs(elemCenterX - snapCenterX.Value);
            if (dist < bestXDistance) { bestXDistance = dist; newLeft = snapCenterX.Value - width / 2; guideX = snapCenterX.Value; }
        }
        if (snapRight.HasValue)
        {
            double dist = Math.Abs(elemRight - snapRight.Value);
            if (dist < bestXDistance) { bestXDistance = dist; newLeft = snapRight.Value - width; guideX = snapRight.Value; }
        }

        // Y-Snap prüfen (obere Kante, Mitte, untere Kante)
        var snapTop = FindNearestSnapPoint(elemTop, yPoints);
        var snapCenterY = FindNearestSnapPoint(elemCenterY, yPoints);
        var snapBottom = FindNearestSnapPoint(elemBottom, yPoints);

        // Nächsten Y-Snap wählen
        double bestYDistance = double.MaxValue;

        if (snapTop.HasValue)
        {
            double dist = Math.Abs(elemTop - snapTop.Value);
            if (dist < bestYDistance) { bestYDistance = dist; newTop = snapTop.Value; guideY = snapTop.Value; }
        }
        if (snapCenterY.HasValue)
        {
            double dist = Math.Abs(elemCenterY - snapCenterY.Value);
            if (dist < bestYDistance) { bestYDistance = dist; newTop = snapCenterY.Value - height / 2; guideY = snapCenterY.Value; }
        }
        if (snapBottom.HasValue)
        {
            double dist = Math.Abs(elemBottom - snapBottom.Value);
            if (dist < bestYDistance) { bestYDistance = dist; newTop = snapBottom.Value - height; guideY = snapBottom.Value; }
        }

        return (newLeft, newTop, guideX, guideY);
    }

    /// <summary>
    /// Calculates snap position for a selection bounding box.
    /// </summary>
    private (double newLeft, double newTop, double? guideX, double? guideY) CalculateElementSnapForSelection(
        double currentLeft, double currentTop, double width, double height)
    {
        if (!snapToElements)
            return (currentLeft, currentTop, null, null);

        var (xPoints, yPoints) = GetAllSnapPointsExcludingSelection();

        double newLeft = currentLeft;
        double newTop = currentTop;
        double? guideX = null;
        double? guideY = null;

        // Selection Bounding-Box Snap-Punkte
        double selLeft = currentLeft;
        double selCenterX = currentLeft + width / 2;
        double selRight = currentLeft + width;

        double selTop = currentTop;
        double selCenterY = currentTop + height / 2;
        double selBottom = currentTop + height;

        // X-Snap prüfen
        var snapLeft = FindNearestSnapPoint(selLeft, xPoints);
        var snapCenterX = FindNearestSnapPoint(selCenterX, xPoints);
        var snapRight = FindNearestSnapPoint(selRight, xPoints);

        double bestXDistance = double.MaxValue;

        if (snapLeft.HasValue)
        {
            double dist = Math.Abs(selLeft - snapLeft.Value);
            if (dist < bestXDistance) { bestXDistance = dist; newLeft = snapLeft.Value; guideX = snapLeft.Value; }
        }
        if (snapCenterX.HasValue)
        {
            double dist = Math.Abs(selCenterX - snapCenterX.Value);
            if (dist < bestXDistance) { bestXDistance = dist; newLeft = snapCenterX.Value - width / 2; guideX = snapCenterX.Value; }
        }
        if (snapRight.HasValue)
        {
            double dist = Math.Abs(selRight - snapRight.Value);
            if (dist < bestXDistance) { bestXDistance = dist; newLeft = snapRight.Value - width; guideX = snapRight.Value; }
        }

        // Y-Snap prüfen
        var snapTop = FindNearestSnapPoint(selTop, yPoints);
        var snapCenterY = FindNearestSnapPoint(selCenterY, yPoints);
        var snapBottom = FindNearestSnapPoint(selBottom, yPoints);

        double bestYDistance = double.MaxValue;

        if (snapTop.HasValue)
        {
            double dist = Math.Abs(selTop - snapTop.Value);
            if (dist < bestYDistance) { bestYDistance = dist; newTop = snapTop.Value; guideY = snapTop.Value; }
        }
        if (snapCenterY.HasValue)
        {
            double dist = Math.Abs(selCenterY - snapCenterY.Value);
            if (dist < bestYDistance) { bestYDistance = dist; newTop = snapCenterY.Value - height / 2; guideY = snapCenterY.Value; }
        }
        if (snapBottom.HasValue)
        {
            double dist = Math.Abs(selBottom - snapBottom.Value);
            if (dist < bestYDistance) { bestYDistance = dist; newTop = snapBottom.Value - height; guideY = snapBottom.Value; }
        }

        return (newLeft, newTop, guideX, guideY);
    }

    #endregion

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