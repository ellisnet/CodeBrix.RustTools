using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.Tests.Helpers;

/// <summary>
/// An HttpMessageHandler that answers from a route table (absolute-path suffix -> response) and
/// records every request with its User-Agent.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string PathSuffix, HttpStatusCode Status, string Body)> _routes = new();

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Respond(string pathSuffix, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((pathSuffix, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userAgent = request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" ", values) : null;
        Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri, userAgent));

        var route = _routes.FirstOrDefault(r => request.RequestUri.AbsolutePath.EndsWith(r.PathSuffix, StringComparison.Ordinal));
        var response = route.PathSuffix == null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"errors\":[{\"detail\":\"Not Found\"}]}") }
            : new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body ?? string.Empty, Encoding.UTF8) };
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string UserAgent);
}
