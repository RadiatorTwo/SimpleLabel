namespace SimpleLabel.Commands;

/// <summary>
/// Command that groups multiple commands into one undoable operation
/// </summary>
public class CompositeCommand : ICommand
{
    private readonly List<ICommand> _commands;

    public CompositeCommand(IEnumerable<ICommand> commands)
    {
        _commands = commands.ToList();
    }

    public void Execute()
    {
        foreach (var command in _commands)
        {
            command.Execute();
        }
    }

    public void Undo()
    {
        // Undo in reverse order
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
