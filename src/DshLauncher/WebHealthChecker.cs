namespace DshLauncher;

internal static class WebHealthChecker
{
    private static readonly HttpClient Client = new();

    public sealed record ProbeResult(bool Responding, int? StatusCode);

    public static async Task<ProbeResult> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            using var response = await Client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            return new ProbeResult(true, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(false, null);
        }
        catch (HttpRequestException)
        {
            return new ProbeResult(false, null);
        }
        catch (InvalidOperationException)
        {
            return new ProbeResult(false, null);
        }
    }
}
