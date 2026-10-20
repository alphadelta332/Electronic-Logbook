import { WorkerEntrypoint } from 'cloudflare:workers';
import { runMissingMonitor, watchdogStub } from './daily-watchdog.mjs';
export { MissingMonitorGuard } from './daily-watchdog.mjs';

// Named RPC entrypoint only; ordinary HTTP cannot forge a healthy heartbeat.
export class MonitorHeartbeat extends WorkerEntrypoint {
  async completed() {
    if (this.env.WATCHDOG_ENABLED !== 'true') throw new Error('Missing-monitor observer disabled.');
    const response = await watchdogStub(this.env).fetch('https://watchdog.internal/heartbeat', { method: 'POST' });
    if (response.status !== 204) throw new Error('Monitor heartbeat not recorded.');
  }
}

export default {
  fetch() { return new Response('Not found', { status: 404 }); },
  async scheduled(controller, env) { await runMissingMonitor(env); }
};
