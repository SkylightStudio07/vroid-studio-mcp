using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

/// <summary>Drives a managed iterator (with nested iterators) one step per frame.</summary>
public sealed class CoroutineDriver
{
    private readonly List<IEnumerator> _stack = new();

    public bool Running => _stack.Count > 0;
    public System.Exception LastError { get; private set; }

    public void Start(IEnumerator routine)
    {
        _stack.Clear();
        LastError = null;
        _stack.Add(routine);
    }

    public void Tick()
    {
        if (_stack.Count == 0) return;
        var top = _stack[^1];
        bool moved;
        try { moved = top.MoveNext(); }
        catch (System.Exception e) { LastError = e; PluginLog.LogError($"coroutine error: {e.Message}"); _stack.Clear(); return; }

        if (moved)
        {
            if (top.Current is IEnumerator nested) _stack.Add(nested);
        }
        else
        {
            _stack.RemoveAt(_stack.Count - 1);
        }
    }
}
