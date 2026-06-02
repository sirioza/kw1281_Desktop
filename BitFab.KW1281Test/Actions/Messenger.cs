using BitFab.KW1281Test.Actions.Records;
using System.Drawing;

namespace BitFab.KW1281Test.Actions;

public sealed class Messenger
{
    private static readonly Messenger _instance = new();
    private readonly ScopedEvent<TextLine> _messageReceived = new();

    private Messenger()
    {
    }

    public static Messenger Instance => _instance;

    public event Action<TextLine>? MessageReceived
    {
        add => _messageReceived.Add(value);
        remove => _messageReceived.Remove(value);
    }

    public IDisposable BeginScope() => _messageReceived.BeginScope();

    public void Add(string message) => Add(message, Color.Black);

    public void Add(string message, Color color) => _messageReceived.Invoke(new() { Text = message, TextColor = color });

    public void AddLine() =>AddLine(string.Empty);

    public void AddLine(string message) => AddLine(message, Color.Black);

    public void AddLine(string message, Color color) => Add(message + Environment.NewLine, color);
}
