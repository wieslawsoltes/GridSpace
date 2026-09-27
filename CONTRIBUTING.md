# Contributing

Use the pinned .NET SDK and run the engine and browser acceptance suites before proposing a change. See [development](docs/development.md) for commands and [architecture](docs/architecture.md) for ownership and dependency boundaries.

Keep workbook and calculation code independent of Uno. Route document mutations through `SpreadsheetSession`, preserve undo/redo semantics, and add focused regression tests for formula, persistence, geometry and input changes. Reference translation must preserve string literals, quoted sheet names and mixed/absolute references.

Report exact reproduction steps, application commit, platform/browser, selection, zoom and a minimal non-sensitive workbook for bugs. Include a screenshot for layout issues. Do not attach proprietary fonts or confidential workbooks to issues.

Changes affecting Excel compatibility must update the compatibility ledger. A disabled feature, smoke test, stub or partial file reader must not be represented as full parity. Follow the existing MIT licensing policy and retain third-party notices for newly introduced assets and dependencies.
