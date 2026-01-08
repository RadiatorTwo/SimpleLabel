using System.Windows;
using System.Windows.Shapes;

namespace SimpleLabel.Commands;

/// <summary>
/// Command to resize a Line element by updating X1/Y1/X2/Y2
/// </summary>
public class ResizeLineCommand : ICommand
{
    private readonly Line _line;
    private readonly Point _oldStart;
    private readonly Point _oldEnd;
    private readonly Point _newStart;
    private readonly Point _newEnd;

    public ResizeLineCommand(Line line, Point oldStart, Point oldEnd, Point newStart, Point newEnd)
    {
        _line = line;
        _oldStart = oldStart;
        _oldEnd = oldEnd;
        _newStart = newStart;
        _newEnd = newEnd;
    }

    public void Execute()
    {
        _line.X1 = _newStart.X;
        _line.Y1 = _newStart.Y;
        _line.X2 = _newEnd.X;
        _line.Y2 = _newEnd.Y;
    }

    public void Undo()
    {
        _line.X1 = _oldStart.X;
        _line.Y1 = _oldStart.Y;
        _line.X2 = _oldEnd.X;
        _line.Y2 = _oldEnd.Y;
    }
}
