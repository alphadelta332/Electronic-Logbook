import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { routeFeatures, routeSelection, routeBounds, mapRequest, frequencyHeat } from "../src/ElectronicLogbook.Mobile/wwwroot/js/routeMap.mjs";

assert.deepEqual([1, 2, 3, 4, 5].map(count => frequencyHeat(count, 5)), [0, .25, .5, .75, 1]);
assert.equal(frequencyHeat(1, 1), 0);
assert.equal(frequencyHeat(10000, 10000), 1);

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
assert.equal(features[0].properties.heat, 1);
assert.equal(routeFeatures({ ...data, connections: [{ from: "A", to: "B", count: 1 }] }).features[0].properties.heat, 0);
assert.deepEqual(routeFeatures({ ...data, connections: [1, 3, 5].map(count => ({ from: "A", to: "B", count })) }).features.map(feature => feature.properties.heat), [0, .5, 1]);
assert.deepEqual(features[0].geometry.coordinates, [[178, -36], [181, -17]]);
assert.equal(features[0].properties.count, 2);
assert.equal(features[0].properties.key, "A/B");
assert.equal(features.length, 1);
const entryRoutes = {
    markers: [
        { icao: "YMMB", longitude: 145, latitude: -38, flightIds: ["trip", "return", "local"] },
        { icao: "YHSM", longitude: 142, latitude: -37, flightIds: ["trip", "other"] },
        { icao: "YBDG", longitude: 144, latitude: -37, flightIds: ["trip", "return", "other"] },
        { icao: "YSHT", longitude: 145, latitude: -36, flightIds: ["trip", "unrelated"] },
        { icao: "YMML", longitude: 145, latitude: -38, flightIds: ["unrelated"] }
    ],
    connections: [
        { from: "YMMB", to: "YHSM", count: 1, flightIds: ["trip"] },
        { from: "YHSM", to: "YBDG", count: 2, flightIds: ["trip", "other"] },
        { from: "YBDG", to: "YSHT", count: 1, flightIds: ["trip"] },
        { from: "YBDG", to: "YMMB", count: 1, flightIds: ["return"] },
        { from: "YSHT", to: "YMML", count: 1, flightIds: ["unrelated"] }
    ]
};
const selected = routeFeatures(entryRoutes, "YMMB").features;
assert.deepEqual(selected.map(feature => feature.properties.key), ["YMMB/YHSM", "YHSM/YBDG", "YBDG/YSHT", "YBDG/YMMB"]);
assert.deepEqual(selected.map(feature => feature.properties.count), [1, 2, 1, 1]);
assert.deepEqual(selected.map(feature => feature.properties.heat), [0, 1, 0, 0]);
const allRouteStyles = new Map(routeFeatures(entryRoutes).features.map(feature => [feature.properties.key, feature.properties.heat]));
for (const airport of entryRoutes.markers) {
    for (const feature of routeFeatures(routeSelection(entryRoutes, airport.icao)).features)
        assert.equal(feature.properties.heat, allRouteStyles.get(feature.properties.key));
}
const aircraftFiltered = { ...data, maximumRouteCount: 5,
    connections: [{ from: "A", to: "B", count: 1, frequencyCount: 3, flightIds: ["trip"] }],
    markers: data.markers.map(marker => ({ ...marker, flightIds: ["trip"] })) };
assert.equal(routeFeatures(aircraftFiltered).features[0].properties.heat, .5);
assert.equal(routeFeatures(routeSelection(aircraftFiltered, "A")).features[0].properties.heat, .5);
// Selecting an intermediate stop retains both ends of every matching entry.
assert.deepEqual(routeFeatures(entryRoutes, "YHSM").features.map(feature => feature.properties.count), [1, 2, 1]);
assert.deepEqual(routeFeatures(entryRoutes, "YMML").features.map(feature => feature.properties.key), ["YSHT/YMML"]);
assert.equal(routeFeatures(entryRoutes).features.length, 5);
assert.equal(routeFeatures(entryRoutes, null).features[1].properties.count, 2);
assert.deepEqual(routeFeatures({ markers: [entryRoutes.markers[0]], connections: [] }, "YMMB").features, []);
assert.deepEqual(routeSelection(entryRoutes, "YMMB").markers.map(marker => marker.icao), ["YMMB", "YHSM", "YBDG", "YSHT"]);
assert.deepEqual(routeSelection(entryRoutes, "YMML").markers.map(marker => marker.icao), ["YSHT", "YMML"]);
assert.deepEqual(routeSelection({ markers: [entryRoutes.markers[0]], connections: [] }, "YMMB").markers, [entryRoutes.markers[0]]);
assert.strictEqual(routeSelection(entryRoutes), entryRoutes);
assert.equal(entryRoutes.markers.length, 5);
assert.equal(entryRoutes.connections[1].count, 2);
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
console.log("Whole-entry airport selection, route geometry, date-line fitting, world-spanning routes and 177-country background passed.");
