using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DayGrid.Api.Auth;

/// <summary>
/// Mirrors Identity's lockout for sign-in attempts against email addresses that have no account,
/// so "locked_out" after N failures cannot be used to tell real accounts from unknown ones: after
/// <see cref="LockoutOptions.MaxFailedAccessAttempts"/> failures an unknown email is "locked" for
/// <see cref="LockoutOptions.DefaultLockoutTimeSpan"/>, exactly like a real account.
/// Kept per normalized email in a size-bounded memory cache (per instance; a restart forgets it,
/// as it forgets nothing important). The failure count expires after a day without failures.
/// </summary>
public sealed class UnknownAccountLockout : IDisposable
{
    private const int MaxTrackedEmails = 100_000;
    private static readonly TimeSpan FailureMemory = TimeSpan.FromDays(1);

    private readonly MemoryCache _cache;
    private readonly IOptions<IdentityOptions> _identityOptions;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public UnknownAccountLockout(IOptions<IdentityOptions> identityOptions, TimeProvider time)
    {
        _identityOptions = identityOptions;
        _time = time;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = MaxTrackedEmails });
    }

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset? LockedUntil;
    }

    /// <summary>Whether <paramref name="normalizedEmail"/> is currently "locked out".</summary>
    public bool IsLockedOut(string normalizedEmail)
    {
        lock (_gate)
            return _cache.TryGetValue(normalizedEmail, out Entry? entry) && entry!.LockedUntil > _time.GetUtcNow();
    }

    /// <summary>Records a failed attempt; returns true when this failure starts a lockout (same
    /// moment Identity locks a real account: the Nth consecutive failure).</summary>
    public bool RecordFailure(string normalizedEmail)
    {
        var lockout = _identityOptions.Value.Lockout;
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (!_cache.TryGetValue(normalizedEmail, out Entry? entry) || entry is null)
                entry = new Entry();

            var lockedNow = false;
            entry.Failures++;
            if (entry.Failures >= Math.Max(1, lockout.MaxFailedAccessAttempts))
            {
                // Identity resets the counter when it locks an account out.
                entry.Failures = 0;
                entry.LockedUntil = now + lockout.DefaultLockoutTimeSpan;
                lockedNow = true;
            }

            _cache.Set(normalizedEmail, entry, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = FailureMemory
            });
            return lockedNow;
        }
    }

    public void Dispose() => _cache.Dispose();
}
