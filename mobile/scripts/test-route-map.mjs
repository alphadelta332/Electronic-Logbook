import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { routeFeatures, routeBounds, mapRequest } from "../src/ElectronicLogbook.Mobile/wwwroot/js/routeMap.mjs";

const app = "https://localhost/routes";
for (const url of ["/data/natural-earth-countries.json", "https://tiles.openfreemap.org/styles/dark",
    "https://tiles.openfreemap.org/planet", "https://tiles.openfreemap.org/planet/20261018/3/4/5.pbf",
    "https://tiles.openfreemap.org/fonts/Noto%20Sans/0-255.pbf", "https://tiles.openfreemap.org/sprites/ofm/ofm.png"]) {
    assert.deepEqual(mapRequest(url, app), { url: new URL(url, app).href, credentials: "omit", referrerPolicy: "no-referrer" });
}
for (const url of ["https://example.com/tiles", "http://tiles.openfreemap.org/planet", "https://tiles.openfreemap.org.evil.test/planet",
    "https://user:secret@tiles.openfreemap.org/planet", "https://tiles.openfreemap.org/planet?token=secret",
    "https://tiles.openfreemap.org/planet#flight", "https://tiles.openfreemap.org/account"]) assert.throws(() => mapRequest(url, app));

const data = { markers: [
    { icao: "A", longitude: 178, latitude: -36, count: 2 },
    { icao: "B", longitude: -179, latitude: -17, count: 2 }
], connections: [{ from: "A", to: "B", count: 2 }] };
assert.deepEqual(routeBounds(data), [[178, -36], [181, -17]]);
const features = routeFeatures(data).features;
assert.deepEqual(features[0].geometry.coordinates, [[178, -36], [181, -17]]);
assert.equal(features[0].properties.count, 2);
assert.equal(features[0].properties.key, "A/B");
assert.equal(features.length, 1);
const reverse = { ...data, connections: [{ from: "B", to: "A", count: 1 }] };
assert.equal(Math.abs(routeFeatures(reverse).features[0].geometry.coordinates[1][0] + 182), 0);
assert.deepEqual(routeBounds(reverse), [[178, -36], [181, -17]]);
assert.deepEqual(routeBounds({ markers: [data.markers[0]], connections: [] }), [[178, -36], [178, -36]]);
assert.equal(routeBounds({ markers: [], connections: [] }), null);
const world = { markers: [0, 120, -120].map((value, index) => ({ icao: `${index}`, longitude: value, latitude: 0 })),
    connections: [{ from: "0", to: "1" }, { from: "1", to: "2" }, { from: "2", to: "0" }] };
const worldBounds = routeBounds(world);
assert.equal(worldBounds[1][0] - worldBounds[0][0], 360);
const background = JSON.parse(await readFile(new URL("../src/ElectronicLogbook.Mobile/wwwroot/data/natural-earth-countries.json", import.meta.url), "utf8"));
assert.equal(background.features.length, 177);
for (const feature of background.features) {
    assert.deepEqual(feature.properties, {});
    const polygons = feature.geometry.type === "Polygon" ? [feature.geometry.coordinates] : feature.geometry.coordinates;
    for (const polygon of polygons) for (const ring of polygon) {
        assert(ring.length >= 4);
        assert.deepEqual(ring[0], ring.at(-1));
        for (const [lng, lat] of ring) assert(Number.isFinite(lng) && Math.abs(lng) <= 180 && Number.isFinite(lat) && Math.abs(lat) <= 90);
    }
}
console.log("Route geometry, date-line fitting, world-spanning routes and 177-country background passed.");
