# Development and release

## Prerequisites

Install the SDK specified by `global.json`, Python 3, and Node.js 22 or later for browser tests. Install the .NET `wasm-tools` workload for browser builds. Linux desktop hosts additionally need the platform's X11/graphics runtime dependencies expected by Uno.

Run `python3 scripts/fetch-assets.py` before building the application. It fetches Google Fonts blobs by immutable content ID, verifies their Git blob hashes, and includes the OFL notice. It does not download or redistribute host-system fonts. Engine-only tests and libraries can run without application assets.

## Useful commands

```bash
# Engines and headless Skia rendering
dotnet test tests/GridSpace.Tests -c Release

# Native desktop host
dotnet run --project src/GridSpace.App -f net10.0-desktop -p:GridSpaceDesktopOnly=true

# Browser app
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/GridSpace.App -f net10.0-browserwasm -c Release -o artifacts/publish -p:WasmShellWebAppBasePath=/GridSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173

# Browser acceptance (second terminal)
npm ci
npx playwright install chromium
npm run test:browser

# Pack a reusable library
dotnet pack src/GridSpace.Controls -c Release -o artifacts/packages
```

Use `GRIDSPACE_URL` to run the same browser tests against another deployment. The tests append `?test=1`; this makes diagnostics read-only and disables local recovery access for that test run. Browser tests depend on the workbench's actual geometry and use physical keyboard and pointer events.

## Repository layout

`src/` contains the eight reusable libraries and the application host. `tests/GridSpace.Tests/` contains engine, interchange, transaction, geometry and raster tests. `tests/browser/` contains Playwright acceptance tests. `scripts/` handles pinned assets, static output collection and local serving. Generated packages, browser output, downloaded font assets and test artifacts are not source-controlled.

Keep the workbook engines free of Uno references. Keep platform file APIs in the application host. Put edits behind `SpreadsheetSession` so rollback, notifications and history remain coherent. Add a regression test when changing reference translation, persistence or hit-testing. Avoid retaining worksheet objects across an undo or load, because these operations replace the model.

## Workflows

**Build** runs engine tests, publishes the actual Uno WASM app, runs browser acceptance, packs all libraries, and deploys the verified artifact on successful main-branch runs. Pages uses the `/GridSpace/` base path. Missing WASM output is a hard failure; no placeholder site is substituted. The public site is checked against the expected commit and tested after deployment.

**Desktop** builds the shared native host on Linux, Windows and macOS. Build success is not equivalent to manual validation of all native file dialogs and input methods on physical machines.

**Release** is triggered by a `v*` tag or explicit workflow dispatch. It validates the version, reruns tests and browser acceptance, publishes self-contained single-file desktop executables (`GridSpace-<version>-<rid>.zip`/`.tar.gz` for win/linux/osx x64 and arm64), packages source/browser output and NuGet libraries, and creates SHA-256 checksums. Only tags create a GitHub release (a prerelease suffix produces a GitHub prerelease) and publish the packages to nuget.org using [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the `nuget` job runs in the protected `nuget` environment and exchanges a GitHub OIDC token for a short-lived API key via `NuGet/login` (set the `NUGET_USER` variable to the nuget.org profile name). No API key is stored. Manual dispatches are dry runs that upload every asset as workflow artifacts.

Release artifacts are not signed platform installers. Signing/notarization credentials and a full native installer pipeline are separate deployment concerns.

## Recovery and troubleshooting

Browser recovery is stored in IndexedDB under `GridSpace`, object store `workbooks`, key `recovery`. Desktop recovery is located below the OS local application data directory in `GridSpace/recovery.gridspace`. Download a native copy before clearing site data or replacing the recovery slot.

When reporting a rendering or input issue, include the application commit from `build-info.json`, browser/OS, viewport size, zoom, the selected address and a minimal non-sensitive workbook. The generated browser-validation artifact contains screenshots, traces and test results. Do not attach confidential workbooks to public issues.
