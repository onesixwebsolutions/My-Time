import { EmailLinkService, parseEmailLinkParams } from './email-link.service';

describe('parseEmailLinkParams', () => {
  it('reads the fragment (new links)', () => {
    expect(parseEmailLinkParams('#userId=u-1&token=abc_-1', '', ['userId', 'token'])).toEqual({ userId: 'u-1', token: 'abc_-1' });
  });

  it('falls back to the query string (links mailed by older versions)', () => {
    expect(parseEmailLinkParams('', '?email=a%40b.co&token=t', ['email', 'token'])).toEqual({ email: 'a@b.co', token: 't' });
  });

  it('prefers the fragment and keeps encoded + and & in an email address', () => {
    expect(parseEmailLinkParams('#email=a%2Bx%26y%40b.co&token=new', '?email=old%40b.co&token=old', ['email', 'token'])).toEqual({
      email: 'a+x&y@b.co',
      token: 'new'
    });
  });

  it('returns null for missing values', () => {
    expect(parseEmailLinkParams('', '', ['userId', 'token'])).toEqual({ userId: null, token: null });
  });
});

describe('EmailLinkService', () => {
  let original: string;

  beforeEach(() => (original = window.location.pathname + window.location.search + window.location.hash));
  afterEach(() => window.history.replaceState(window.history.state, '', original));

  it('returns the parameters and removes them (and the token) from the address bar', () => {
    window.history.replaceState(window.history.state, '', '/confirm-email?legacy=1#userId=u-9&token=secret-token');
    const params = new EmailLinkService().take(['userId', 'token']);
    expect(params).toEqual({ userId: 'u-9', token: 'secret-token' });
    expect(window.location.href).not.toContain('secret-token');
    expect(window.location.hash).toBe('');
    expect(window.location.search).toBe('');
    expect(window.location.pathname).toBe('/confirm-email');
  });
});
