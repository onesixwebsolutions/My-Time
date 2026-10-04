using Microsoft.AspNetCore.Http.Features;

namespace DayGrid.Api.Security;

/// <summary>
/// Caps request bodies (default 1 MB, config <c>Limits:MaxRequestBodyBytes</c>). Kestrel enforces
/// the same limit itself (<c>KestrelServerLimits.MaxRequestBodySize</c>, also for chunked bodies);
/// this middleware answers an oversized declared Content-Length with a 413 problem up front and
/// applies the limit on hosts that are not Kestrel.
/// </summary>
public sealed class RequestBodyLimitMiddleware
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly RequestDelegate _next;
    private readonly long _maxBytes;

    public RequestBodyLimitMiddleware(RequestDelegate next, long maxBytes)
    {
        _next = next;
        _maxBytes = maxBytes;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength is { } length && length > _maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new { type = "about:blank", title = "The request body is too large.", status = 413, code = "payload_too_large" },
                options: null, contentType: "application/problem+json");
            return;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature
            && (feature.MaxRequestBodySize is null || feature.MaxRequestBodySize > _maxBytes))
            feature.MaxRequestBodySize = _maxBytes;

        await _next(context);
    }
}
