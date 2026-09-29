# WoWDBDefs table definitions

These `.dbd` files describe the column layout of the DB2 tables Altrobe reads. They are copied
unchanged from [WoWDBDefs](https://github.com/wowdev/WoWDBDefs) at commit
`e989e99e6f5f97c57b2e138d4d28b16066b4ee9c` (2026-09-25). `manifest.json` is WoWDBDefs'
`manifest.json` trimmed to these tables; it maps each table to its DB2 FileDataID.

They are community-written format descriptions, not Blizzard content. The definitions are licensed
under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) by the WoWDBDefs
contributors. Altrobe's MIT license does not cover them.

They are embedded in `Altrobe.Core.dll` so a first run works offline. When a vendored file does not
list the build being read, Altrobe downloads the current file from WoWDBDefs into its cache folder.

To refresh them, download the same file names from WoWDBDefs' `definitions/` folder, update the
commit above, and keep the list in sync with `Tables/GameTableNames.cs`.
