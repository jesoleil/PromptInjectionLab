using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Shared HTTP execution for all provider adapters: per-attempt timeout,
/// caller cancellation, error classification, retries with exponential
/// backoff (honouring Retry-After) and secret redaction of error text.
///
/// Retried: 429, 408, 5xx (incl. Anthropic 529) and network errors.
/// Not retried: timeouts (a timed-out request may still be billed), 4xx
/// client errors (bad request, auth, not found) and caller cancellation.
/// </summary>
public sealed class HttpLlmTransport
{
    public sealed record Result(bool Ok, int? Status, string Body, LlmErrorKind Kind, string? Error, int Attempts, long ElapsedMs);

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">Injected wait used between retries (tests pass a no-op).</param>
    public HttpLlmTransport(HttpClient http, ILogger logger, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _logger = logger;
        _delay = delay ?? Task.Delay;
    }

    /// <param name="createRequest">Builds a fresh request per attempt (requests cannot be re-sent).</param>
    /// <param name="secret">API key to scrub from any error text.</param>
    public async Task<Result> SendAsync(Func<HttpRequestMessage> createRequest, ProviderOptions o, string provider,
        string? secret, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        for (int attempt = 1; ; attempt++)
        {
            LlmErrorKind kind;
            string error;
            int? status = null;
            TimeSpan? retryAfter = null;

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (o.TimeoutSeconds > 0) attemptCts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));

            try
            {
                using var req = createRequest();
                using var resp = await _http.SendAsync(req, attemptCts.Token);
                status = (int)resp.StatusCode;
                var body = await resp.Content.ReadAsStringAsync(attemptCts.Token);
                if (resp.IsSuccessStatusCode)
                    return new Result(true, status, body, LlmErrorKind.None, null, attempt, sw.ElapsedMilliseconds);

                kind = Classify(resp.StatusCode);
                retryAfter = RetryAfter(resp);
                error = $"HTTP {status} {resp.ReasonPhrase}: {ExtractErrorMessage(body)}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new Result(false, null, "", LlmErrorKind.Cancelled, "cancelled", attempt, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{Provider} request timed out after {Seconds}s (attempt {Attempt})", provider, o.TimeoutSeconds, attempt);
                return new Result(false, null, "", LlmErrorKind.Timeout, "timeout", attempt, sw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                kind = LlmErrorKind.NetworkError;
                error = ex.Message;
            }

            error = Truncate(SecretRedactor.Redact(error, secret), 500);

            if (!IsRetryable(kind) || attempt > o.MaxRetries)
            {
                _logger.LogWarning("{Provider} request failed ({Kind}): {Error}", provider, kind, error);
                return new Result(false, status, "", kind, error, attempt, sw.ElapsedMilliseconds);
            }

            var wait = NextDelay(o, attempt, retryAfter);
            _logger.LogWarning("{Provider} {Kind} (attempt {Attempt}/{Max}); retrying in {Ms} ms",
                provider, kind, attempt, o.MaxRetries + 1, (long)wait.TotalMilliseconds);
            try
            {
                await _delay(wait, ct);
            }
            catch (OperationCanceledException)
            {
                return new Result(false, status, "", LlmErrorKind.Cancelled, "cancelled", attempt, sw.ElapsedMilliseconds);
            }
        }
    }

    public static LlmErrorKind Classify(HttpStatusCode code) => (int)code switch
    {
        401 or 403 => LlmErrorKind.AuthenticationFailed,
        404 => LlmErrorKind.NotFound,
        429 => LlmErrorKind.RateLimited,
        408 => LlmErrorKind.ServerError,
        >= 500 => LlmErrorKind.ServerError,
        _ => LlmErrorKind.BadRequest,
    };

    public static bool IsRetryable(LlmErrorKind kind) =>
        kind is LlmErrorKind.RateLimited or LlmErrorKind.ServerError or LlmErrorKind.NetworkError;

    public static TimeSpan NextDelay(ProviderOptions o, int attempt, TimeSpan? retryAfter)
    {
        var cap = TimeSpan.FromSeconds(Math.Max(0, o.MaxRetryDelaySeconds));
        var wait = retryAfter ?? TimeSpan.FromMilliseconds(Math.Max(0, o.RetryBaseDelayMs) * Math.Pow(2, attempt - 1));
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        return wait > cap ? cap : wait;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra?.Delta is { } d) return d;
        if (ra?.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }

    /// <summary>Pulls a human-readable message out of the usual provider error bodies.</summary>
    public static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(empty body)";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? "";
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m) &&
                    m.ValueKind == JsonValueKind.String)
                {
                    var type = err.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    return type is null ? m.GetString() ?? "" : $"{type}: {m.GetString()}";
                }
            }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
                return msg.GetString() ?? "";
        }
        catch (JsonException) { }
        return Truncate(body.Trim(), 300);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
