using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Commands;

/// <summary>
/// Command to move an element on the canvas
/// </summary>
public class MoveElementCommand : ICommand
{
    private readonly UIElement _element;
    private readonly Point _oldPosition;
    private readonly Point _newPosition;

    public MoveElementCommand(UIElement element, Point oldPosition, Point newPosition)
    {
        _element = element;
        _oldPosition = oldPosition;
        _newPosition = newPosition;
    }

    public void Execute()
    {
        Canvas.SetLeft(_element, _newPosition.X);
        Canvas.SetTop(_element, _newPosition.Y);
    }

    public void Undo()
    {
        Canvas.SetLeft(_element, _oldPosition.X);
        Canvas.SetTop(_element, _oldPosition.Y);
    }
}
