import { expect, test } from './support/fixtures';

// The SPA's SignalrService exists but no page subscribes to it yet (Phase 5), so the bundle
// doesn't open a hub connection on its own. This drives the same wire protocol from inside
// the page (same origin, same cookies) to prove /hubs/schedule negotiates and completes the
// JSON-protocol handshake over WebSockets — with the console guard watching.
test('SignalR hub negotiates and handshakes over WebSockets without console errors', async ({ page }) => {
  await page.goto('/today');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  const result = await page.evaluate(async () => {
    const negotiate = await fetch('/hubs/schedule/negotiate?negotiateVersion=1', { method: 'POST' });
    const body = await negotiate.json();
    const transports: string[] = body.availableTransports.map((t: { transport: string }) => t.transport);
    const url = `${location.origin.replace(/^http/, 'ws')}/hubs/schedule?id=${encodeURIComponent(body.connectionToken)}`;

    const handshake = await new Promise<string>((resolve, reject) => {
      const ws = new WebSocket(url);
      const timer = setTimeout(() => reject(new Error('handshake timeout')), 10_000);
      ws.onopen = () => ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + '\u001e');
      ws.onmessage = (e) => {
        clearTimeout(timer);
        const text = String(e.data);
        // Clean close: the server must accept our close frame without an error.
        ws.send(JSON.stringify({ type: 7 }) + '\u001e');
        ws.close(1000);
        resolve(text);
      };
      ws.onerror = () => reject(new Error('websocket error'));
    });
    return { status: negotiate.status, transports, handshake };
  });

  expect(result.status).toBe(200);
  expect(result.transports).toContain('WebSockets');
  expect(result.handshake).toBe('{}\u001e');
});
