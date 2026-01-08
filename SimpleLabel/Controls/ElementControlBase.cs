using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SimpleLabel.Models;

namespace SimpleLabel.Controls;

/// <summary>
/// Abstract base class for element controls with shared functionality.
/// Provides default implementations for common operations that can be overridden by specific element types.
/// </summary>
public abstract class ElementControlBase : IElementControl
{
    /// <summary>
    /// Conversion factor from millimeters to pixels (at 96 DPI).
    /// </summary>
    protected const double MM_TO_PIXELS = 96.0 / 25.4;

    /// <summary>
    /// Conversion factor from pixels to millimeters (at 96 DPI).
    /// </summary>
    protected const double PIXELS_TO_MM = 25.4 / 96.0;

    /// <summary>
    /// The underlying WPF UI element being controlled.
    /// </summary>
    protected readonly UIElement _uiElement;

    /// <summary>
    /// The serializable data model for this element.
    /// </summary>
    protected readonly CanvasElement _canvasElement;

    /// <summary>
    /// Reference to the MainWindow for property panel access.
    /// </summary>
    protected MainWindow? _mainWindow;

    /// <summary>
    /// Gets the type of element this control manages.
    /// </summary>
    public abstract ElementType ElementType { get; }

    /// <summary>
    /// Gets the underlying UI element being controlled.
    /// </summary>
    public UIElement UIElement => _uiElement;

    /// <summary>
    /// Gets the data model for serialization.
    /// </summary>
    public CanvasElement CanvasElement => _canvasElement;

    /// <summary>
    /// Gets whether this element uses endpoint-based positioning (X1/Y1/X2/Y2) vs bounds-based (X/Y/Width/Height).
    /// Default is false. Override in Line and Arrow controls to return true.
    /// </summary>
    public virtual bool UsesEndpoints => false;

