using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Commands;

/// <summary>
/// Command to add an element to the canvas
/// </summary>
public class AddElementCommand : ICommand
{
    private readonly UIElement _element;
    private readonly Canvas _canvas;

    public AddElementCommand(UIElement element, Canvas canvas)
    {
        _element = element;
        _canvas = canvas;
    }

    public void Execute()
    {
        _canvas.Children.Add(_element);
    }

    public void Undo()
    {
        _canvas.Children.Remove(_element);
    }
}
