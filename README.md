# Altrobe

A dressing room for World of Warcraft: Forever, in the browser. See the same outfit on different races, save a look, and share it as a link.

Early development.

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

Nothing is downloaded from Blizzard. The server only reads your local install.

Other commands, all from the repository root:

| Command | What it does |
| --- | --- |
| `npm run dev` | Runs the server without opening a browser, and Vite with hot reload on `http://localhost:5173/`. Ctrl+C stops both. |
| `npm test` | Checks that no game content is tracked, then runs the .NET tests, lints the web app and runs its unit tests. |
| `npm --prefix web run test:e2e` | Browser checks against your real install. Opt-in: run `npx --prefix web playwright install chromium` once, then set `ALTROBE_E2E=1`. Without it they skip. |

`ALTROBE_WOW_PATH`, `ALTROBE_PORT`, `ALTROBE_CACHE_DIR` and `ALTROBE_NO_BROWSER=1` work with
`npm start` and `npm run dev`. [docs/local-server.md](docs/local-server.md) covers the server's
options and API, and [web/README.md](web/README.md) the web app and its browser checks.

So far this has been tried only on macOS. The scripts are written for Windows and Linux too, and CI
is set up to build and test on both, but nobody has run the app end to end there yet.

`tools/` holds the Phase 1 spike. The server and web app replace it; it stays as a historical
reference.

## How it gets its data

Altrobe reads item data and 3D models from a World of Warcraft: Forever install on your own machine. This repository contains code only. It will never contain Blizzard game files, models, textures, or extracted tables.

If you run the importer, you are reading files from your own game install. Blizzard's End User License Agreement restricts this, and you should read it before you do. Blizzard has not historically acted against model viewers like this one.

## What this project will not do

- Commit or host Blizzard content in this repository
- Offer downloads of game models or textures
- Read encrypted, unreleased game files
- Use Blizzard trademarks in its name

## License

The code is released under the [MIT License](LICENSE). The license covers Altrobe's code only. It grants no rights to Blizzard's game content.

## Not affiliated with Blizzard

Altrobe is a fan project. It is not affiliated with or endorsed by Blizzard Entertainment. World of Warcraft and World of Warcraft: Forever are trademarks of Blizzard Entertainment, Inc.
