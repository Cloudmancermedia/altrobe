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
  rebinding, and any request with a cross-site `Origin`. It sends no CORS headers.
- `POST /api/v1/install` needs an `application/json` body, so a plain cross-site form cannot call it.

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
| `GET /items/search?q=&slot=&quality=&limit=&offset=` | array of `itemId`, `name`, `slot` (look slot name), `inventoryType`, `quality`, `iconFileDataId`; total in `X-Total-Count` |
| `GET /items/{itemId}/resolved?race=&sex=&models=` | models, textures, body texture sections, geosets, helm hides and attachment points for one race and sex. `404` when the item has no visual |

Assets are converted on first request and cached with `Cache-Control: immutable`:

- `GET /assets/{build}/models/{fdid}.glb` and `.json` (skinned glTF plus metadata)
- `GET /assets/{build}/textures/{fdid}.png` (any BLP texture, item icons included)

`{build}` must be the selected build, for example `1.60.1.70009`. The web app's own files live
under `/static/` so they never clash with `/assets/`.

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
