using DayGrid.Api.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace DayGrid.Api.Security;

/// <summary>
/// Validates the antiforgery token (X-XSRF-TOKEN header + antiforgery cookie, bound to the current
/// user) on every unsafe /api request — including login and register. Runs after authorization,
/// so an anonymous call to a protected endpoint still gets 401 rather than 400.
/// </summary>
public sealed class AntiforgeryValidationMiddleware
{
    private readonly RequestDelegate _next;

    public AntiforgeryValidationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var method = context.Request.Method;
        if (context.Request.Path.StartsWithSegments("/api")
            && !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                // Re-issue a valid token for the current caller so a retry can succeed (e.g. a token
                // that was minted before the session expired).
                AuthSupport.IssueXsrfCookie(context);
                await AuthSetup.WriteProblemAsync(context, StatusCodes.Status400BadRequest, "antiforgery",
                    "The antiforgery token is missing or invalid. Reload the page and try again.");
                return;
            }
        }

        await _next(context);
    }
}
