using System.Windows;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Enumeration of resize handle positions for element manipulation.
/// </summary>
public enum ResizeHandle
{
    /// <summary>Top-left corner handle.</summary>
    TopLeft,

    /// <summary>Top-center edge handle.</summary>
    TopCenter,

    /// <summary>Top-right corner handle.</summary>
    TopRight,

    /// <summary>Middle-left edge handle.</summary>
    MiddleLeft,

    /// <summary>Middle-right edge handle.</summary>
    MiddleRight,

    /// <summary>Bottom-left corner handle.</summary>
    BottomLeft,

    /// <summary>Bottom-center edge handle.</summary>
    BottomCenter,

    /// <summary>Bottom-right corner handle.</summary>
    BottomRight,

    /// <summary>Start point handle for endpoint-based elements (Line, Arrow).</summary>
    StartPoint,

    /// <summary>End point handle for endpoint-based elements (Line, Arrow).</summary>
    EndPoint
}

/// <summary>
/// Interface for element-specific controls that handle selection, resize, and property management.
/// Each canvas element type implements this interface to provide element-specific behavior.
/// </summary>
public interface IElementControl
{
    /// <summary>
    /// Gets the type of element this control manages.
    /// </summary>
    ElementType ElementType { get; }

    /// <summary>
    /// Gets the underlying UI element being controlled.
    /// </summary>
    UIElement UIElement { get; }

    /// <summary>
    /// Gets the data model for serialization.
    /// </summary>
    CanvasElement CanvasElement { get; }

    /// <summary>
    /// Gets whether this element uses endpoint-based positioning (X1/Y1/X2/Y2) vs bounds-based (X/Y/Width/Height).
    /// Returns true for Line and Arrow elements, false for all others.
    /// </summary>
    bool UsesEndpoints { get; }

    /// <summary>
    /// Handle resize from a specific handle position.
    /// </summary>
    /// <param name="handle">The resize handle being dragged.</param>
    /// <param name="horizontalChange">The horizontal drag delta.</param>
    /// <param name="verticalChange">The vertical drag delta.</param>
    void HandleResize(ResizeHandle handle, double horizontalChange, double verticalChange);

    /// <summary>
    /// Update the properties panel with this element's values.
    /// </summary>
    /// <remarks>
    /// The exact panel type will be determined during Phase 6 integration.
    /// For now, implementations can leave this empty or throw NotImplementedException.
    /// </remarks>
    void PopulatePropertiesPanel();

    /// <summary>
    /// Apply property changes from the properties panel.
    /// </summary>
    /// <param name="propertyName">The name of the property being changed.</param>
    /// <param name="newValue">The new value for the property.</param>
    void ApplyPropertyChanges(string propertyName, object newValue);
}
