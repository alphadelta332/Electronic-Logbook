import { monitorAlertMessage, monitorDeliveryLimits } from './daily-monitor.mjs';

const requireValue = condition => { if (!condition) throw new Error('Backup alert delivery unavailable.'); };
const sender = 'FlightLogX backups <backups@auth.flightlogx.app>';
const hex = value => Array.from(new Uint8Array(value), byte => byte.toString(16).padStart(2, '0')).join('');

// Internal only. The monitor must reserve an attempt before this adapter can spend it once.
export async function sendBackupAlert(storage, config, token, message, request = fetch, clock = Date.now) {
  requireValue(config?.version === 1 && Object.keys(config).sort().join(',') === 'recipient,version' &&
    typeof config.recipient === 'string' && config.recipient.length <= 254 && !/\s/.test(config.recipient) &&
    /^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?\.[a-zA-Z]{2,}$/.test(config.recipient) &&
    /^re_[a-zA-Z0-9_-]{10,}$/.test(token ?? '') && !/\s/.test(token));
  const body = JSON.stringify({ from: sender, to: [config.recipient], subject: message?.subject, text: message?.text });
  const bodySha256 = hex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(body)));
  await storage.transaction(async txn => {
    const state = await txn.get('monitor-state'), now = clock();
    const limits = monitorDeliveryLimits[state?.kind ?? 'primary'];
    requireValue(state?.active && Number.isSafeInteger(now) && now >= state.startedAt &&
      now - state.startedAt < 5 * 60 * 1000 && state.signature && !state.delivered &&
      state.day === new Date(now).toISOString().slice(0, 10) && state.month === state.day.slice(0, 7) &&
      Number.isSafeInteger(state.incidentAttempts) && state.incidentAttempts >= 1 && state.incidentAttempts <= 2 &&
      limits && Number.isSafeInteger(state.dailyAttempts) && state.dailyAttempts >= 1 && state.dailyAttempts <= limits.day &&
      Number.isSafeInteger(state.monthlyAttempts) && state.monthlyAttempts >= 1 && state.monthlyAttempts <= limits.month &&
      now >= Date.parse(state.incidentAt) && now - Date.parse(state.incidentAt) < 24 * 60 * 60 * 1000 &&
      JSON.stringify(message) === JSON.stringify(monitorAlertMessage(state)));
    const wire = state.resend;
    requireValue(!wire || wire.incident !== state.incident ||
      (wire.bodySha256 === bodySha256 && wire.attempt < state.incidentAttempts));
    // Persist the exact wire-body binding and spend before I/O, including failures/lost replies.
    await txn.put('monitor-state', { ...state, resend: { incident: state.incident, bodySha256, attempt: state.incidentAttempts } });
  });
  let response;
  try {
    response = await request('https://api.resend.com/emails', { method: 'POST', redirect: 'manual', cache: 'no-store',
      signal: AbortSignal.timeout(10000), headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json',
        'Idempotency-Key': message.idempotencyKey }, body });
    requireValue(response.status === 200 && response.body &&
      (!response.headers.has('content-length') || /^\d{1,4}$/.test(response.headers.get('content-length')) &&
        Number(response.headers.get('content-length')) <= 4096));
    const reader = response.body.getReader(), parts = []; let size = 0;
    try {
      for (;;) {
        const { done, value } = await reader.read(); if (done) break;
        requireValue((size += value.length) <= 4096); parts.push(value);
      }
    } finally { await reader.cancel(); reader.releaseLock(); }
    const bytes = new Uint8Array(size); let offset = 0;
    for (const part of parts) { bytes.set(part, offset); offset += part.length; }
    const data = JSON.parse(new TextDecoder().decode(bytes));
    requireValue(/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(data?.id ?? ''));
    return { accepted: true, id: data.id };
  } catch { throw new Error('Backup alert delivery unavailable.'); }
}
