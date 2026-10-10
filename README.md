# Altrobe

A browser-based dressing room for World of Warcraft: Forever. See the same outfit on different races, save a look, and share it as a link.

Early development.

## Download and run

You need World of Warcraft: Forever installed on the same computer. You don't need .NET, Node.js
or anything else.

1. Open [Releases](https://github.com/Cloudmancermedia/altrobe/releases) and download the zip for
   your computer:
   - Windows: `Altrobe-<version>-win-x64.zip`
   - Mac with Apple silicon (M1 and later): `Altrobe-<version>-osx-arm64.zip`
   - Mac with an Intel processor: `Altrobe-<version>-osx-x64.zip`
   - Linux: `Altrobe-<version>-linux-x64.zip`
2. Unzip it.
3. Start Altrobe. On Windows, double-click `Altrobe.exe`. On macOS, double-click `Altrobe`, which
   opens in a Terminal window. On Linux, run `./Altrobe` from a terminal in that folder.
4. Altrobe isn't signed, so the first start shows a warning:
   - Windows SmartScreen: click **More info**, then **Run anyway**.
   - macOS: click **Done**, then open System Settings > Privacy & Security and click
     **Open Anyway** next to the Altrobe message. On macOS 14 and earlier, you can instead
     right-click Altrobe and choose **Open**. From Terminal,
     `xattr -dr com.apple.quarantine <the unzipped folder>` does the same.
5. Your browser opens Altrobe at `http://127.0.0.1:5161/`. To stop it, close the window it runs in.

`READ ME FIRST.txt` in the zip covers the same steps, plus what to do if Altrobe can't find your
game. Altrobe reads your game files on your own computer. It never contacts Blizzard.

## Developer quick start

You need:

- the [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) 20 or newer
- World of Warcraft: Forever installed on the same machine

```sh
git clone https://github.com/Cloudmancermedia/altrobe.git && cd altrobe && npm start
```

`npm start` installs the web app's dependencies if they are missing, builds the web app, then
compiles and runs the local server at `http://127.0.0.1:5161/` and opens it in your browser. On
the first run:

1. The server looks for your game in `ALTROBE_WOW_PATH`, then `/Applications/World of Warcraft`,
   `C:\Program Files (x86)\World of Warcraft` and `C:\Program Files\World of Warcraft`.
2. If the install has exactly one Forever product, the page selects it for you. Otherwise it lists
   the products it found, Forever first and labelled, and asks which to use.
3. Each character and item is converted from your install the first time you view it, so the
   first look at a character takes a few seconds. Converted files are cached in `~/.altrobe`
   (Windows: `%LOCALAPPDATA%\Altrobe`); deleting that folder is safe.

Nothing is downloaded from Blizzard. The server reads your local install, plus a one-time download of Classic
item names the game files leave out ([cmangos classic-db](https://github.com/cmangos/classic-db), about 13 MB).

Other commands, all from the repository root:

| Command | What it does |
| --- | --- |
| `npm run dev` | Runs the server without opening a browser, and Vite with hot reload on `http://localhost:5173/`. Ctrl+C stops both. |
| `npm test` | Checks that no game content is tracked, then runs the .NET tests, lints the web app and runs its unit tests. |
| `npm run package -- <rid>` | Builds a downloadable zip for `win-x64`, `osx-arm64`, `osx-x64` or `linux-x64` in `dist-packages/`. See [docs/releasing.md](docs/releasing.md). |
| `npm --prefix web run test:e2e` | Browser checks against your real install. Opt-in; see [web/README.md](web/README.md#browser-checks). |

To dress characters in plain language, type into the Ask panel. It runs on your own API key
(OpenAI, Anthropic, OpenRouter), a free local model (Ollama, LM Studio), or the Claude Code you
have installed and signed in to your Claude plan, set in the panel's settings; a key stays on your
computer. If a provider or model doesn't work for you, please open an issue. The
Claude Code option runs `claude -p` for each prompt, so it counts against your plan's usage limits.

You can also ask Claude or ChatGPT directly: install the Claude Desktop extension
(`Altrobe-<version>.mcpb` from a release), add Altrobe to Codex or the ChatGPT desktop app, or connect
Claude Code to the server's MCP endpoint (this repository's `.mcp.json` does it). See [MCP in docs/local-server.md](docs/local-server.md#mcp-dress-characters-from-claude).

`ALTROBE_WOW_PATH`, `ALTROBE_PORT`, `ALTROBE_CACHE_DIR` and `ALTROBE_NO_BROWSER=1` work with
`npm start` and `npm run dev`. [docs/local-server.md](docs/local-server.md) covers the server's
options and API, and [web/README.md](web/README.md) the web app and its browser checks.

So far this has been tried only on macOS. The scripts are written for Windows and Linux too, and CI
is set up to build and test on both, but nobody has run the app end to end there yet.

`tools/` holds the first prototype. The server and web app replace it.

## How it gets its data

Altrobe reads item data and 3D models from a World of Warcraft: Forever install on your own machine. This repository contains code only. It will never contain Blizzard game files, models, textures, or extracted tables.

The hosted website reads game builds from Blizzard's public CDN, converts them ahead of time and
serves the result as static files. [docs/hosted-pipeline.md](docs/hosted-pipeline.md) explains how a
build is baked and published.

## What this project will not do

- Commit or host Blizzard content in this repository
- Offer downloads of game models or textures
- Read encrypted, unreleased game files
- Use Blizzard trademarks in its name

## License

The code is released under the [MIT License](LICENSE). The license covers Altrobe's code only. It grants no rights to Blizzard's game content.

## Not affiliated with Blizzard

Altrobe is a fan project. It is not affiliated with or endorsed by Blizzard Entertainment. World of Warcraft and World of Warcraft: Forever are trademarks of Blizzard Entertainment, Inc.
