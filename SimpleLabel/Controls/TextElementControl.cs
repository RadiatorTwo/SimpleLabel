using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Element control for TextBlock text elements.
/// Uses bounds-based resize inherited from ElementControlBase.
/// </summary>
public class TextElementControl : ElementControlBase
{
    private readonly TextBlock _textBlock;

    /// <summary>
    /// Gets the type of element this control manages.
    /// </summary>
    public override ElementType ElementType => ElementType.Text;

    /// <summary>
    /// Gets the underlying TextBlock being controlled.
    /// </summary>
    public TextBlock TextBlock => _textBlock;

    /// <summary>
    /// Initializes a new instance of the TextElementControl class.
    /// </summary>
    /// <param name="textBlock">The TextBlock to control.</param>
    /// <param name="canvasElement">The data model for serialization.</param>
    /// <param name="mainWindow">Optional reference to the MainWindow for property panel access.</param>
    /// <exception cref="ArgumentNullException">Thrown when textBlock is null.</exception>
    public TextElementControl(TextBlock textBlock, CanvasElement canvasElement, MainWindow? mainWindow = null)
        : base(textBlock, canvasElement, mainWindow)
    {
        _textBlock = textBlock ?? throw new ArgumentNullException(nameof(textBlock));
    }

    #region Text Property Helpers

    /// <summary>
    /// Gets the text content.
    /// </summary>
    /// <returns>The current text content.</returns>
    public string GetText() => _textBlock.Text;

    /// <summary>
    /// Sets the text content and updates CanvasElement.
    /// </summary>
    /// <param name="text">The text to set.</param>
    public void SetText(string text)
    {
        _textBlock.Text = text;
        _canvasElement.Text = text;
    }

    /// <summary>
    /// Gets the font size.
    /// </summary>
    /// <returns>The current font size.</returns>
    public double GetFontSize() => _textBlock.FontSize;

    /// <summary>
    /// Sets the font size and updates CanvasElement.
    /// </summary>
    /// <param name="size">The font size to set.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when size is not positive.</exception>
    public void SetFontSize(double size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Font size must be positive");
        _textBlock.FontSize = size;
        _canvasElement.FontSize = size;
    }

    /// <summary>
    /// Gets the font family.
    /// </summary>
    /// <returns>The current font family.</returns>
    public FontFamily GetFontFamily() => _textBlock.FontFamily;

    /// <summary>
    /// Sets the font family and updates CanvasElement.
    /// </summary>
    /// <param name="family">The font family to set.</param>
    public void SetFontFamily(FontFamily family)
    {
        _textBlock.FontFamily = family;
        _canvasElement.FontFamily = family.Source;
    }

    /// <summary>
    /// Gets the foreground brush.
    /// </summary>
    /// <returns>The current foreground brush, or null if not set.</returns>
    public Brush? GetForeground() => _textBlock.Foreground;

    /// <summary>
    /// Sets the foreground brush.
    /// </summary>
    /// <param name="brush">The brush to set.</param>
    public void SetForeground(Brush? brush)
    {
        _textBlock.Foreground = brush;
        // Note: CanvasElement color sync in Phase 6
    }

    /// <summary>
    /// Gets the font weight.
    /// </summary>
    /// <returns>The current font weight.</returns>
    public FontWeight GetFontWeight() => _textBlock.FontWeight;

    /// <summary>
    /// Sets the font weight and updates CanvasElement.
    /// </summary>
    /// <param name="weight">The font weight to set.</param>
    public void SetFontWeight(FontWeight weight)
    {
        _textBlock.FontWeight = weight;
        _canvasElement.FontWeight = weight.ToString();
    }

    /// <summary>
    /// Gets the font style.
    /// </summary>
    /// <returns>The current font style.</returns>
    public FontStyle GetFontStyle() => _textBlock.FontStyle;

    /// <summary>
    /// Sets the font style and updates CanvasElement.
    /// </summary>
    /// <param name="style">The font style to set.</param>
    public void SetFontStyle(FontStyle style)
    {
        _textBlock.FontStyle = style;
        _canvasElement.FontStyle = style.ToString();
    }

    /// <summary>
    /// Gets the text alignment.
    /// </summary>
    /// <returns>The current text alignment.</returns>
    public TextAlignment GetTextAlignment() => _textBlock.TextAlignment;

    /// <summary>
    /// Sets the text alignment and updates CanvasElement.
    /// </summary>
    /// <param name="alignment">The text alignment to set.</param>
    public void SetTextAlignment(TextAlignment alignment)
    {
        _textBlock.TextAlignment = alignment;
        _canvasElement.TextAlignment = alignment.ToString();
    }

    /// <summary>
    /// Gets the bounds of the text element (position and size).
    /// </summary>
    /// <returns>A Rect containing the position and dimensions.</returns>
    public Rect GetBounds()
    {
        return new Rect(
            GetCanvasLeft(),
            GetCanvasTop(),
            _textBlock.Width,
            _textBlock.Height);
    }

    /// <summary>
    /// Updates CanvasElement from current text element position and size.
    /// Call after resize operations to sync the data model.
    /// </summary>
    public void SyncCanvasElement()
    {
        _canvasElement.X = GetCanvasLeft();
        _canvasElement.Y = GetCanvasTop();
        _canvasElement.Width = _textBlock.Width;
        _canvasElement.Height = _textBlock.Height;
    }

    #endregion

    #region Resize Handling

