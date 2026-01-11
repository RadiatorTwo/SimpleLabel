namespace SimpleLabel.Commands;

/// <summary>
/// Manages undo/redo stacks for command-based operations
/// </summary>
public class CommandManager
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();

    /// <summary>
    /// Execute a command and add it to the undo stack
    /// </summary>
    public void ExecuteCommand(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear(); // Clear redo stack when new command is executed (branching timeline)
    }

    /// <summary>
    /// Add a command to the undo stack WITHOUT executing it.
    /// Use when the action has already been performed (e.g., during drag operations).
    /// </summary>
    public void AddExecutedCommand(ICommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    /// <summary>
    /// Undo the last command
    /// </summary>
    public void Undo()
    {
        if (_undoStack.Count > 0)
        {
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
        }
    }

    /// <summary>
    /// Redo the last undone command
    /// </summary>
    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
        }
    }

    /// <summary>
    /// Check if undo is available
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Check if redo is available
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Clear both undo and redo stacks (used on New/Open)
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
