# Releasing

A release is a draft GitHub Release with one zip per platform and one Claude Desktop extension
(`Altrobe-<version>.mcpb`), built by `.github/workflows/release.yml`. The version comes from the tag, so there is no version file to
edit.

## Cut a release

1. Make sure `main` is green in CI. The CI job packages and smoke tests one build per OS, so a
   green `main` means the packages start.
2. Pick the version. Use `MAJOR.MINOR.PATCH`, with a suffix such as `-beta.1` for a pre-release.
3. Tag the commit on `main` and push the tag:

   ```sh
   git fetch origin main
   git tag v0.2.0 origin/main
   git push origin v0.2.0
   ```

4. The Release workflow builds `win-x64`, `osx-arm64`, `osx-x64` and `linux-x64`, each on its own
   OS, plus the Claude Desktop extension. It starts each package, checks `/api/v1/status` reports
   the version and `/` serves the web app, and lists the MCP tools through the extension's bridge.
   Then it creates a draft release named `Altrobe 0.2.0` with the four zips, the `.mcpb` and
   generated notes.
   A version with a suffix is marked as a pre-release.
5. Open the draft under Releases. Download at least your own platform's zip, unzip it and start it
   against a real install. Edit the notes if needed.
6. Publish the draft.

To build packages without tagging, run the Release workflow by hand (Actions > Release > Run
workflow). With a version it also creates the draft release, and publishing the draft creates the
tag. Without one it builds `0.1.0-dev` packages as workflow artifacts and makes no release.

If a run fails after it created the draft, delete the draft before re-running, because
`gh release create` refuses to create a release whose tag already has one.

## Build a package locally

```sh
npm run package -- osx-arm64                    # or win-x64, osx-x64, linux-x64
npm run package -- osx-arm64 --version 0.2.0    # stamp a version
npm run package:desktop                         # the Claude Desktop extension, any OS
npm run smoke:package -- dist-packages/Altrobe-0.1.0-dev-osx-arm64.zip --desktop dist-packages/Altrobe-0.1.0-dev.mcpb
```

The zip lands in `dist-packages/`, which git ignores. Without `--version`, a `vX.Y.Z` tag on
`HEAD` gives the version, else `0.1.0-dev`. Any OS can build any package, but only a macOS build
is ad-hoc signed, and Apple silicon Macs refuse to run arm64 code that isn't signed. Build the
macOS packages on a Mac.

## What is in a package

`Altrobe-<version>-<rid>/` holds the self-contained, single-file `Altrobe` executable
(`Altrobe.exe` on Windows), `appsettings.json`, `wwwroot/` (the built web app), `LICENSE`,
`THIRD_PARTY_NOTICES.md` and `READ ME FIRST.txt` (from `packaging/`). The WoWDBDefs table
definitions are embedded in the executable. The executable is not trimmed, because TACTSharp,
DBCD and System.Text.Json use reflection that trimming breaks. It is not code-signed or notarized
either, so `READ ME FIRST.txt` explains how to get past the Windows and macOS warnings.
