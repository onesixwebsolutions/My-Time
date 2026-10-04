import { Injectable } from '@angular/core';

/**
 * Parses the parameters of an emailed account link. New links carry them in the URL fragment
 * (`/confirm-email#userId=..&token=..`): browsers never send the fragment to a server, so the
 * token stays out of access logs, proxies and Referer headers. Links mailed by older versions
 * used the query string (`?userId=..&token=..`) and are still accepted. The fragment wins.
 * Values are read from the raw (still percent-encoded) strings so `+`, `&` or `%` in an email
 * address survive.
 */
export function parseEmailLinkParams(hash: string, search: string, names: readonly string[]): Record<string, string | null> {
  const fragment = new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
  const query = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search);
  const result: Record<string, string | null> = {};
  for (const name of names) {
    result[name] = fragment.get(name) || query.get(name) || null;
  }
  return result;
}

/** Reads an account link's parameters once, then removes them from the address bar. */
@Injectable({ providedIn: 'root' })
export class EmailLinkService {
  /**
   * Returns the named parameters of the current URL (fragment first, then query) and replaces
   * the address-bar URL with the bare path — the token must not linger in history, bookmarks or
   * a screenshot. history.state is kept so the Angular router's navigation state is untouched.
   */
  take(names: readonly string[]): Record<string, string | null> {
    const { hash, search, pathname } = window.location;
    const params = parseEmailLinkParams(hash, search, names);
    if (hash || search) {
      window.history.replaceState(window.history.state, '', pathname);
    }
    return params;
  }
}
