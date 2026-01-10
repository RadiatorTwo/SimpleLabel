using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SimpleLabel.Commands;
using SimpleLabel.Controls;
using SimpleLabel.Models;
using SimpleLabel.Services;
using WpfColor = System.Windows.Media.Color;
using FormsColor = System.Drawing.Color;
using CommandManager = SimpleLabel.Commands.CommandManager;

namespace SimpleLabel.Controllers;

/// <summary>
/// Controller for property panel event handlers.
/// Manages property changes for selected elements with undo/redo support.
/// </summary>
public class PropertyPanelController
{
    // Unit conversion constants (96 DPI)
    private const double MM_TO_PIXELS = 96.0 / 25.4;
    private const double PIXELS_TO_MM = 25.4 / 96.0;

    // Dependencies
    private readonly CommandManager _commandManager;
    private readonly Action _updatePropertiesPanel;
    private readonly Action _updateUndoRedoButtons;
    private readonly Action _setDirty;
    private readonly Func<UIElement, IElementControl?> _getElementControl;

    // State fields for undo tracking
    private UIElement? _selectedElement;
    private double _initialPropertyValue;
    private string? _initialStringValue;
    private bool _initialBoolValue;

    /// <summary>
    /// Gets or sets the currently selected element.
    /// </summary>
    public UIElement? SelectedElement
    {
        get => _selectedElement;
        set => _selectedElement = value;
    }

    /// <summary>
    /// Initializes a new instance of the PropertyPanelController class.
    /// </summary>
    /// <param name="commandManager">The command manager for undo/redo operations.</param>
    /// <param name="updatePropertiesPanel">Callback to refresh the properties panel UI.</param>
    /// <param name="updateUndoRedoButtons">Callback to update undo/redo button states.</param>
    /// <param name="setDirty">Callback to mark the document as modified.</param>
    /// <param name="getElementControl">Function to get IElementControl for an element.</param>
    public PropertyPanelController(
        CommandManager commandManager,
        Action updatePropertiesPanel,
        Action updateUndoRedoButtons,
        Action setDirty,
        Func<UIElement, IElementControl?> getElementControl)
    {
        _commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
        _updatePropertiesPanel = updatePropertiesPanel ?? throw new ArgumentNullException(nameof(updatePropertiesPanel));
        _updateUndoRedoButtons = updateUndoRedoButtons ?? throw new ArgumentNullException(nameof(updateUndoRedoButtons));
        _setDirty = setDirty ?? throw new ArgumentNullException(nameof(setDirty));
        _getElementControl = getElementControl ?? throw new ArgumentNullException(nameof(getElementControl));
    }

    #region Focus Handlers

