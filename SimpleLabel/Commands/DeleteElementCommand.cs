using System.Windows;
using System.Windows.Controls;

namespace SimpleLabel.Commands;

/// <summary>
/// Command to delete an element from the canvas
/// </summary>
public class DeleteElementCommand : ICommand
{
    private readonly UIElement _element;
    private readonly Canvas _canvas;
    private readonly int _index;

    public DeleteElementCommand(UIElement element, Canvas canvas, int index)
    {
        _element = element;
        _canvas = canvas;
        _index = index;
    }

    public void Execute()
    {
        _canvas.Children.Remove(_element);
    }

    public void Undo()
    {
        // Restore element to its original position in the canvas children collection
        _canvas.Children.Insert(_index, _element);
    }
}
