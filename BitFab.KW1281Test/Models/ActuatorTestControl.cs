using System.Threading.Channels;

namespace BitFab.KW1281Test.Models;

public sealed class ActuatorTestControl
{
    private readonly Channel<bool> _steps = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly CancellationTokenSource _stop = new();

    public bool StopRequested => _stop.IsCancellationRequested;

    public void RequestNext()
    {
        if (!StopRequested)
        {
            _steps.Writer.TryWrite(true);
        }
    }

    public void RequestStop()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }

        _steps.Writer.TryWrite(false);
    }

    public async Task<bool> WaitForNextStepAsync()
    {
        if (StopRequested)
        {
            return false;
        }

        try
        {
            return await _steps.Reader.ReadAsync(_stop.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
