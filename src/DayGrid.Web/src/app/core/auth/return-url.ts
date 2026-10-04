/**
 * Open-redirect protection for `?returnUrl=`: only same-origin, app-relative paths are allowed —
 * must start with a single "/", must not be protocol-relative ("//host") or use backslashes
 * ("/\\host", which browsers treat like "//"), and must not contain control characters.
 * Anything else falls back to `fallback`.
 */
export function sanitizeReturnUrl(value: string | null | undefined, fallback = '/'): string {
  if (!value || typeof value !== 'string') return fallback;
  if (!value.startsWith('/') || value.startsWith('//')) return fallback;
  if (value.includes('\\')) return fallback;
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(value)) return fallback;
  try {
    const base = 'https://daygrid.invalid';
    const parsed = new URL(value, base);
    if (parsed.origin !== base) return fallback;
    const path = parsed.pathname + parsed.search + parsed.hash;
    // Never bounce back onto the login page itself.
    if (parsed.pathname === '/login') return fallback;
    return path;
  } catch {
    return fallback;
  }
}

/** Public (anonymous) SPA routes from the contract — the auth layout pages. */
const PUBLIC_PATHS = ['/login', '/register', '/register/check-email', '/confirm-email', '/forgot-password', '/reset-password'];

export function isPublicAuthUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return PUBLIC_PATHS.includes(path);
}
