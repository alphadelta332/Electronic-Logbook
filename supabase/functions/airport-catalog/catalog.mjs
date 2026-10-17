const epoch = Date.UTC(2024, 0, 25);
const cycleMs = 28 * 86400000;

export function airacCycle(date = new Date()) {
    return new Date(epoch + Math.floor((date.getTime() - epoch) / cycleMs) * cycleMs).toISOString().slice(0, 10);
}

export function normaliseName(name) {
    const text = name.trim();
    for (const suffix of ['International Airport', 'Air Field', 'Air Port', 'Air Base', 'Aerodrome', 'Airfield', 'Airstrip', 'Airport', 'Runway']) {
        if (text.toLowerCase().endsWith(` ${suffix.toLowerCase()}`)) return text.slice(0, -suffix.length).trim();
    }
    return text;
}

export function validateAirport(airport) {
    if (!airport || typeof airport.i !== 'string' || !/^[A-Z0-9-]{2,16}$/.test(airport.i) ||
        typeof airport.n !== 'string' || !airport.n.trim() || airport.n.length > 500 ||
        !Number.isFinite(airport.y) || Math.abs(airport.y) > 90 || !Number.isFinite(airport.x) || Math.abs(airport.x) > 180 ||
        [airport.a, airport.b].some(code => code != null && (typeof code !== 'string' || code.length > 16)))
        throw new Error('Invalid airport record');
    return airport;
}

export function airportFromRow(row) {
    if (!['large_airport', 'medium_airport'].includes(row.type) && !(row.type === 'small_airport' && row.scheduled_service === 'yes')) return null;
    const code = [row.icao_code, row.gps_code, row.ident].map(value => (value ?? '').trim().toUpperCase()).find(value => value.length === 4);
    if (!code) return null;
    if (!row.latitude_deg?.trim() || !row.longitude_deg?.trim()) throw new Error('Missing airport coordinates');
    return validateAirport({ i: code, n: normaliseName(row.name ?? ''), a: null, b: null, y: Number(row.latitude_deg), x: Number(row.longitude_deg) });
}

export function mergeAirports(existing, downloaded) {
    const merged = new Map(existing.map(airport => [validateAirport(airport).i, airport]));
    for (const next of downloaded) {
        validateAirport(next);
        const prior = merged.get(next.i);
        merged.set(next.i, prior ? { ...prior, y: next.y, x: next.x } : next);
    }
    return [...merged.values()].sort((a, b) => a.i.localeCompare(b.i, 'en'));
}
