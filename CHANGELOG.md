# Changelog

## 0.1.0-alpha.1

Initial local-first spreadsheet workbench for Uno Platform browser and desktop hosts.

Eight packable libraries separate the workbook model, formulas, editing transactions, interchange, sparse geometry, Skia rendering, controls and workbench. The app includes an Office-style ribbon, formula bar, direct editing, selection/fill/resize gestures, frozen panes, formatting, sorting/filtering, charts, notes, worksheet operations and local recovery.

Native, delimited-text and bounded XLSX-subset interchange are implemented. Content-verified Carlito assets provide deterministic application typography. CI runs engine/raster and physical-input browser tests, packs libraries, builds native hosts and deploys verified browser output to GitHub Pages. Release automation produces archives, packages and checksums.

This alpha does not have full Excel feature, file, keyboard, accessibility or pixel-level parity. See [compatibility](docs/compatibility.md) for the current boundary.
