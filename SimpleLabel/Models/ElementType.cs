namespace SimpleLabel.Models;

/// <summary>
/// Enumeration of all supported canvas element types.
/// Replaces magic strings like "Text", "Rectangle" for compile-time type safety.
/// </summary>
public enum ElementType
{
    /// <summary>
    /// Text element with font, color, and alignment properties.
    /// </summary>
    Text,

    /// <summary>
    /// Rectangle shape with fill, stroke, and corner radius properties.
    /// </summary>
    Rectangle,

    /// <summary>
    /// Ellipse shape with fill and stroke properties.
    /// </summary>
    Ellipse,

    /// <summary>
    /// Line element using endpoint coordinates (X1/Y1/X2/Y2).
    /// </summary>
    Line,

    /// <summary>
    /// Arrow element using endpoint coordinates with arrowhead decorations.
    /// </summary>
    Arrow,

    /// <summary>
    /// Image element with optional monochrome filters.
    /// </summary>
    Image,

    /// <summary>
    /// Custom polygon shape defined by point collection.
    /// </summary>
    Polygon,

    /// <summary>
    /// Triangle shape (specialized polygon).
    /// </summary>
    Triangle,

    /// <summary>
    /// Star shape (specialized polygon).
    /// </summary>
    Star
}
