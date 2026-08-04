using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DungeonDrip.Data;

/// <summary>
/// The plugin's single outbound HTTP client.
/// </summary>
/// <remarks>
/// One client rather than one per source, so the identifying User-Agent and the safety limits are
/// stated once. Every response is read through a hard byte ceiling: both sources are third-party
/// hosts, and Content-Length is advisory - a chunked response can claim nothing and stream forever,
/// which an unbounded ReadAsStringAsync would happily buffer into memory.
/// </remarks>
public sealed class HttpFetcher : IDisposable
{
    /// <summary>
    /// One response. <see cref="Body"/> is null exactly when <see cref="NotModified"/> - there is
    /// nothing to read on a 304, and the ETag comes back unchanged so the caller can re-store it.
    /// </summary>
    public readonly record struct Response(bool NotModified, string? Body, string? ETag);

    private readonly HttpClient http;

    public HttpFetcher()
    {
        http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            // Per-request timeouts are applied with a linked token; this is only a backstop.
            Timeout = TimeSpan.FromMinutes(2),
        };

        var version = typeof(HttpFetcher).Assembly.GetName().Version?.ToString(3) ?? "0";
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DungeonDrip/{version} (Dalamud plugin; https://github.com/Spibane/DungeonDrip)");
    }

    /// <summary>
    /// Fetches one URL, revalidating with an ETag and refusing to buffer more than it was told to.
    /// </summary>
    /// <remarks>
    /// The timeout is a linked token rather than the client's, so one slow host cannot hold up
    /// another's request, and headers are read before the body so an over-large response can be
    /// refused on its Content-Length before a byte of it is buffered.
    /// </remarks>
    /// <param name="etag">Sent as If-None-Match; a 304 comes back as <see cref="Response.NotModified"/>.</param>
    /// <param name="maxBytes">Hard ceiling on the body; anything larger is an error, not a truncation.</param>
    public async Task<Response> GetAsync(
        string url,
        string? etag,
        TimeSpan timeout,
        int maxBytes,
        CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

        if (response.StatusCode == HttpStatusCode.NotModified)
            return new Response(true, null, etag);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidOperationException($"response declares {response.Content.Headers.ContentLength} bytes");

        var body = await ReadCappedAsync(response, maxBytes, deadline.Token);
        return new Response(false, body, response.Headers.ETag?.Tag);
    }

    public void Dispose() => http.Dispose();

    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, int maxBytes, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffered = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffered.Length + read > maxBytes)
                throw new InvalidOperationException($"response exceeded {maxBytes} bytes");

            buffered.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffered.GetBuffer(), 0, (int)buffered.Length);
    }
}
