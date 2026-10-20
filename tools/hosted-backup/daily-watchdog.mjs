import { checkMissingMonitor, recordMonitorHeartbeat } from './daily-monitor.mjs';
import { sendBackupAlert } from './daily-alert-delivery.mjs';

export const watchdogObjectName = 'flightlogx-preview-monitor';

// Separate Worker/SQLite namespace: neither checks nor alerts call the primary Worker.
export class MissingMonitorGuard {
  constructor(state, env) { this.storage = state.storage; this.env = env; }
  async fetch(request) {
    if (this.env.WATCHDOG_ENABLED !== 'true') return new Response('Disabled', { status: 409 });
    try {
      if (request.method !== 'POST' || request.body) return new Response('Not found', { status: 404 });
      const route = new URL(request.url).pathname;
      if (route === '/heartbeat') {
        await recordMonitorHeartbeat(this.storage);
        return new Response(null, { status: 204 });
      }
      if (route !== '/check') return new Response('Not found', { status: 404 });
      const config = JSON.parse(this.env.ALERT_DELIVERY_JSON);
      return Response.json(await checkMissingMonitor(this.storage, message =>
        sendBackupAlert(this.storage, config, this.env.RESEND_SEND_TOKEN, message)));
    } catch { return new Response('Missing-monitor check unavailable', { status: 409 }); }
  }
}

export function watchdogStub(env) {
  return env.MISSING_MONITOR.get(env.MISSING_MONITOR.idFromName(watchdogObjectName));
}

export async function runMissingMonitor(env) {
  if (env.WATCHDOG_ENABLED !== 'true') return;
  const response = await watchdogStub(env).fetch('https://watchdog.internal/check', { method: 'POST' });
  if (!response.ok) throw new Error('Missing-monitor check unavailable.');
}
