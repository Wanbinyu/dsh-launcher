namespace DshLauncher;

internal readonly record struct StartRequest(Task<StartResult> Completion, bool IsOwner);

internal sealed class StartCoordinator
{
    private readonly Func<Task<StartResult>> _start;
    private readonly Action<Uri?> _openBrowser;
    private Task<StartResult>? _activeOperation;
    private bool _openRequested;

    public StartCoordinator(Func<Task<StartResult>> start, Action<Uri?> openBrowser)
    {
        _start = start;
        _openBrowser = openBrowser;
    }

    public StartRequest Request(bool openBrowser)
    {
        if (_activeOperation is { IsCompleted: false })
        {
            _openRequested |= openBrowser;
            return new StartRequest(_activeOperation, IsOwner: false);
        }

        _openRequested = openBrowser;
        _activeOperation = RunAsync();
        return new StartRequest(_activeOperation, IsOwner: true);
    }

    private async Task<StartResult> RunAsync()
    {
        var result = await _start();
        if (result.Ready && _openRequested)
        {
            _openBrowser(result.LaunchUrl);
        }

        return result;
    }
}
