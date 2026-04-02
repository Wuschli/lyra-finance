namespace Lyra.Core.EnableBanking;

/// <summary>
/// A <see cref="DelegatingHandler"/> that logs all outgoing HTTP requests and incoming responses
/// per async execution context. Uses <see cref="AsyncLocal{T}"/> so concurrent syncs on different
/// connections never interfere with each other — each async call tree sees only its own logger.
/// </summary>
public class SyncRequestLoggingHandler : DelegatingHandler
{
    public SyncRequestLoggingHandler() : base(new HttpClientHandler())
    {
    }

    private static readonly AsyncLocal<Action<string>?> _logAction = new();

    /// <summary>
    /// Registers a log callback for the current async execution context.
    /// </summary>
    public static void SetLogAction(Action<string> action) => _logAction.Value = action;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var logAction = _logAction.Value;
        if (logAction != null)
        {
            var requestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : "(no body)";

            // Log request headers + body
            var requestHeaders = string.Join("\n", request.Headers.Select(h => $"  {h.Key}: {string.Join(", ", h.Value)}"));
            if (request.Content?.Headers != null)
                requestHeaders += "\n" + string.Join("\n", request.Content.Headers.Select(h => $"  {h.Key}: {string.Join(", ", h.Value)}"));

            logAction($"→ {request.Method} {request.RequestUri}\n{requestHeaders}\n{requestBody}");
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (logAction != null)
        {
            var responseBody = response.Content != null
                ? await response.Content.ReadAsStringAsync(cancellationToken)
                : "(no body)";

            // Log response status + headers + body
            var responseHeaders = string.Join("\n", response.Headers.Select(h => $"  {h.Key}: {string.Join(", ", h.Value)}"));
            if (response.Content?.Headers != null)
                responseHeaders += "\n" + string.Join("\n", response.Content.Headers.Select(h => $"  {h.Key}: {string.Join(", ", h.Value)}"));

            logAction($"← {(int)response.StatusCode} {response.ReasonPhrase}\n{responseHeaders}\n{responseBody}");
        }

        return response;
    }
}