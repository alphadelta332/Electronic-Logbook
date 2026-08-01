# Architecture Graph Workflow

Use `graphify-out/graph.html` as an architecture map only after the root
`.graphifyignore` has been applied. Without that filter, Graphify can include tests,
generated mobile runtime assets, build output, rendered documents, and binaries. That
makes the graph visually dense and can make the app look more coupled than the
hand-authored product source actually is.

The root `.graphifyignore` keeps the default map focused on product architecture by
excluding:

- generated Blazor/.NET runtime and Android sync output;
- `bin`, `obj`, `dist`, `node_modules`, `artifacts`, and `TestResults`;
- tests, test projects, and solution manifests that reintroduce test-project references;
- CI metadata and build, release, validation, and device-install scripts;
- general documentation and local launch profiles;
- rendered/binary artifacts such as `.xlsm`, `.pdf`, images, ZIPs, and JARs;
- generated package metadata such as `mobile/package-lock.json`.

Graphify deliberately scans its default `graphify-out/memory` query-history directory
even when that directory is ignored. Do not save query results there for this repository.
If a query result is worth retaining locally, keep it outside the architecture corpus:

```powershell
graphify save-result --memory-dir .graphify-memory `
  --question "<question>" --answer "<answer>" --type query
```

The ignored `.graphify-memory` directory is local working memory, not product
architecture or a tracked project artifact.

Rebuild the code-only map from the repo root:

```powershell
$env:GRAPHIFY_FORCE = "1"
try {
    graphify extract . --out . --code-only
    graphify cluster-only . --no-label
}
finally {
    Remove-Item Env:GRAPHIFY_FORCE -ErrorAction SilentlyContinue
}
```

Before rebuilding after a `.graphifyignore` change, move the active `graph.json`,
`manifest.json`, report, HTML, analysis, and label files into a timestamped backup under
the ignored `graphify-out` directory. Starting without the old graph and manifest avoids
retaining nodes that were admitted by an older corpus definition.

Graphify does not currently recognize exported VBA `.bas` modules. The default graph is
therefore authoritative for the .NET/mobile implementation and supporting project
structure, but not for the complete workbook/VBA architecture. Use the exported VBA
sources directly when a question involves `modBoot`, `modLogbook`, or `modUpdate`.

Use the graph for bounded architecture questions:

- what depends on `ElectronicLogbook.Portable`;
- whether workbook exchange, mobile exchange, or updater recovery is crossing boundaries;
- whether a planned change belongs in the portable core or in an adapter;
- whether a new dependency would make the active milestone harder to complete.

Do not count graph cleanup, graph regeneration, or extra architectural notes as roadmap
progress unless they directly complete the active milestone gate.