    /// <summary>
    /// Handle resize operations for bounds-based text elements.
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
    /// Populates Text-specific properties: text content, font family, font size,
    /// foreground color, alignment, bold, and italic settings.
    /// </remarks>
    public override void PopulatePropertiesPanel()
    {
        if (_mainWindow == null)
            return;

        // Show text formatting group, hide others
        _mainWindow.groupTextFormatting.Visibility = System.Windows.Visibility.Visible;
        _mainWindow.groupShapeStyling.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupImageFilters.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.groupArrowControls.Visibility = System.Windows.Visibility.Collapsed;

        // Hide X2, Y2 controls (not applicable for text)
        _mainWindow.labelX2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyX2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.labelY2.Visibility = System.Windows.Visibility.Collapsed;
        _mainWindow.propertyY2.Visibility = System.Windows.Visibility.Collapsed;

        // Enable and populate text formatting controls
        _mainWindow.propertyText.Text = _textBlock.Text;
        _mainWindow.propertyFontFamily.SelectedItem = _textBlock.FontFamily.Source;

        // Find the closest matching font size item
        double currentFontSize = _textBlock.FontSize;
        System.Windows.Controls.ComboBoxItem? bestMatch = null;
        double smallestDifference = double.MaxValue;

        foreach (var item in _mainWindow.propertyFontSize.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag != null)
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
            _mainWindow.propertyFontSize.SelectedItem = bestMatch;
        }
        else
        {
            // No close match, calculate and show mm equivalent
            // Approximate actual character height: pt * 0.265 approximately equals mm
            // (accounts for cap height being ~70% of font size)
            double mm = currentFontSize * 0.265;
            _mainWindow.propertyFontSize.SelectedItem = null;
            _mainWindow.propertyFontSize.Text = $"{mm:F0}mm ({currentFontSize:F0}pt)";
        }

        // Get foreground color and set preview
        if (_textBlock.Foreground is SolidColorBrush brush)
        {
            _mainWindow.propertyColorPreview.Fill = brush;
        }

        // Set alignment
        _mainWindow.propertyAlignment.SelectedIndex = _textBlock.TextAlignment switch
        {
            TextAlignment.Left => 0,
            TextAlignment.Center => 1,
            TextAlignment.Right => 2,
            _ => 0
        };

        // Set bold and italic
        _mainWindow.propertyBold.IsChecked = _textBlock.FontWeight == FontWeights.Bold;
        _mainWindow.propertyItalic.IsChecked = _textBlock.FontStyle == FontStyles.Italic;

        // Enable controls
        _mainWindow.propertyText.IsEnabled = true;
        _mainWindow.propertyFontFamily.IsEnabled = true;
        _mainWindow.propertyFontSize.IsEnabled = true;
        _mainWindow.propertyColorButton.IsEnabled = true;
        _mainWindow.propertyAlignment.IsEnabled = true;
        _mainWindow.propertyBold.IsEnabled = true;
        _mainWindow.propertyItalic.IsEnabled = true;
    }

    /// <summary>
    /// Apply property changes from the properties panel.
    /// </summary>
    /// <param name="propertyName">The name of the property being changed.</param>
    /// <param name="newValue">The new value for the property.</param>
    public override void ApplyPropertyChanges(string propertyName, object newValue)
    {
        switch (propertyName)
        {
            case "X":
                var newX = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                SetCanvasLeft(newX);
                _canvasElement.X = newX;
                break;

            case "Y":
                var newY = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                SetCanvasTop(newY);
                _canvasElement.Y = newY;
                break;

            case "Width":
                var newWidth = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                _textBlock.Width = newWidth;
                _canvasElement.Width = newWidth;
                break;

            case "Height":
                var newHeight = Convert.ToDouble(newValue) * MM_TO_PIXELS;
                _textBlock.Height = newHeight;
                _canvasElement.Height = newHeight;
                break;

            case "Text":
                var text = newValue?.ToString() ?? string.Empty;
                _textBlock.Text = text;
                _canvasElement.Text = text;
                break;

            case "FontFamily":
                FontFamily fontFamily;
                if (newValue is FontFamily ff)
                    fontFamily = ff;
                else
                    fontFamily = new FontFamily(newValue?.ToString() ?? "Segoe UI");
                _textBlock.FontFamily = fontFamily;
                _canvasElement.FontFamily = fontFamily.Source;
                break;

            case "FontSize":
                // FontSize is in points (pt), not mm
                var fontSize = Convert.ToDouble(newValue);
                _textBlock.FontSize = fontSize;
                _canvasElement.FontSize = fontSize;
                break;

            case "Color":
                var color = (Color)newValue;
                _textBlock.Foreground = new SolidColorBrush(color);
                _canvasElement.ForegroundColor = color.ToString();
                break;

            case "Alignment":
                TextAlignment alignment;
                if (newValue is TextAlignment ta)
                    alignment = ta;
                else if (newValue is string alignStr)
                    alignment = alignStr switch
                    {
                        "Center" => TextAlignment.Center,
                        "Right" => TextAlignment.Right,
                        _ => TextAlignment.Left
                    };
                else if (newValue is int alignIndex)
                    alignment = alignIndex switch
                    {
                        1 => TextAlignment.Center,
                        2 => TextAlignment.Right,
                        _ => TextAlignment.Left
                    };
                else
                    alignment = TextAlignment.Left;

                _textBlock.TextAlignment = alignment;
                _canvasElement.TextAlignment = alignment.ToString();
                break;

            case "Bold":
                var isBold = Convert.ToBoolean(newValue);
                _textBlock.FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal;
                _canvasElement.FontWeight = _textBlock.FontWeight.ToString();
                break;

            case "Italic":
                var isItalic = Convert.ToBoolean(newValue);
                _textBlock.FontStyle = isItalic ? FontStyles.Italic : FontStyles.Normal;
                _canvasElement.FontStyle = _textBlock.FontStyle.ToString();
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

    #endregion
}
