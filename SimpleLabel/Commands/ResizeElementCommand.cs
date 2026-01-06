using System.Windows;

namespace SimpleLabel.Commands;

/// <summary>
/// Command to resize an element
/// </summary>
public class ResizeElementCommand : ICommand
{
    private readonly FrameworkElement _element;
    private readonly Size _oldSize;
    private readonly Size _newSize;

    public ResizeElementCommand(FrameworkElement element, Size oldSize, Size newSize)
    {
        _element = element;
        _oldSize = oldSize;
        _newSize = newSize;
    }

    public void Execute()
    {
        _element.Width = _newSize.Width;
        _element.Height = _newSize.Height;
    }

    public void Undo()
    {
        _element.Width = _oldSize.Width;
        _element.Height = _oldSize.Height;
    }
}
