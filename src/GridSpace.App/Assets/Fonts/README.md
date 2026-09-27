# Application typography

Run `python3 scripts/fetch-assets.py` from the repository root before building the application. The script downloads four unmodified Carlito faces and their SIL Open Font License notice from Google Fonts by verified content hash.

Downloaded binaries are build inputs, not source-controlled files. Browser and desktop builds include the files as application assets. The original `OFL.txt` accompanies them in the application output. No font is copied from the developer machine or from a Microsoft installation.

Carlito is the declared application fallback. Logical font-family names remain in workbook metadata; this does not imply exact glyph or metric parity with every font requested by an imported workbook.
