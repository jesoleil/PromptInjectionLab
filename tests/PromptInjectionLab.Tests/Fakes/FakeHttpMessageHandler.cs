using System.Net;
using System.Text;

namespace PromptInjectionLab.Tests.Fakes;

/// <summary>
/// In-memory HttpMessageHandler for tests — no network. Records every request
/// (method, URI, body) and answers with a canned response or throws.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<(HttpMethod Method, Uri? Uri, string Body)> Requests { get; } = new();

    public static FakeHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri, body));
        return _responder(request);
    }
}
