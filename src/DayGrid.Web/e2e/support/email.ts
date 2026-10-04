import * as fs from 'node:fs';
import * as path from 'node:path';

// Reads the .eml files the API writes in Email:Mode=Pickup (see global-setup.ts). Just enough
// MIME to find links: headers, multipart bodies, quoted-printable and base64 parts.

export interface Eml {
  file: string;
  to: string;
  subject: string;
  /** Decoded text of every body part, concatenated. */
  body: string;
}

function splitHeaders(raw: string): { headers: Record<string, string>; body: string } {
  const idx = raw.search(/\r?\n\r?\n/);
  const head = idx >= 0 ? raw.slice(0, idx) : raw;
  const body = idx >= 0 ? raw.slice(idx).replace(/^\r?\n\r?\n/, '') : '';
  const headers: Record<string, string> = {};
  for (const line of head.replace(/\r?\n[ \t]+/g, ' ').split(/\r?\n/)) {
    const colon = line.indexOf(':');
    if (colon > 0) headers[line.slice(0, colon).trim().toLowerCase()] = line.slice(colon + 1).trim();
  }
  return { headers, body };
}

function decodeQuotedPrintable(text: string): string {
  const bytes: number[] = [];
  const soft = text.replace(/=\r?\n/g, '');
  for (let i = 0; i < soft.length; i++) {
    const ch = soft[i];
    if (ch === '=' && /^[0-9A-Fa-f]{2}$/.test(soft.slice(i + 1, i + 3))) {
      bytes.push(parseInt(soft.slice(i + 1, i + 3), 16));
      i += 2;
    } else {
      bytes.push(...Buffer.from(ch, 'utf8'));
    }
  }
  return Buffer.from(bytes).toString('utf8');
}

/** RFC 2047 encoded words (=?utf-8?B?...?= / =?utf-8?Q?...?=) in headers. */
function decodeHeader(value: string): string {
  return value.replace(/=\?([^?]+)\?([BbQq])\?([^?]*)\?=/g, (_m, _charset, enc: string, data: string) =>
    enc.toUpperCase() === 'B' ? Buffer.from(data, 'base64').toString('utf8') : decodeQuotedPrintable(data.replace(/_/g, ' '))
  );
}

function decodePart(raw: string): string {
  const { headers, body } = splitHeaders(raw);
  const type = headers['content-type'] ?? 'text/plain';
  const boundary = /boundary="?([^";]+)"?/i.exec(type)?.[1];
  if (/^multipart\//i.test(type) && boundary) {
    return body
      .split(`--${boundary}`)
      .slice(1)
      .filter((part) => !part.startsWith('--'))
      .map((part) => decodePart(part.replace(/^\r?\n/, '')))
      .join('\n');
  }
  const encoding = (headers['content-transfer-encoding'] ?? '').toLowerCase();
  if (encoding === 'base64') return Buffer.from(body.replace(/\s+/g, ''), 'base64').toString('utf8');
  if (encoding === 'quoted-printable') return decodeQuotedPrintable(body);
  return body;
}

export function readEml(file: string): Eml {
  const raw = fs.readFileSync(file, 'utf8');
  const { headers } = splitHeaders(raw);
  return {
    file,
    to: decodeHeader(headers['to'] ?? ''),
    subject: decodeHeader(headers['subject'] ?? ''),
    body: decodePart(raw)
  };
}

/** All emails in the pickup folder, oldest first (file names start with a UTC timestamp). */
export function listEmails(pickupDir: string): Eml[] {
  if (!fs.existsSync(pickupDir)) return [];
  return fs
    .readdirSync(pickupDir)
    .filter((f) => f.endsWith('.eml'))
    .sort()
    .map((f) => readEml(path.join(pickupDir, f)));
}

function decodeEntities(s: string): string {
  return s.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>');
}

/** Absolute links (http/https) in an email body, entity-decoded. */
export function linksIn(email: Eml): string[] {
  const found = new Set<string>();
  for (const m of email.body.matchAll(/https?:\/\/[^\s"'<>]+/g)) found.add(decodeEntities(m[0]));
  return [...found];
}

/**
 * Waits for the newest email to `to` (case-insensitive) that contains a link whose path is
 * `linkPath` and that arrived after `since` emails (pass the count from countEmails() taken
 * before the action that sends it). Returns that link.
 */
export async function waitForLink(
  pickupDir: string,
  to: string,
  linkPath: string,
  options: { since?: number; timeoutMs?: number } = {}
): Promise<string> {
  const deadline = Date.now() + (options.timeoutMs ?? 15_000);
  for (;;) {
    const emails = listEmails(pickupDir).slice(options.since ?? 0);
    for (const email of emails.reverse()) {
      if (!email.to.toLowerCase().includes(to.toLowerCase())) continue;
      const link = linksIn(email).find((l) => new URL(l).pathname === linkPath);
      if (link) return link;
    }
    if (Date.now() > deadline) {
      throw new Error(`No email to ${to} with a ${linkPath} link arrived in ${pickupDir}`);
    }
    await new Promise((r) => setTimeout(r, 200));
  }
}

export function countEmails(pickupDir: string): number {
  return fs.existsSync(pickupDir) ? fs.readdirSync(pickupDir).filter((f) => f.endsWith('.eml')).length : 0;
}
