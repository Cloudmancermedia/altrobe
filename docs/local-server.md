# Local server

`importer/src/Altrobe.Server` is a small web server that runs on your own machine. It reads your
World of Warcraft install, converts models and textures on demand, and serves the web app. It never
downloads game data from Blizzard; a missing file is an error, not a download.

## Run it

Needs the .NET 10 SDK. From the repository root, `npm start` builds the web app and then runs
the server (see the README's quick start). To run only the server:

```sh
cd importer
dotnet run --project src/Altrobe.Server
```

It listens on `http://127.0.0.1:5161/` and opens that page in your browser.

| Flag | Environment variable | Default |
| --- | --- | --- |
| `--port <n>` | `ALTROBE_PORT` | `5161` |
| `--web-root <dir>` | `ALTROBE_WEB_ROOT` | `wwwroot` next to the binary, else the repo's `web/dist` |
| `--no-browser` | `ALTROBE_NO_BROWSER=1` | opens the browser |
| | `ALTROBE_WOW_PATH` | also looks in `/Applications/World of Warcraft`, `C:\Program Files (x86)\World of Warcraft` and `C:\Program Files\World of Warcraft` |
| | `ALTROBE_CACHE_DIR` | `~/.altrobe` (Windows: `%LOCALAPPDATA%\Altrobe`) |
| | `ALTROBE_CONFIG_DIR` | settings, such as the prompt box's API key: `~/Library/Application Support/Altrobe` on macOS, `%APPDATA%\Altrobe` on Windows, `$XDG_CONFIG_HOME/altrobe` (else `~/.config/altrobe`) on Linux |
| | `ALTROBE_LLM_KEY` | the prompt box's API key, in place of the stored one |

Converted files go to `<cache>/<product>/<build>/models` and `textures`. Deleting the folder is safe;
files are converted again on the next request.

## Security

- The server binds to 127.0.0.1 only.
- It refuses any request whose `Host` header is not `localhost` or `127.0.0.1`, which stops DNS
  rebinding, and any request whose `Origin` is not this server. It sends no CORS headers.
- `POST /api/v1/install` needs an `application/json` body, so a plain cross-site form cannot call it.
- An `Origin` header must be `localhost` or `127.0.0.1` on the server's own port, so a page served
  elsewhere, including another local port, cannot call the API, `/mcp` or the tab's WebSocket. Any
  program on the same computer that sends no `Origin` can, as with the rest of the API.

## API

Everything is under `/api/v1`. Errors use `{"error": {"code": "...", "message": "..."}}` with a
matching HTTP status. Until an install is selected, every data endpoint answers `409` with code
`no_install`. The server keeps no look state: only the selected install and the per-build cache.

| Endpoint | Does |
| --- | --- |
| `GET /status` | the server version, the installs found, the selected product and the cache folder |
| `POST /install` `{"path"?, "product"}` | selects a product. `409` with code `install_updating` while Battle.net is updating the install: `.build.info` then names a build or CDN config that is not in `Data/config` yet |
| `GET /characters` | races, with their classes and sexes |
| `GET /characters/{race}/{sex}?models=hd\|sd&classId=` | the default look and every selectable customization choice |
| `GET /items/search?q=&slot=&quality=&limit=&offset=&unnamed=` | item search. Items with no name are left out unless `unnamed=1`, though an exact item ID still finds one. Total in `X-Total-Count` |
| `GET /sets/search?q=&limit=&offset=` | item set search by set name, piece name or set ID. Total in `X-Total-Count` |
| `GET /sets/notable?race=&class=` | the race's notable PvP and tier sets, for one class or every class it can be |
| `GET /sets/pieces` | every set piece's item ID, so equipping a set can take off the one worn before |
| `GET /titles`, `GET /names` | the game's titles and random-name lists |
| `GET /items/{itemId}/resolved?race=&sex=&models=` | what one item draws on one race and sex. `404` when the item has no visual |
| `GET`, `PUT /assistant/settings` | the prompt box's provider, model and key, saved to `settings.json` in the config folder, readable only by its owner. The key itself is never returned |
| `POST /assistant/test` | one short request with the saved settings |
| `POST /assistant/prompt` `{"text", "history"?}` | runs one prompt and streams its steps as `application/x-ndjson`. With `claude-code`, the server runs the player's own `claude -p` with its built-in tools off (`--tools ""`), only this server's MCP tools allowed, the player's settings and other MCP servers skipped, and no saved session |

The response shapes are in `web/src/api/types.ts`.

Assets are converted on first request and cached with `Cache-Control: immutable`:

- `GET /assets/{build}/models/{fdid}.glb` and `.json` (skinned glTF plus metadata). The metadata's
  `particles` lists the model's particle emitters (glows on shoulders and weapons): bone, offset,
  texture, blend mode, generator, animated tracks and lifetime curves. The web app simulates and
  draws them; spin, tails, wind and model particles are not drawn.
- `GET /assets/{build}/textures/{fdid}.png` (any BLP texture, item icons included)
- `GET /assets/{build}/anims/{model}/{animId}.glb`: one animation for a body model's bones, with no
  meshes; the viewer plays it on the body it already loaded. `404` when the model has no such animation.

`{build}` must be the selected build, for example `1.60.1.70009`. The web app's own files live
under `/static/` so they never clash with `/assets/`.

Converted files are cached under `<cache folder>/<product>/<build>/v<N>/`, where `N` is the
converter version (`AssetConverter.OutputVersion`). The web app adds `?v=N` to asset URLs; the
server ignores it, but it gives the browser a new URL whenever `N` changes. Bump `N` in any change
that makes the converter write different files for the same input. On the next start, Altrobe then
deletes the old version's folder and converts again on first view. The build's game index
(`tact/`) is kept.

## MCP: dress characters from Claude

The server also speaks MCP (Model Context Protocol) at `http://127.0.0.1:5161/mcp`, over
streamable HTTP. An MCP client such as Claude Code can then search items and change the look in
the open Altrobe tab. The user's own Claude plan runs the model; Altrobe only receives commands.

Connect Claude Code from this repository: `.mcp.json` names the server, and Claude Code asks
before it uses it. From anywhere else:

```sh
claude mcp add --transport http altrobe http://127.0.0.1:5161/mcp
```

After updating Altrobe, reconnect the client (`/mcp` in Claude Code): clients keep the tool list
they received when they connected, so new tools and options stay hidden until then.

Then open Altrobe in the browser and ask Claude something like "put the Orc in Thunderfury and
show the same outfit on an Undead female".

The tools mirror the web app's command API (`McpTools.cs`); a client lists them when it connects.

- `search_items`, `search_sets` and `list_characters` run on the server and work with no tab open.
  The other tools go to the newest open tab over `GET /api/v1/session`, a WebSocket, and run with
  the same commands its buttons use. With no tab open they fail with a message saying to open
  Altrobe.
- Look changes and `get_look` answer after the tab has drawn the change (up to 15 s), so their notices
  belong to the current look.
- `equip_item` checks the item against the catalog first, so a model cannot equip an item ID it
  made up, or put an item in a slot it does not fit.

### Claude Desktop

Claude Desktop starts local MCP servers over stdio, so it reaches this endpoint through a small
bridge packaged as a Claude Desktop extension, `Altrobe-<version>.mcpb`, attached to each release.
Double-click it, or drag it onto Settings > Extensions in Claude Desktop, and click Install. Claude
Desktop runs it with its own Node.js, so nothing else needs installing. The extension's one
setting is the port (default 5161).

The bridge (`desktop-extension/bridge.mjs`) relays messages to `http://127.0.0.1:<port>/mcp`
unchanged, so the tools and their descriptions always match the running Altrobe. When Altrobe isn't
running, each request fails with "Altrobe is not running on port 5161. Start Altrobe, then try
again." Claude Desktop keeps the tool list from when the extension started, so after updating
Altrobe, restart Claude Desktop.

The extension is not signed, so Claude Desktop warns about that when installing it. If installing
does nothing, add the bridge to `claude_desktop_config.json` by hand instead. This needs Node.js
installed; unzip the `.mcpb` (it is a zip) and point at its `server/index.js`:

```json
{
  "mcpServers": {
    "altrobe": {
      "command": "node",
      "args": ["/path/to/unzipped/server/index.js"],
      "env": { "ALTROBE_URL": "http://127.0.0.1:5161/mcp" }
    }
  }
}
```

Build it with `npm run package:desktop`; the bridge's tests run with `npm test`.

### Codex and the ChatGPT desktop app

The ChatGPT desktop app's Codex mode, the Codex CLI and the Codex IDE extension share one list of
MCP servers in `~/.codex/config.toml`, and all of them run on your computer, so they can reach
Altrobe directly ([OpenAI: MCP in Codex](https://learn.chatgpt.com/docs/extend/mcp)). In the app,
use Settings > MCP servers > Add server, or add this to the file and restart:

```toml
[mcp_servers.altrobe]
url = "http://127.0.0.1:5161/mcp"
```

Codex speaks streamable HTTP itself, so this needs no bridge. OpenAI documents the `url` option
with an `https` example; the plain `http://127.0.0.1` address works with the Codex CLI. To try it without editing the file, pass it for one run:
`codex exec -c 'mcp_servers.altrobe.url="http://127.0.0.1:5161/mcp"' "find Thunderfury"`. If a
Codex version refuses it, run the Claude Desktop bridge instead (needs Node.js; unzip the `.mcpb`
for `server/index.js`):

```toml
[mcp_servers.altrobe]
command = "node"
args = ["/path/to/unzipped/server/index.js"]
env = { ALTROBE_URL = "http://127.0.0.1:5161/mcp" }
```

ChatGPT on the web and on phones connects to MCP servers from OpenAI's servers, which can't reach
`127.0.0.1` on your computer. Don't expose Altrobe through a public tunnel to get around that: the
endpoint has no sign-in, so anyone with the address could drive your Altrobe tab.

## Tests

```sh
cd importer
dotnet test
```

The tests use made-up data built in code. Two integration tests read a real install and are
skipped unless you opt in:

```sh
ALTROBE_INTEGRATION=1 dotnet test
```

They check that opening the build, loading tables, building looks, converting assets and opening
files that are not on disk make no network connection at all.
