namespace DshLauncher;

internal static class WebHealthChecker
{
    private static readonly HttpClient Client = new();

    public sealed record ProbeResult(bool Responding, int? StatusCode)
    {
        public bool RequiresAuthentication => StatusCode == 401;
    }

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
            var statusCode = (int)response.StatusCode;
            return new ProbeResult(statusCode is >= 200 and < 400, statusCode);
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
