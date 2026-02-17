namespace PDFEditor.Core.Services;

/// <summary>
/// Represents an undoable/redoable operation on a document
/// </summary>
public class UndoableAction
{
    public string Description { get; set; } = string.Empty;
    public byte[] StateBefore { get; set; } = Array.Empty<byte>();
    public byte[] StateAfter { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Manages undo/redo history for PDF operations.
/// Each entry stores the full PDF state before and after the operation
/// for guaranteed correctness.
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<UndoableAction> _undoStack = new();
    private readonly Stack<UndoableAction> _redoStack = new();
    private const int MaxHistoryDepth = 20;

    /// <summary>
    /// Records an operation that can be undone
    /// </summary>
    public void RecordAction(string description, byte[] stateBefore, byte[] stateAfter)
    {
        _undoStack.Push(new UndoableAction
        {
            Description = description,
            StateBefore = stateBefore,
            StateAfter = stateAfter
        });

        // Clear redo stack on new action (can't redo after new action)
        _redoStack.Clear();

        // Limit history depth to prevent excessive memory usage
        if (_undoStack.Count > MaxHistoryDepth)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = Math.Min(items.Length - 1, MaxHistoryDepth - 1); i >= 0; i--)
                _undoStack.Push(items[i]);
        }
    }

    /// <summary>
    /// Undoes the last operation, returns the restored state
    /// </summary>
    public byte[]? Undo()
    {
        if (_undoStack.Count == 0) return null;

        var action = _undoStack.Pop();
        _redoStack.Push(action);
        return action.StateBefore;
    }

    /// <summary>
    /// Redoes the last undone operation, returns the restored state
    /// </summary>
    public byte[]? Redo()
    {
        if (_redoStack.Count == 0) return null;

        var action = _redoStack.Pop();
        _undoStack.Push(action);
        return action.StateAfter;
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    /// <summary>
    /// Gets a list of all undo history descriptions (most recent first)
    /// </summary>
    public List<string> UndoHistoryDescriptions =>
        _undoStack.Select(a => a.Description).ToList();

    /// <summary>
    /// Gets a list of all redo history descriptions (most recent first)
    /// </summary>
    public List<string> RedoHistoryDescriptions =>
        _redoStack.Select(a => a.Description).ToList();

    /// <summary>
    /// Undo to a specific point in history (1-based index from top)
    /// </summary>
    public byte[]? UndoTo(int steps)
    {
        byte[]? result = null;
        for (int i = 0; i < steps && CanUndo; i++)
            result = Undo();
        return result;
    }

    /// <summary>
    /// Clears all history
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
