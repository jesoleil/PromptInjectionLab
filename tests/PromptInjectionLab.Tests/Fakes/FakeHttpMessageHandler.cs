using System.Net;
using System.Text;

namespace PromptInjectionLab.Tests.Fakes;

/// <summary>
/// In-memory HttpMessageHandler for tests — no network. Records every request
/// (method, URI, headers, body) and answers with canned responses or throws.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public sealed record Captured(HttpMethod Method, Uri? Uri, string Body, Dictionary<string, string> Headers);

    public List<Captured> Requests { get; } = new();

    public static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static FakeHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => Response(json, status));

    /// <summary>Answers with each factory in turn; the last one repeats.</summary>
    public static FakeHttpMessageHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        int i = 0;
        return new(_ => responses[Math.Min(i++, responses.Length - 1)]());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new Captured(request.Method, request.RequestUri, body, headers));
        return _responder(request);
    }
}
