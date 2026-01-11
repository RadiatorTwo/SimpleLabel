using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Models;

/// <summary>
/// Represents a layer item in the Layers panel, wrapping a canvas element.
/// </summary>
public class LayerItem : INotifyPropertyChanged
{
    private UIElement? _element;
    private string _displayName = string.Empty;
    private ElementType _elementType;

    /// <summary>
    /// Gets or sets the canvas element this layer represents.
    /// </summary>
    public UIElement? Element
    {
        get => _element;
        set
        {
            if (_element != value)
            {
                _element = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the display name shown in the Layers panel.
    /// </summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the element type.
    /// </summary>
    public ElementType ElementType
    {
        get => _elementType;
        set
        {
            if (_elementType != value)
            {
                _elementType = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Generates a display name for the given element based on its type.
    /// </summary>
    /// <param name="element">The canvas element.</param>
    /// <param name="elementType">The type of the element.</param>
    /// <returns>A user-friendly display name for the layer.</returns>
    public static string GetDisplayName(UIElement element, ElementType elementType)
    {
        return elementType switch
        {
            ElementType.Text => GetTextDisplayName(element),
            ElementType.Rectangle => "Rectangle",
            ElementType.Ellipse => "Ellipse",
            ElementType.Line => "Line",
            ElementType.Arrow => "Arrow",
            ElementType.Triangle => "Triangle",
            ElementType.Star => "Star",
            ElementType.Image => "Image",
            ElementType.Polygon => "Polygon",
            _ => "Element"
        };
    }

    private static string GetTextDisplayName(UIElement element)
    {
        if (element is TextBlock textBlock)
        {
            var text = textBlock.Text ?? string.Empty;
            if (text.Length > 15)
            {
                text = text.Substring(0, 15) + "...";
            }
            return $"Text: \"{text}\"";
        }
        return "Text";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