    /// <summary>
    /// Initializes a new instance of the ElementControlBase class.
    /// </summary>
    /// <param name="uiElement">The WPF UI element to control.</param>
    /// <param name="canvasElement">The data model for serialization.</param>
    /// <param name="mainWindow">Optional reference to the MainWindow for property panel access.</param>
    protected ElementControlBase(UIElement uiElement, CanvasElement canvasElement, MainWindow? mainWindow = null)
    {
        _uiElement = uiElement ?? throw new ArgumentNullException(nameof(uiElement));
        _canvasElement = canvasElement ?? throw new ArgumentNullException(nameof(canvasElement));
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// Default resize implementation for bounds-based elements (X/Y/Width/Height).
    /// Supports Shift key for aspect ratio preservation.
    /// Line and Arrow controls will override this for endpoint-based behavior.
    /// </summary>
    /// <param name="handle">The resize handle being dragged.</param>
    /// <param name="horizontalChange">The horizontal drag delta.</param>
    /// <param name="verticalChange">The vertical drag delta.</param>
    public virtual void HandleResize(ResizeHandle handle, double horizontalChange, double verticalChange)
    {
        if (_uiElement is not FrameworkElement fe)
            return;

        const double minSize = 10.0;

        // Check if Shift is pressed for aspect ratio preservation
        bool maintainAspectRatio = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        double aspectRatio = fe.Width / fe.Height;

        switch (handle)
        {
            case ResizeHandle.BottomRight:
                {
                    var newWidth = fe.Width + horizontalChange;
                    var newHeight = fe.Height + verticalChange;

                    if (maintainAspectRatio)
                    {
                        if (Math.Abs(horizontalChange) > Math.Abs(verticalChange))
                            newHeight = newWidth / aspectRatio;
                        else
                            newWidth = newHeight * aspectRatio;
                    }

                    if (newWidth > minSize) fe.Width = newWidth;
                    if (newHeight > minSize) fe.Height = newHeight;
                }
                break;

            case ResizeHandle.BottomLeft:
                {
                    var newWidth = fe.Width - horizontalChange;
                    var newHeight = fe.Height + verticalChange;

                    if (maintainAspectRatio)
                    {
                        if (Math.Abs(horizontalChange) > Math.Abs(verticalChange))
                            newHeight = newWidth / aspectRatio;
                        else
                            newWidth = newHeight * aspectRatio;
                    }

                    if (newWidth > minSize)
                    {
                        double widthDelta = fe.Width - newWidth;
                        fe.Width = newWidth;
                        SetCanvasLeft(GetCanvasLeft() + widthDelta);
                    }
                    if (newHeight > minSize) fe.Height = newHeight;
                }
                break;

            case ResizeHandle.TopRight:
                {
                    var newWidth = fe.Width + horizontalChange;
                    var newHeight = fe.Height - verticalChange;

                    if (maintainAspectRatio)
                    {
                        if (Math.Abs(horizontalChange) > Math.Abs(verticalChange))
                            newHeight = newWidth / aspectRatio;
                        else
                            newWidth = newHeight * aspectRatio;
                    }

                    if (newWidth > minSize) fe.Width = newWidth;
                    if (newHeight > minSize)
                    {
                        double heightDelta = fe.Height - newHeight;
                        fe.Height = newHeight;
                        SetCanvasTop(GetCanvasTop() + heightDelta);
                    }
                }
                break;

            case ResizeHandle.TopLeft:
                {
                    var newWidth = fe.Width - horizontalChange;
                    var newHeight = fe.Height - verticalChange;

                    if (maintainAspectRatio)
                    {
                        if (Math.Abs(horizontalChange) > Math.Abs(verticalChange))
                            newHeight = newWidth / aspectRatio;
                        else
                            newWidth = newHeight * aspectRatio;
                    }

                    if (newWidth > minSize)
                    {
                        double widthDelta = fe.Width - newWidth;
                        fe.Width = newWidth;
                        SetCanvasLeft(GetCanvasLeft() + widthDelta);
                    }
                    if (newHeight > minSize)
                    {
                        double heightDelta = fe.Height - newHeight;
                        fe.Height = newHeight;
                        SetCanvasTop(GetCanvasTop() + heightDelta);
                    }
                }
                break;

            case ResizeHandle.MiddleRight:
                {
                    var newWidth = fe.Width + horizontalChange;
                    var newHeight = maintainAspectRatio ? newWidth / aspectRatio : fe.Height;

                    if (newWidth > minSize && newHeight > minSize)
                    {
                        fe.Width = newWidth;
                        fe.Height = newHeight;
                    }
                }
                break;

            case ResizeHandle.MiddleLeft:
                {
                    var newWidth = fe.Width - horizontalChange;
                    var newHeight = maintainAspectRatio ? newWidth / aspectRatio : fe.Height;

                    if (newWidth > minSize && newHeight > minSize)
                    {
                        double widthDelta = fe.Width - newWidth;
                        fe.Width = newWidth;
                        fe.Height = newHeight;
                        SetCanvasLeft(GetCanvasLeft() + widthDelta);
                    }
                }
                break;

            case ResizeHandle.BottomCenter:
                {
                    var newHeight = fe.Height + verticalChange;
                    var newWidth = maintainAspectRatio ? newHeight * aspectRatio : fe.Width;

                    if (newWidth > minSize && newHeight > minSize)
                    {
                        fe.Width = newWidth;
                        fe.Height = newHeight;
                    }
                }
                break;

            case ResizeHandle.TopCenter:
                {
                    var newHeight = fe.Height - verticalChange;
                    var newWidth = maintainAspectRatio ? newHeight * aspectRatio : fe.Width;

                    if (newWidth > minSize && newHeight > minSize)
                    {
                        double heightDelta = fe.Height - newHeight;
                        fe.Width = newWidth;
                        fe.Height = newHeight;
                        SetCanvasTop(GetCanvasTop() + heightDelta);
                    }
                }
                break;

            case ResizeHandle.StartPoint:
            case ResizeHandle.EndPoint:
                // These are for endpoint-based elements - subclasses should override
                break;
        }
    }

    /// <summary>
    /// Update the properties panel with this element's values.
    /// </summary>
    /// <remarks>
    /// Implementation will be completed in Phase 6 when property panel structure is defined.
    /// </remarks>
    public abstract void PopulatePropertiesPanel();

    /// <summary>
    /// Apply property changes from the properties panel.
    /// </summary>
    /// <param name="propertyName">The name of the property being changed.</param>
    /// <param name="newValue">The new value for the property.</param>
    public abstract void ApplyPropertyChanges(string propertyName, object newValue);

    #region Canvas Position Helpers

    /// <summary>
    /// Gets the current Canvas.Left position of the element.
    /// </summary>
    /// <returns>The left position, or 0.0 if not set.</returns>
    protected double GetCanvasLeft()
    {
        var left = Canvas.GetLeft(_uiElement);
        return double.IsNaN(left) ? 0.0 : left;
    }

    /// <summary>
    /// Gets the current Canvas.Top position of the element.
    /// </summary>
    /// <returns>The top position, or 0.0 if not set.</returns>
    protected double GetCanvasTop()
    {
        var top = Canvas.GetTop(_uiElement);
        return double.IsNaN(top) ? 0.0 : top;
    }

    /// <summary>
    /// Sets the Canvas.Left position of the element.
    /// </summary>
    /// <param name="left">The left position to set.</param>
    protected void SetCanvasLeft(double left)
    {
        Canvas.SetLeft(_uiElement, left);
    }

    /// <summary>
    /// Sets the Canvas.Top position of the element.
    /// </summary>
    /// <param name="top">The top position to set.</param>
    protected void SetCanvasTop(double top)
    {
        Canvas.SetTop(_uiElement, top);
    }

    /// <summary>
    /// Gets the current canvas position as a point.
    /// </summary>
    /// <returns>A Point containing the Canvas.Left and Canvas.Top values.</returns>
    protected Point GetCanvasPosition()
    {
        return new Point(GetCanvasLeft(), GetCanvasTop());
    }

    /// <summary>
    /// Sets the canvas position of the element.
    /// </summary>
    /// <param name="x">The left position.</param>
    /// <param name="y">The top position.</param>
    protected void SetCanvasPosition(double x, double y)
    {
        SetCanvasLeft(x);
        SetCanvasTop(y);
    }

    #endregion

    #region Size Helpers

    /// <summary>
    /// Gets the current size of the element.
    /// </summary>
    /// <returns>A Size containing the Width and Height values, or Size.Empty if not a FrameworkElement.</returns>
    protected Size GetElementSize()
    {
        if (_uiElement is FrameworkElement fe)
        {
            return new Size(fe.Width, fe.Height);
        }
        return Size.Empty;
    }

    /// <summary>
    /// Sets the size of the element.
    /// </summary>
    /// <param name="width">The width to set.</param>
    /// <param name="height">The height to set.</param>
    protected void SetElementSize(double width, double height)
    {
        if (_uiElement is FrameworkElement fe)
        {
            fe.Width = width;
            fe.Height = height;
        }
    }

    #endregion
}
