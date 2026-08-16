using System.IO.Pipes;

namespace DshLauncher;

internal static class ControlClient
{
    public static async Task<string?> TrySendAsync(
        string pipeName,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            return await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
