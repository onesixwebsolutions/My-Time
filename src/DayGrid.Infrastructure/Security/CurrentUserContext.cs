using System.Security.Claims;
using DayGrid.Application.Security;
using Microsoft.AspNetCore.Http;

namespace DayGrid.Infrastructure.Security;

/// <summary>
/// Scoped <see cref="ICurrentUser"/>: the authenticated user of the current HTTP request, unless
/// a background job has explicitly switched the scope to a user with <see cref="ActAs"/>.
/// </summary>
public sealed class CurrentUserContext : ICurrentUser
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private Guid? _actAs;

    public CurrentUserContext(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId => _actAs ?? FromPrincipal(_httpContextAccessor?.HttpContext?.User);

    /// <summary>Makes this scope act for <paramref name="userId"/> (background jobs only).</summary>
    public void ActAs(Guid userId) => _actAs = userId;

    public static Guid? FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
