using System.Security.Claims;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DayGrid.Api.Auth;

/// <summary>
/// Sign-in manager that treats an account disabled by an administrator as unable to sign in —
/// both for new sign-ins and when an existing session cookie is re-validated (defence in depth:
/// disabling also rotates the security stamp, which already invalidates every session).
/// </summary>
public sealed class AppSignInManager : SignInManager<AppUser>
{
    public AppSignInManager(
        UserManager<AppUser> userManager, IHttpContextAccessor contextAccessor, IUserClaimsPrincipalFactory<AppUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor, ILogger<SignInManager<AppUser>> logger, IAuthenticationSchemeProvider schemes,
        IUserConfirmation<AppUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<bool> CanSignInAsync(AppUser user) =>
        !user.IsDisabled && await base.CanSignInAsync(user);

    public override async Task<AppUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { IsDisabled: true } ? null : user;
    }
}
