namespace SimpleLabel.Commands;

/// <summary>
/// Interface for undoable/redoable commands
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Execute the command (perform the operation)
    /// </summary>
    void Execute();

    /// <summary>
    /// Undo the command (reverse the operation)
    /// </summary>
    void Undo();
}
