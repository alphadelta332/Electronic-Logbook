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
- tests and test projects;
- rendered/binary artifacts such as `.xlsm`, `.pdf`, images, ZIPs, and JARs;
- generated package metadata such as `mobile/package-lock.json`.

Rebuild or update the map from the repo root:

```powershell
graphify .
```

Use the graph for bounded architecture questions:

- what depends on `ElectronicLogbook.Portable`;
- whether workbook exchange, mobile exchange, or updater recovery is crossing boundaries;
- whether a planned change belongs in the portable core or in an adapter;
- whether a new dependency would make the active milestone harder to complete.

Do not count graph cleanup, graph regeneration, or extra architectural notes as roadmap
progress unless they directly complete the active milestone gate.
