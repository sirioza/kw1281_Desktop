namespace BitFab.KW1281Test.Actions;

internal sealed class ScopedEvent<T>
{
    private readonly AsyncLocal<Scope?> _currentScope = new();
    private Action<T>? _globalHandlers;

    public IDisposable BeginScope()
    {
        Scope? scope = new(_currentScope.Value, this);
        _currentScope.Value = scope;

        return scope;
    }

    public void Add(Action<T>? handler)
    {
        Scope? scope = _currentScope.Value;
        if (scope != null)
        {
            scope.Handlers += handler;
        }
        else
        {
            _globalHandlers += handler;
        }
    }

    public void Remove(Action<T>? handler)
    {
        Scope? scope = _currentScope.Value;
        if (scope != null)
        {
            scope.Handlers -= handler;
        }
        else
        {
            _globalHandlers -= handler;
        }
    }

    public void Invoke(T value)
    {
        var handler = _currentScope.Value?.Handlers ?? _globalHandlers;
        handler?.Invoke(value);
    }

    private sealed class Scope(Scope? parent, ScopedEvent<T> owner) : IDisposable
    {
        private readonly Scope? _parent = parent;
        private readonly ScopedEvent<T> _owner = owner;
        private bool _disposed;

        public Action<T>? Handlers { get; set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner._currentScope.Value = _parent;
            _disposed = true;
        }
    }
}
