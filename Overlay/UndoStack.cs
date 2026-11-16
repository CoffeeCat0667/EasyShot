namespace EasyShot.Overlay;

internal sealed class UndoStack
{
    private readonly List<(Action Undo, Action Redo)> _commands = new();
    private int _position;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _commands.Count;

    // 调用方已完成编辑；这里只记录对应的撤销与重做。
    public void Record(Action undo, Action redo)
    {
        _commands.RemoveRange(_position, _commands.Count - _position);
        _commands.Add((undo, redo));
        if (_commands.Count > 20)
        {
            _commands.RemoveAt(0);
        }

        _position = _commands.Count;
    }

    public void Undo()
    {
        if (CanUndo)
        {
            _commands[--_position].Undo();
        }
    }

    public void Redo()
    {
        if (CanRedo)
        {
            _commands[_position++].Redo();
        }
    }
}