    /// <summary>
    /// Handles GotFocus event to capture initial values for undo/redo.
    /// </summary>
    public void HandleGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Store initial value for numeric properties
            if (double.TryParse(textBox.Text, out double value))
            {
                _initialPropertyValue = value;
            }
            // Store initial string value for text properties
            _initialStringValue = textBox.Text;
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
                        _initialStringValue = displayText switch
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
                        _initialStringValue = displayText;
                        if (double.TryParse(displayText, out double fontSize))
                        {
                            _initialPropertyValue = fontSize;
                        }
                    }
                    else
                    {
                        _initialStringValue = displayText;
                    }
                }
                else
                {
                    _initialStringValue = comboBox.SelectedItem.ToString();
                    // For editable combo boxes, also try to parse numeric value
                    if (comboBox.Name == "propertyFontSize" && double.TryParse(comboBox.Text, out double fontSize))
                    {
                        _initialPropertyValue = fontSize;
                    }
                }
            }
            else
            {
                // SelectedItem is null, use the current Text value
                _initialStringValue = comboBox.Text;
                // For font size combo box, also store as numeric value
                if (comboBox.Name == "propertyFontSize" && double.TryParse(comboBox.Text, out double fontSize))
                {
                    _initialPropertyValue = fontSize;
                }
            }
        }
        else if (sender is Slider slider)
        {
            // Store initial value for slider
            _initialPropertyValue = slider.Value;
        }
        else if (sender is NumericUpDown numericUpDown)
        {
            // Store initial value for NumericUpDown
            _initialPropertyValue = numericUpDown.Value;
        }
    }

    #endregion

    #region NumericUpDown ValueChanged Handlers

    /// <summary>
    /// Handles ValueChanged events from NumericUpDown controls.
    /// </summary>
    public void HandleValueChanged(object sender, double newValueMm, NumericUpDown propertyX, NumericUpDown propertyY,
        NumericUpDown propertyWidth, NumericUpDown propertyHeight,
        NumericUpDown propertyStrokeThickness, NumericUpDown propertyRadiusX, NumericUpDown propertyRadiusY)
    {
        if (_selectedElement == null) return;

        // Determine which property was changed and apply immediately
        // Note: newValueMm is in millimeters for position/size properties
        if (sender == propertyX)
        {
            double newValuePixels = newValueMm * MM_TO_PIXELS;
            double oldValuePixels = Canvas.GetLeft(_selectedElement);
            if (double.IsNaN(oldValuePixels)) oldValuePixels = 0;
            if (Math.Abs(newValuePixels - oldValuePixels) > 0.01)
            {
                _commandManager.ExecuteCommand(new ChangePropertyCommand(_selectedElement, "X", oldValuePixels, newValuePixels));
                _updateUndoRedoButtons();
                _setDirty();
            }
        }
        else if (sender == propertyY)
        {
            double newValuePixels = newValueMm * MM_TO_PIXELS;
            double oldValuePixels = Canvas.GetTop(_selectedElement);
            if (double.IsNaN(oldValuePixels)) oldValuePixels = 0;
            if (Math.Abs(newValuePixels - oldValuePixels) > 0.01)
            {
                _commandManager.ExecuteCommand(new ChangePropertyCommand(_selectedElement, "Y", oldValuePixels, newValuePixels));
                _updateUndoRedoButtons();
                _setDirty();
            }
        }
        else if (sender == propertyWidth)
        {
            if (_selectedElement is FrameworkElement fe)
            {
                double newValuePixels = newValueMm * MM_TO_PIXELS;
                double oldValuePixels = fe.Width;
                if (double.IsNaN(oldValuePixels)) oldValuePixels = 0;
                if (Math.Abs(newValuePixels - oldValuePixels) > 0.01)
                {
                    _commandManager.ExecuteCommand(new ChangePropertyCommand(_selectedElement, "Width", oldValuePixels, newValuePixels));
                    _updateUndoRedoButtons();
                    _setDirty();
                }
            }
        }
        else if (sender == propertyHeight)
        {
            if (_selectedElement is FrameworkElement fe)
            {
                double newValuePixels = newValueMm * MM_TO_PIXELS;
                double oldValuePixels = fe.Height;
                if (double.IsNaN(oldValuePixels)) oldValuePixels = 0;
                if (Math.Abs(newValuePixels - oldValuePixels) > 0.01)
                {
                    _commandManager.ExecuteCommand(new ChangePropertyCommand(_selectedElement, "Height", oldValuePixels, newValuePixels));
                    _updateUndoRedoButtons();
                    _setDirty();
                }
            }
        }
        else if (sender == propertyStrokeThickness && _selectedElement is Shape shape)
        {
            // StrokeThickness is in pixels, not mm
            double oldValue = shape.StrokeThickness;
            if (Math.Abs(newValueMm - oldValue) > 0.01)
            {
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "StrokeThickness", oldValue, newValueMm));
                _updateUndoRedoButtons();
                _setDirty();
            }
        }
        else if (sender == propertyRadiusX && _selectedElement is Rectangle rect)
        {
            // RadiusX is in pixels, not mm
            double oldValue = rect.RadiusX;
            if (Math.Abs(newValueMm - oldValue) > 0.01)
            {
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect, "RadiusX", oldValue, newValueMm));
                _updateUndoRedoButtons();
                _setDirty();
            }
        }
        else if (sender == propertyRadiusY && _selectedElement is Rectangle rect2)
        {
            // RadiusY is in pixels, not mm
            double oldValue = rect2.RadiusY;
            if (Math.Abs(newValueMm - oldValue) > 0.01)
            {
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect2, "RadiusY", oldValue, newValueMm));
                _updateUndoRedoButtons();
                _setDirty();
            }
        }
    }

    #endregion

    #region Position/Size Property Handlers

    /// <summary>
    /// Handles PropertyX LostFocus event.
    /// </summary>
    public void HandlePropertyXLostFocus(NumericUpDown control)
    {
        if (_selectedElement == null) return;

        double newValueMm = control.Value;
        double newValuePixels = newValueMm * MM_TO_PIXELS;

        // Get current value directly from element (may have changed via drag)
        double currentPixels = Canvas.GetLeft(_selectedElement);
        if (double.IsNaN(currentPixels)) currentPixels = 0;

        if (Math.Abs(newValuePixels - currentPixels) > 0.01)
        {
            // Value changed - create command
            _commandManager.ExecuteCommand(new ChangePropertyCommand(_selectedElement, "X", currentPixels, newValuePixels));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyY LostFocus event.
    /// </summary>
    public void HandlePropertyYLostFocus(NumericUpDown control)
    {
        if (_selectedElement == null) return;

        double newValueMm = control.Value;
        double newValuePixels = newValueMm * MM_TO_PIXELS;

        // Get current value directly from element (may have changed via drag)
        double currentPixels = Canvas.GetTop(_selectedElement);
        if (double.IsNaN(currentPixels)) currentPixels = 0;

        if (Math.Abs(newValuePixels - currentPixels) > 0.01)
        {
            // Value changed - create command
            _commandManager.ExecuteCommand(new ChangePropertyCommand(_selectedElement, "Y", currentPixels, newValuePixels));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyWidth LostFocus event.
    /// </summary>
    public void HandlePropertyWidthLostFocus(NumericUpDown control)
    {
        if (_selectedElement is not FrameworkElement element) return;

        double newValueMm = control.Value;
        double newValuePixels = newValueMm * MM_TO_PIXELS;

        // Get current value directly from element (may have changed via resize)
        double currentPixels = element.Width;
        if (double.IsNaN(currentPixels)) currentPixels = 0;

        if (Math.Abs(newValuePixels - currentPixels) > 0.01)
        {
            // Value changed - create command
            _commandManager.ExecuteCommand(new ChangePropertyCommand(element, "Width", currentPixels, newValuePixels));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyHeight LostFocus event.
    /// </summary>
    public void HandlePropertyHeightLostFocus(NumericUpDown control)
    {
        if (_selectedElement is not FrameworkElement element) return;

        double newValueMm = control.Value;
        double newValuePixels = newValueMm * MM_TO_PIXELS;

        // Get current value directly from element (may have changed via resize)
        double currentPixels = element.Height;
        if (double.IsNaN(currentPixels)) currentPixels = 0;

        if (Math.Abs(newValuePixels - currentPixels) > 0.01)
        {
            // Value changed - create command
            _commandManager.ExecuteCommand(new ChangePropertyCommand(element, "Height", currentPixels, newValuePixels));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyX2 LostFocus event (for Line/Arrow elements).
    /// </summary>
    public void HandlePropertyX2LostFocus(NumericUpDown control)
    {
        if (_selectedElement == null) return;

        // Always delegate to IElementControl which reads current values
        var elementControl = _getElementControl(_selectedElement);
        if (elementControl != null)
        {
            elementControl.ApplyPropertyChanges("X2", control.Value);
        }

        _updateUndoRedoButtons();
        _updatePropertiesPanel();
        _setDirty();
    }

    /// <summary>
    /// Handles PropertyY2 LostFocus event (for Line/Arrow elements).
    /// </summary>
    public void HandlePropertyY2LostFocus(NumericUpDown control)
    {
        if (_selectedElement == null) return;

        // Always delegate to IElementControl which reads current values
        var elementControl = _getElementControl(_selectedElement);
        if (elementControl != null)
        {
            elementControl.ApplyPropertyChanges("Y2", control.Value);
        }

        _updateUndoRedoButtons();
        _updatePropertiesPanel();
        _setDirty();
    }

    #endregion

    #region Text Property Handlers

    /// <summary>
    /// Handles PropertyText LostFocus event.
    /// </summary>
    public void HandlePropertyTextLostFocus(TextBox textBox)
    {
        if (_selectedElement is not TextBlock textBlock)
            return;

        string newValue = textBox.Text;
        string oldValue = _initialStringValue ?? textBlock.Text;

        if (newValue != oldValue)
        {
            // Value changed - create command
            _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "Text", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyText PreviewKeyDown event to handle Enter key.
    /// </summary>
    public void HandlePropertyTextPreviewKeyDown(TextBox textBox, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Trigger the LostFocus logic by moving focus away
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles PropertyFontFamily LostFocus event.
    /// </summary>
    public void HandlePropertyFontFamilyLostFocus(ComboBox comboBox)
    {
        if (_selectedElement is not TextBlock textBlock)
            return;

        if (comboBox.SelectedItem == null)
            return;

        string newValue = comboBox.SelectedItem.ToString()!;
        string oldValue = _initialStringValue ?? textBlock.FontFamily.Source;

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.FontFamily = new FontFamily(oldValue);
            _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontFamily", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyFontSize SelectionChanged event.
    /// </summary>
    public void HandlePropertyFontSizeChanged(ComboBox comboBox)
    {
        if (_selectedElement is not TextBlock textBlock)
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
                    _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontSize", oldValue, newValue));
                    _updateUndoRedoButtons();
                    _updatePropertiesPanel();
                    _setDirty();
                }
            }
        }
    }

    /// <summary>
    /// Handles PropertyFontSize LostFocus event for manual text input.
    /// </summary>
    public void HandlePropertyFontSizeLostFocus(ComboBox comboBox)
    {
        if (_selectedElement is not TextBlock textBlock)
            return;

        // Handle manual text input (when user types directly)
        string text = comboBox.Text;

        // Try to parse as direct pt value
        if (!double.TryParse(text, out double newValue))
            return;

        // If initialPropertyValue is invalid (0 or negative), use the current FontSize
        double oldValue = _initialPropertyValue > 0 ? _initialPropertyValue : textBlock.FontSize;

        if (Math.Abs(newValue - oldValue) > 0.01)
        {
            // Value changed - create command
            textBlock.FontSize = oldValue;
            _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontSize", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyColor Click event.
    /// </summary>
    public void HandlePropertyColorClick()
    {
        if (_selectedElement is not TextBlock textBlock) return;

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
                _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "Foreground", oldValue, newValue));
                _updateUndoRedoButtons();
                _updatePropertiesPanel();
                _setDirty();
            }
        }
    }

    /// <summary>
    /// Handles PropertyAlignment LostFocus event.
    /// </summary>
    public void HandlePropertyAlignmentLostFocus(ComboBox comboBox)
    {
        if (_selectedElement is not TextBlock textBlock)
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

        string oldValue = _initialStringValue ?? textBlock.TextAlignment.ToString();

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.TextAlignment = Enum.Parse<TextAlignment>(oldValue);
            _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "TextAlignment", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyBold Changed event.
    /// </summary>
    public void HandlePropertyBoldChanged(CheckBox checkBox)
    {
        if (_selectedElement is not TextBlock textBlock)
            return;

        bool newChecked = checkBox.IsChecked == true;
        string newValue = newChecked ? "Bold" : "Normal";
        string oldValue = (textBlock.FontWeight == FontWeights.Bold) ? "Bold" : "Normal";

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.FontWeight = oldValue == "Bold" ? FontWeights.Bold : FontWeights.Normal;
            _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontWeight", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyItalic Changed event.
    /// </summary>
    public void HandlePropertyItalicChanged(CheckBox checkBox)
    {
        if (_selectedElement is not TextBlock textBlock)
            return;

        bool newChecked = checkBox.IsChecked == true;
        string newValue = newChecked ? "Italic" : "Normal";
        string oldValue = (textBlock.FontStyle == FontStyles.Italic) ? "Italic" : "Normal";

        if (newValue != oldValue)
        {
            // Value changed - create command
            textBlock.FontStyle = oldValue == "Italic" ? FontStyles.Italic : FontStyles.Normal;
            _commandManager.ExecuteCommand(new ChangeTextPropertyCommand(textBlock, "FontStyle", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    #endregion

    #region Shape Property Handlers

    /// <summary>
    /// Handles PropertyFillColor Click event.
    /// </summary>
    public void HandlePropertyFillColorClick()
    {
        if (_selectedElement is not Shape shape) return;

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
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "Fill", oldValue, newValue));
                _updateUndoRedoButtons();
                _updatePropertiesPanel();
                _setDirty();
            }
        }
    }

    /// <summary>
    /// Handles PropertyStrokeColor Click event.
    /// </summary>
    public void HandlePropertyStrokeColorClick()
    {
        // Handle Arrow elements (Canvas containing Line)
        if (_selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
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

                _updatePropertiesPanel();
                _setDirty();
            }
            return;
        }

        // Handle Line elements
        if (_selectedElement is Line lineElement)
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
                _updatePropertiesPanel();
                _setDirty();
            }
            return;
        }

        // Handle regular Shape elements
        if (_selectedElement is not Shape shape) return;

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
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "Stroke", oldValue, newValue));
                _updateUndoRedoButtons();
                _updatePropertiesPanel();
                _setDirty();
            }
        }
    }

    /// <summary>
    /// Handles PropertyStrokeThickness LostFocus event.
    /// </summary>
    public void HandlePropertyStrokeThicknessLostFocus(NumericUpDown control)
    {
        double newValue = control.Value;

        // Handle Arrow elements (Canvas containing Line)
        if (_selectedElement is Canvas arrowCanvas && arrowCanvas.Tag is Tuple<Line, Polygon, Polygon, CanvasElement> arrowData)
        {
            var line = arrowData.Item1;
            if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
            {
                line.StrokeThickness = newValue;
                _updatePropertiesPanel();
                _setDirty();
            }
            return;
        }

        // Handle Line elements
        if (_selectedElement is Line lineElement)
        {
            if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
            {
                lineElement.StrokeThickness = newValue;
                _updatePropertiesPanel();
                _setDirty();
            }
            return;
        }

        // Handle regular Shape elements
        if (_selectedElement is not Shape shape) return;

        if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
        {
            // Value changed - create command
            shape.StrokeThickness = _initialPropertyValue;
            _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "StrokeThickness", _initialPropertyValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyRadiusX LostFocus event.
    /// </summary>
    public void HandlePropertyRadiusXLostFocus(NumericUpDown control)
    {
        if (_selectedElement is not Rectangle rect) return;

        double newValue = control.Value;

        if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
        {
            // Value changed - create command
            rect.RadiusX = _initialPropertyValue;
            _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect, "RadiusX", _initialPropertyValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyRadiusY LostFocus event.
    /// </summary>
    public void HandlePropertyRadiusYLostFocus(NumericUpDown control)
    {
        if (_selectedElement is not Rectangle rect) return;

        double newValue = control.Value;

        if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
        {
            // Value changed - create command
            rect.RadiusY = _initialPropertyValue;
            _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(rect, "RadiusY", _initialPropertyValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyStrokeDashPattern Changed event.
    /// </summary>
    public void HandlePropertyStrokeDashPatternChanged(ComboBox comboBox)
    {
        if (_selectedElement is not Shape shape || comboBox.SelectedItem is not ComboBoxItem item)
            return;

        string newPattern = item.Tag?.ToString() ?? "Solid";
        string oldPattern = LabelSerializer.DetectDashPattern(shape.StrokeDashArray);

        if (oldPattern != newPattern)
        {
            _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "StrokeDashPattern", oldPattern, newPattern));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyUseGradientFill Changed event.
    /// </summary>
    public void HandlePropertyUseGradientFillChanged(CheckBox checkBox)
    {
        if (_selectedElement is not Shape shape) return;

        bool newValue = checkBox.IsChecked == true;
        bool oldValue = shape.Fill is LinearGradientBrush;

        if (oldValue != newValue)
        {
            _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "UseGradientFill", oldValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyGradientStart Click event.
    /// </summary>
    public void HandlePropertyGradientStartClick()
    {
        if (_selectedElement is not Shape shape || shape.Fill is not LinearGradientBrush gradientBrush)
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
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "GradientStartColor", oldValue, newValue));
                _updateUndoRedoButtons();
                _updatePropertiesPanel();
                _setDirty();
            }
        }
    }

    /// <summary>
    /// Handles PropertyGradientEnd Click event.
    /// </summary>
    public void HandlePropertyGradientEndClick()
    {
        if (_selectedElement is not Shape shape || shape.Fill is not LinearGradientBrush gradientBrush)
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
                _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "GradientEndColor", oldValue, newValue));
                _updateUndoRedoButtons();
                _updatePropertiesPanel();
                _setDirty();
            }
        }
    }

    /// <summary>
    /// Handles PropertyGradientAngle LostFocus event.
    /// </summary>
    public void HandlePropertyGradientAngleLostFocus(Slider slider)
    {
        if (_selectedElement is not Shape shape) return;

        double newValue = slider.Value;

        if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
        {
            _commandManager.ExecuteCommand(new ChangeShapePropertyCommand(shape, "GradientAngle", _initialPropertyValue, newValue));
            _updateUndoRedoButtons();
            _updatePropertiesPanel();
            _setDirty();
        }
    }

    #endregion

    #region Image Property Handlers

    /// <summary>
    /// Handles PropertyMonochromeEnabled Changed event.
    /// </summary>
    public void HandlePropertyMonochromeEnabledChanged(CheckBox checkBox, Panel monochromeControlsPanel, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        bool oldValue = canvasElement.MonochromeEnabled ?? false;
        bool newValue = checkBox.IsChecked ?? false;

        if (newValue != oldValue)
        {
            // Show/hide monochrome controls
            monochromeControlsPanel.Visibility = newValue ? Visibility.Visible : Visibility.Collapsed;

            // Value changed - create command
            _commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "MonochromeEnabled", oldValue, newValue, applyImageFilter));
            _updateUndoRedoButtons();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyThreshold ValueChanged event for real-time preview.
    /// </summary>
    public void HandlePropertyThresholdValueChanged(Slider slider, TextBlock thresholdValueText, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        // Update the value display
        thresholdValueText.Text = ((int)slider.Value).ToString();

        // If monochrome is enabled, apply filter immediately for real-time preview
        if (imageElement.Tag is Tuple<CanvasElement, BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;
            if (canvasElement.MonochromeEnabled == true)
            {
                // Update the threshold value (without creating undo command yet)
                canvasElement.Threshold = (byte)slider.Value;
                applyImageFilter(imageElement);
            }
        }
    }

    /// <summary>
    /// Handles PropertyThreshold LostFocus event for undo command creation.
    /// </summary>
    public void HandlePropertyThresholdLostFocus(Slider slider, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        byte newValue = (byte)slider.Value;
        byte oldValue = (byte)_initialPropertyValue;

        if (newValue != oldValue)
        {
            // Value changed - create command for undo/redo
            canvasElement.Threshold = oldValue; // Reset to old value
            _commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Threshold", oldValue, newValue, applyImageFilter));
            _updateUndoRedoButtons();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyAlgorithm Changed event.
    /// </summary>
    public void HandlePropertyAlgorithmChanged(ComboBox comboBox, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
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
            _commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Algorithm", oldValue, newValue, applyImageFilter));
            _updateUndoRedoButtons();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyBrightness ValueChanged event for real-time preview.
    /// </summary>
    public void HandlePropertyBrightnessValueChanged(Slider slider, TextBlock brightnessValueText, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        // Update the value display
        brightnessValueText.Text = ((int)slider.Value).ToString();

        // If monochrome is enabled, apply filter immediately for real-time preview
        if (imageElement.Tag is Tuple<CanvasElement, BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;
            if (canvasElement.MonochromeEnabled == true)
            {
                canvasElement.Brightness = slider.Value;
                applyImageFilter(imageElement);
            }
        }
    }

    /// <summary>
    /// Handles PropertyBrightness LostFocus event for undo command creation.
    /// </summary>
    public void HandlePropertyBrightnessLostFocus(Slider slider, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        double newValue = slider.Value;
        double oldValue = _initialPropertyValue;

        if (Math.Abs(newValue - oldValue) > 0.01)
        {
            canvasElement.Brightness = oldValue;
            _commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Brightness", oldValue, newValue, applyImageFilter));
            _updateUndoRedoButtons();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyContrast ValueChanged event for real-time preview.
    /// </summary>
    public void HandlePropertyContrastValueChanged(Slider slider, TextBlock contrastValueText, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        // Update the value display
        contrastValueText.Text = ((int)slider.Value).ToString();

        // If monochrome is enabled, apply filter immediately for real-time preview
        if (imageElement.Tag is Tuple<CanvasElement, BitmapSource> tuple)
        {
            var canvasElement = tuple.Item1;
            if (canvasElement.MonochromeEnabled == true)
            {
                canvasElement.Contrast = slider.Value;
                applyImageFilter(imageElement);
            }
        }
    }

    /// <summary>
    /// Handles PropertyContrast LostFocus event for undo command creation.
    /// </summary>
    public void HandlePropertyContrastLostFocus(Slider slider, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        double newValue = slider.Value;
        double oldValue = _initialPropertyValue;

        if (Math.Abs(newValue - oldValue) > 0.01)
        {
            canvasElement.Contrast = oldValue;
            _commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Contrast", oldValue, newValue, applyImageFilter));
            _updateUndoRedoButtons();
            _setDirty();
        }
    }

    /// <summary>
    /// Handles PropertyInvert Changed event.
    /// </summary>
    public void HandlePropertyInvertChanged(CheckBox checkBox, Action<Image> applyImageFilter)
    {
        if (_selectedElement is not Image imageElement)
            return;

        if (imageElement.Tag is not Tuple<CanvasElement, BitmapSource> tuple)
            return;

        var canvasElement = tuple.Item1;
        bool oldValue = canvasElement.InvertColors ?? false;
        bool newValue = checkBox.IsChecked ?? false;

        if (newValue != oldValue)
        {
            _commandManager.ExecuteCommand(new ChangeImagePropertyCommand(imageElement, "Invert", oldValue, newValue, applyImageFilter));
            _updateUndoRedoButtons();
            _setDirty();
        }
    }

    #endregion

    #region Arrow Property Handlers

    /// <summary>
    /// Handles PropertyHasStartArrow Changed event.
    /// </summary>
    public void HandlePropertyHasStartArrowChanged(CheckBox checkBox)
    {
        if (_selectedElement == null)
            return;

        var elementControl = _getElementControl(_selectedElement);
        if (elementControl != null)
        {
            elementControl.ApplyPropertyChanges("HasStartArrow", checkBox.IsChecked ?? false);
        }
    }

    /// <summary>
    /// Handles PropertyHasEndArrow Changed event.
    /// </summary>
    public void HandlePropertyHasEndArrowChanged(CheckBox checkBox)
    {
        if (_selectedElement == null)
            return;

        var elementControl = _getElementControl(_selectedElement);
        if (elementControl != null)
        {
            elementControl.ApplyPropertyChanges("HasEndArrow", checkBox.IsChecked ?? false);
        }
    }

    /// <summary>
    /// Handles PropertyArrowheadSize ValueChanged event (for display update).
    /// </summary>
    public void HandlePropertyArrowheadSizeValueChanged(double newValue, TextBlock sizeValueText)
    {
        sizeValueText.Text = newValue.ToString("F0");
    }

    /// <summary>
    /// Handles PropertyArrowheadSize LostFocus event.
    /// </summary>
    public void HandlePropertyArrowheadSizeLostFocus(Slider slider)
    {
        if (_selectedElement == null)
            return;

        double newValue = slider.Value;

        if (Math.Abs(newValue - _initialPropertyValue) > 0.01)
        {
            var elementControl = _getElementControl(_selectedElement);
            if (elementControl != null)
            {
                elementControl.ApplyPropertyChanges("ArrowheadSize", newValue);
            }
        }
    }

    #endregion
}
