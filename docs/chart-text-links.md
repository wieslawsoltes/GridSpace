# Live chart text and inspector synchronization

Chart titles, category/value axis titles and explicit series names can reference one formatted worksheet cell. Enter a reference such as `'Revenue'!$B$2` in the corresponding inspector reference field. References can name another sheet in the same workbook; expressions, ranges, named ranges and external workbook references are not supported.

**Customize Series** retains automatic header names as live cell links. Chart source overlays provide separate single-cell name grips for retargeting those links without changing the numeric vectors. Sources on another sheet are never interpreted as addresses on the host sheet. PivotChart sources remain report-owned.

Entering a different inline title commits a literal title and removes its link. Enter without a change, and Escape, preserve the existing link. Clearing a reference in the inspector freezes the currently displayed caption. Structural edits and sheet rename/delete follow supported cell identities; a deleted target stays `#REF!` instead of reconnecting to a newly inserted cell or recreated sheet. Copy/paste and worksheet duplication preserve the supported source/text bindings.

## Rendering, caches and file interchange

Resolved text uses the cell's supported number format. Caption-only changes refresh text while retaining the same numeric/category arrays. Unchanged cached reads allocate zero bytes in the deterministic engine tests. Input edits retain the existing conservative numeric-cache invalidation; this is not a claim of incremental element-by-element chart calculation.

The XLSX writer emits standard chart `c:strRef` elements and caption caches. Supported standard imports work without the GridSpace extension. Unsupported references retain available cached captions with warnings. Schema tests cover the supported chart families, but do not launch Microsoft Excel or prove arbitrary package-preserving interoperability.

## Inspector ownership and synchronization

Document notifications request one coalesced dispatcher update after the current input handler unwinds. The callback reads the current chart and inspector rather than capturing stale owners. Closing a panel, switching selection or disposing the workbench makes a pending update harmless.

Changes limited to chart position or size call `ChartEditorControl.SynchronizeGeometry`, preserving the visual controls and focused drafts. This fast path requires matching workbook and worksheet identities, input revision, and all non-geometry chart properties. A replaced workbook, source change, title link change or recalculation cannot be mistaken for a geometry-only update. The inspector's last accepted numeric text is updated even when a focused draft is retained, so a later rejected edit restores the correct baseline.

Type/source changes still rebuild the relevant inspector presentation and resolved series. This makes Combo series controls appear immediately and ensures **Customize Series** uses the newly resolved source. Rejected checkbox edits restore the accepted document value without recursively submitting another edit. Selecting a different or copied PivotTable rebinds its field-list owner.

The four inspector browser tests use physical keyboard and pointer input. They cover Combo controls, source-change/customize sequencing, a geometry nudge without visual rebuild or data resolution, and editing a copied PivotTable. Diagnostics expose only identities and counters, not mutation entry points. The text-link download regression avoids shadowing its chart-creation helper with a local saved-chart variable.

## Validation

```bash
dotnet test tests/GridSpace.Tests -c Release
npm run test:browser
```

CI publishes and tests the complete Uno WebAssembly application, builds the desktop hosts, and repeats browser acceptance after a successful main deployment. Consult the specific run for results; this document does not assert an unfinished run succeeded. No tagged release or NuGet publication is part of this merge.

The separately recorded intermittent zero-length `ImageData` error is not fixed by these changes. Complete Excel text formatting, arbitrary chart-element dragging, expression/name links, external workbook links and lossless arbitrary XLSX preservation remain outside this implementation.
