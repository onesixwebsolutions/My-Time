import { HttpErrorResponse } from '@angular/common/http';

/** Normalised view of an RFC 7807 ProblemDetails error (with the contract's `code` / `errors` extensions). */
export interface ApiProblem {
  status: number;
  code: string | null;
  /** Field name (camelCase, matching the request body) -> messages. */
  fieldErrors: Record<string, string[]>;
  title: string | null;
  /** Seconds from a 429 `Retry-After` header, when present. */
  retryAfterSeconds: number | null;
}

/** "Password" / "$.password" / "request.Password" -> "password". */
export function normaliseFieldName(key: string): string {
  const last = key.replace(/^\$\./, '').split('.').pop() ?? key;
  return last.charAt(0).toLowerCase() + last.slice(1);
}

export function toApiProblem(err: unknown): ApiProblem {
  if (!(err instanceof HttpErrorResponse)) {
    return { status: 0, code: null, fieldErrors: {}, title: null, retryAfterSeconds: null };
  }
  const body = err.error && typeof err.error === 'object' ? (err.error as Record<string, unknown>) : {};
  const fieldErrors: Record<string, string[]> = {};
  const errors = body['errors'];
  if (errors && typeof errors === 'object') {
    for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
      const messages = Array.isArray(value) ? value.map(String) : [String(value)];
      const name = normaliseFieldName(key);
      fieldErrors[name] = [...(fieldErrors[name] ?? []), ...messages];
    }
  }
  const retryHeader = err.headers?.get('Retry-After');
  const retry = retryHeader != null ? Number.parseInt(retryHeader, 10) : NaN;
  return {
    status: err.status,
    code: typeof body['code'] === 'string' ? (body['code'] as string) : null,
    fieldErrors,
    title: typeof body['title'] === 'string' ? (body['title'] as string) : null,
    retryAfterSeconds: Number.isFinite(retry) ? retry : null
  };
}

export const GENERIC_ERROR = 'Something went wrong. Please try again.';

/** Message for failures that every form handles the same way (rate limit, network, server). */
export function commonErrorMessage(problem: ApiProblem): string {
  if (problem.status === 429) {
    return problem.retryAfterSeconds
      ? `Too many attempts, try again in ${problem.retryAfterSeconds} s.`
      : 'Too many attempts, try again in a minute.';
  }
  if (problem.status === 0) return 'Could not reach the server. Check your connection and try again.';
  return GENERIC_ERROR;
}
