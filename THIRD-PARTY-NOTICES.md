# Third-party notices

GridSpace source is MIT licensed. Its dependencies and build/test tools retain their own licenses; inspect the resolved package metadata for the complete transitive dependency set.

* Uno Platform / Uno.WinUI: Apache-2.0. https://github.com/unoplatform/uno
* SkiaSharp: MIT; underlying Skia notices also apply. https://github.com/mono/SkiaSharp
* Carlito application fonts: SIL Open Font License 1.1. The build fetches pinned Google Fonts blobs and distributes the unmodified `OFL.txt` alongside the font assets. https://github.com/google/fonts/tree/main/ofl/carlito
* Playwright: Apache-2.0. https://github.com/microsoft/playwright
* xUnit.net: Apache-2.0. https://github.com/xunit/xunit

The asset-fetching script never copies fonts from the host OS. GridSpace does not include Microsoft Excel binaries, logos, proprietary fonts or extracted application resources. References to Excel describe compatibility goals and familiar spreadsheet interactions; they do not imply affiliation or endorsement.
