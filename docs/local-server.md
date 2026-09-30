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

| Endpoint | Returns |
| --- | --- |
| `GET /status` | `version`, `installs[]` (each with `products[]`: `product`, `build`, `buildName`, `isForever`), `active`, `cacheFolder` |
| `POST /install` `{"path"?, "product"}` | selects a product; `path` defaults to the first install found. Returns the status. `409` with code `install_updating` when `.build.info` names a build or CDN config that is not in `Data/config` yet, which is how an install looks while Battle.net updates it |
| `GET /characters` | `build`, `races[]`: `race`, `name`, `classes[]`, `sexes[]` with `hd` and `sd` |
| `GET /characters/{race}/{sex}?models=hd\|sd&classId=` | the default look: body model, texture layout and sections, geosets, `meshGeosets` (every geoset in the body mesh), texture layers, `choices` (defaults) and `options` (every selectable choice with its geosets and layers) |
| `GET /items/search?q=&slot=&quality=&limit=&offset=` | array of `itemId`, `name`, `slot` (look slot name), `inventoryType`, `quality`, `iconFileDataId`, `internal` (a developer, placeholder or NPC item by its name; these sort last, and the MCP tools leave them out); total in `X-Total-Count` |
| `GET /sets/search?q=&limit=&offset=` | item sets matching a set name, a piece's name or a set ID: `setId`, `name`, `internal`, `pieces[]` (`slot`, `itemId`, `name`, `quality`, `iconFileDataId`) and `skipped[]` (`itemId`, `reason`); total in `X-Total-Count` |
| `GET /items/{itemId}/resolved?race=&sex=&models=` | models, textures, body texture sections, geosets, helm hides and attachment points for one race and sex. `404` when the item has no visual |

Assets are converted on first request and cached with `Cache-Control: immutable`:

- `GET /assets/{build}/models/{fdid}.glb` and `.json` (skinned glTF plus metadata)
- `GET /assets/{build}/textures/{fdid}.png` (any BLP texture, item icons included)

`{build}` must be the selected build, for example `1.60.1.70009`. The web app's own files live
under `/static/` so they never clash with `/assets/`.

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

The tools are the web app's command API: `search_items`, `search_sets`, `equip_set`, `build_outfit`, `wear_main_outfit`, `list_characters`, `get_look`,
`equip_item`, `unequip`, `set_character`, `set_customization`, `randomize_customization`,
`reset_customization`, `compare`, `set_visibility`, `set_view` and `share_link`.

- `search_items`, `search_sets` and `list_characters` run on the server and work with no tab open.
  `search_items` also filters by required level (`min_level`, `max_level`), armor type (`armor`:
  cloth, leather, mail or plate, from `Item.SubclassID`) and class (`class_id`, from
  `ItemSparse.AllowableClass`), and its results carry `requiredLevel`, `itemLevel` and `armor`.
  Class does not cover armor proficiency, so a model picks the armor type itself.
- Items carry a weapon type (`weapon`: sword, axe, mace, dagger and so on, from `Item.SubclassID`) and
  `hands` (one-hand, two-hand, main hand, off hand, shield, held in off hand, ranged, thrown, from the
  inventory type), and `search_items` filters on both. A two-hander in the main hand clears the off
  hand, and an off-hand item is refused while one is held.
- Look changes and `get_look` answer after the tab has drawn the change (up to 15 s), so their notices
  belong to the current look.
- `build_outfit` fills each armor slot and the back for a level, and the main hand when given weapon
  types (the data does not say which weapons a class can use), with `hands` and an optional shield or
  off-hand item: the item with the highest required
  level within 7 levels below it, then the highest item level. The same request always gives the same
  outfit. `compare` takes each extra character's own `items` and `label`, so a leveling journey is one
  `build_outfit` call per level and one `compare` call.
  The two searches list only real items and sets; developer, test and NPC items are left out, though
  an exact item ID still finds one. The search panel in the web app lists them, last.
- Some items have a model but no name, quality or level in the build (in the Forever beta, all of
  tier 1 and tier 2). They are listed as `unnamed`, named after their set and slot ("Battlegear of
  Wrath: chest") or, outside a set, "Unnamed chest (item 10006)". Set names are real.
- The other tools go to the open tab over `GET /api/v1/session`, a WebSocket. The tab runs them with
  the same commands its buttons use, so the 3D view updates live. With no tab open they fail with
  a message saying to open Altrobe. If several tabs are open, the newest one gets the commands.
- `equip_item` checks the item against the catalog first, so a model cannot equip an item ID it
  made up, or put an item in a slot it does not fit.

Claude Desktop only starts local MCP servers over stdio, so it cannot use this endpoint directly
yet. A one-click Claude Desktop bundle is the next step.

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
