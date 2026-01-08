using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Element control for Image elements with source management and monochrome filter properties.
/// Uses bounds-based resize inherited from ElementControlBase.
/// </summary>
public class ImageControl : ElementControlBase
{
    private readonly Image _image;
    private BitmapSource? _originalSource;

    /// <summary>
    /// Gets the type of element this control manages.
    /// </summary>
    public override ElementType ElementType => ElementType.Image;

    // UsesEndpoints inherited as false - images use bounds-based resize

    /// <summary>
    /// Gets the underlying Image element being controlled.
    /// </summary>
    public Image Image => _image;

    /// <summary>
    /// Gets or sets the original unfiltered image source.
    /// Used for reapplying filters without degradation.
    /// </summary>
    public BitmapSource? OriginalSource
    {
        get => _originalSource;
        set => _originalSource = value;
    }

    /// <summary>
    /// Initializes a new instance of the ImageControl class.
    /// </summary>
    /// <param name="image">The Image element to control.</param>
    /// <param name="canvasElement">The data model for serialization.</param>
    /// <param name="originalSource">Optional original BitmapSource for filter reapplication.</param>
    /// <param name="mainWindow">Optional reference to the MainWindow for property panel access.</param>
    /// <exception cref="ArgumentNullException">Thrown when image is null.</exception>
    public ImageControl(Image image, CanvasElement canvasElement, BitmapSource? originalSource = null, MainWindow? mainWindow = null)
        : base(image, canvasElement, mainWindow)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _originalSource = originalSource;
    }

    #region Image Property Helpers

    /// <summary>
    /// Gets the current image source (may be filtered).
    /// </summary>
    public ImageSource? GetSource() => _image.Source;

    /// <summary>
    /// Sets the image source (typically a filtered version).
    /// </summary>
    public void SetSource(ImageSource? source)
    {
        _image.Source = source;
    }

    /// <summary>
    /// Gets the image path from CanvasElement.
    /// </summary>
    public string? GetImagePath() => _canvasElement.ImagePath;

    /// <summary>
    /// Gets whether monochrome filter is enabled.
    /// </summary>
    public bool GetMonochromeEnabled() => _canvasElement.MonochromeEnabled ?? false;

    /// <summary>
    /// Sets whether monochrome filter is enabled.
    /// </summary>
    public void SetMonochromeEnabled(bool enabled)
    {
        _canvasElement.MonochromeEnabled = enabled;
    }

    /// <summary>
    /// Gets the monochrome threshold (0-255).
    /// </summary>
    public byte GetThreshold() => _canvasElement.Threshold ?? 128;

    /// <summary>
    /// Sets the monochrome threshold.
    /// </summary>
    public void SetThreshold(byte threshold)
    {
        _canvasElement.Threshold = threshold;
    }

    /// <summary>
    /// Gets the monochrome algorithm name.
    /// </summary>
    public string GetMonochromeAlgorithm() => _canvasElement.MonochromeAlgorithm ?? "Threshold";

    /// <summary>
    /// Sets the monochrome algorithm.
    /// </summary>
    public void SetMonochromeAlgorithm(string algorithm)
    {
        _canvasElement.MonochromeAlgorithm = algorithm;
    }

    /// <summary>
    /// Gets whether colors are inverted.
    /// </summary>
    public bool GetInvertColors() => _canvasElement.InvertColors ?? false;

    /// <summary>
    /// Sets whether colors are inverted.
    /// </summary>
    public void SetInvertColors(bool invert)
    {
        _canvasElement.InvertColors = invert;
    }

    /// <summary>
    /// Gets the brightness adjustment (-100 to 100).
    /// </summary>
    public double GetBrightness() => _canvasElement.Brightness ?? 0;

    /// <summary>
    /// Sets the brightness adjustment.
    /// </summary>
    public void SetBrightness(double brightness)
    {
        _canvasElement.Brightness = Math.Clamp(brightness, -100, 100);
    }

    /// <summary>
    /// Gets the contrast adjustment (-100 to 100).
    /// </summary>
    public double GetContrast() => _canvasElement.Contrast ?? 0;

    /// <summary>
    /// Sets the contrast adjustment.
    /// </summary>
    public void SetContrast(double contrast)
    {
        _canvasElement.Contrast = Math.Clamp(contrast, -100, 100);
    }

    /// <summary>
    /// Gets the bounds of the image element.
    /// </summary>
    public Rect GetBounds()
    {
        return new Rect(
            GetCanvasLeft(),
            GetCanvasTop(),
            _image.Width,
            _image.Height);
    }

    /// <summary>
    /// Updates CanvasElement from current image position and size.
    /// </summary>
    public void SyncCanvasElement()
    {
        _canvasElement.X = GetCanvasLeft();
        _canvasElement.Y = GetCanvasTop();
        _canvasElement.Width = _image.Width;
        _canvasElement.Height = _image.Height;
    }

    #endregion

    #region Resize Handling

    /// <summary>
    /// Handle resize operations for bounds-based image elements.
    /// Uses base class implementation then syncs CanvasElement.
    /// </summary>
    /// <param name="handle">The resize handle being dragged.</param>
    /// <param name="horizontalChange">The horizontal drag delta.</param>
    /// <param name="verticalChange">The vertical drag delta.</param>
    public override void HandleResize(ResizeHandle handle, double horizontalChange, double verticalChange)
    {
        // Base class handles all bounds-based resize logic
        base.HandleResize(handle, horizontalChange, verticalChange);

        // Sync CanvasElement with new position/size for serialization
        SyncCanvasElement();
    }

    #endregion

    #region IElementControl Implementation

    /// <summary>
    /// Update the properties panel with this element's values.
    /// </summary>
    /// <remarks>
    /// Populates Image-specific properties: monochrome filter settings including
    /// enabled state, algorithm, threshold, brightness, contrast, and invert.
    /// </remarks>
    public override void PopulatePropertiesPanel()
    {
        if (_mainWindow == null)
            return;

        // Show image filters group, hide others
        _mainWindow.groupTextFormatting.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupShapeStyling.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupImageFilters.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.groupArrowControls.Visibility = System.Windows.Visibility.Collapsed;

        // Hide X2, Y2 controls (not applicable for images)
        _mainWindow.labelX2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyX2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.labelY2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyY2.Visibility = System.Windows.Visibility.Collapsed;

        // Enable and populate image filter controls
        bool monochromeEnabled = _canvasElement.MonochromeEnabled ?? false;

        _mainWindow.propertyMonochromeEnabled.IsChecked = monochromeEnabled;
        _mainWindow.panelMonochromeControls.Visibility = monochromeEnabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // Algorithm selection
        string algorithm = _canvasElement.MonochromeAlgorithm ?? "Threshold";
        _mainWindow.propertyAlgorithm.SelectedIndex = algorithm switch
        {
            "FloydSteinberg" => 1,
            "Ordered" => 2,
            "Atkinson" => 3,
            _ => 0
        };

        // Threshold
        _mainWindow.propertyThresholdSlider.Value = _canvasElement.Threshold ?? 128;
        _mainWindow.propertyThresholdValue.Text = (_canvasElement.Threshold ?? 128).ToString();

        // Brightness
        _mainWindow.propertyBrightness.Value = _canvasElement.Brightness ?? 0;
        _mainWindow.propertyBrightnessValue.Text = (_canvasElement.Brightness ?? 0).ToString("F0");

        // Contrast
        _mainWindow.propertyContrast.Value = _canvasElement.Contrast ?? 0;
        _mainWindow.propertyContrastValue.Text = (_canvasElement.Contrast ?? 0).ToString("F0");

        // Invert
        _mainWindow.propertyInvert.IsChecked = _canvasElement.InvertColors ?? false;

        // Enable controls
        _mainWindow.propertyMonochromeEnabled.IsEnabled = true;
        _mainWindow.propertyAlgorithm.IsEnabled = true;
        _mainWindow.propertyThresholdSlider.IsEnabled = true;
        _mainWindow.propertyBrightness.IsEnabled = true;
        _mainWindow.propertyContrast.IsEnabled = true;
        _mainWindow.propertyInvert.IsEnabled = true;
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
