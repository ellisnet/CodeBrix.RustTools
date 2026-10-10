using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.Internal;

/// <summary>
/// Chooses the HttpClient for the registry clients (the options' client, a client over the
/// options' handler, or one shared default client) and sends GET requests with the
/// User-Agent header the services require.
/// </summary>
internal sealed class HttpClientSource
{
    private static readonly Lazy<HttpClient> SharedDefault = new Lazy<HttpClient>(
        () => new HttpClient { Timeout = TimeSpan.FromSeconds(100) },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly HttpClient _client;
    private readonly string _userAgent;

    /// <summary>
    /// Creates the source from the library options.
    /// </summary>
    public HttpClientSource(RustWorkbenchOptions options)
    {
        options ??= new RustWorkbenchOptions();
        _userAgent = options.EffectiveUserAgent;
        if (options.HttpClient != null)
        {
            _client = options.HttpClient;
        }
        else if (options.HttpMessageHandler != null)
        {
            _client = new HttpClient(options.HttpMessageHandler, disposeHandler: false);
        }
        else
        {
            _client = SharedDefault.Value;
        }
    }

    /// <summary>
    /// Sends a GET request for <paramref name="uri"/> with the User-Agent and Accept headers.
    /// The caller disposes the response.
    /// </summary>
    /// <exception cref="RustToolsException">The request failed at the network level or timed out.</exception>
    public async Task<HttpResponseMessage> GetAsync(Uri uri, string accept, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        if (!string.IsNullOrEmpty(accept))
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        try
        {
            return await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RustToolsException($"The request to {uri} failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RustToolsException($"The request to {uri} timed out.", ex);
        }
    }

    /// <summary>
    /// GETs <paramref name="uri"/> and returns the body text, throwing for non-success codes.
    /// </summary>
    /// <exception cref="RustToolsException">The request failed or returned a non-success status.</exception>
    public async Task<string> GetStringAsync(Uri uri, string accept, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(uri, accept, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new RustToolsException($"The request to {uri} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        return body;
    }
}
