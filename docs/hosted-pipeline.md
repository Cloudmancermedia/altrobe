# Hosted bundle pipeline

The hosted website serves game data converted ahead of time, as a static bundle. The server binary's
`bake` command writes one build as a bundle, the web app's static build reads it, and
`npm run publish:bundle` uploads it.

## Why it works this way

- **Blizzard's public CDN, not a game install.** The bake reads each build straight from the CDN
  through TACTSharp, so it needs no machine with the game installed and can run unattended. All
  reads go through `IGameFiles`, so the converter is the same one the local app uses.
- **glTF and PNG, converted on our side.** The browser then needs no parser for the game's own model
  formats, and the web app reads the same files from the local server or a bundle.
- **Static files only.** The dressing room needs no application server: search runs in the browser
  over the baked catalog.
- **Pinned table layouts.** The bake reads game tables with a pinned WoWDBDefs commit. A build that
  changes a layout fails the run, and the site keeps serving the previous build until someone bumps
  the pin.
- **No hotfixes.** The local app applies the player's client hotfix cache (`DBCache.bin`). The CDN has
  no such file, so the site shows each build as shipped.
- **Local-only features stay local.** The command channel (`/api/v1/session`) and the `/mcp` endpoint
  let a program on your own computer drive your own tab, so the static build leaves them out.

## Bundle layout

Under the output folder:

| Path | Holds |
| --- | --- |
| `{product}/current.json` | the build the site shows |
| `{product}/{build}/manifest.json` | counts, and what failed to convert |
| `{product}/{build}/characters.json` | races and every base look |
| `{product}/{build}/catalog.json`, `sets.json` | the item catalog and sets, for search in the browser |
| `{product}/{build}/notable.json` | notable sets per race and class |
| `{product}/{build}/set-pieces.json` | every item that is a piece of some set |
| `{product}/{build}/titles.json`, `names.json` | titles and the random-name lists |
| `{product}/{build}/items/{itemId}.json` | the item resolved for every race, sex and body kind |
| `{product}/{build}/v{N}/models/{fdid}.glb`, `.json` | converted models |
| `{product}/{build}/v{N}/textures/{fdid}.png` | textures |
| `{product}/{build}/v{N}/anims/{model}/{animId}.glb` | the offered animations for each body model |

The fields are in `web/src/api/types.ts`. `N` is the converter version (`AssetConverter.OutputVersion`),
as in the local cache, so a converter change publishes new files beside the old ones instead of
overwriting them. Below `v{N}` the paths keep the local server's `/assets/{build}/...` shape, so the
web app changes only its base URL. The JSON comes from the resolvers and catalogs, not the converter,
so it stays beside the build. `current.json` moves only after the count check passes.

The site offers a chosen list of animations (`BundleBaker.AnimationList`), because every animation
would add about 1.7 GB per build. Only Classic animations count (`AnimationNames.IsClassic`): the HD
bodies also carry retail's, which the app never offers.

## Run it from a local install

Reads local files only; no network.

```sh
dotnet build importer/src/Altrobe.Server
dotnet importer/src/Altrobe.Server/bin/Debug/net10.0/Altrobe.Server.dll bake \
  --out bundle --install "/Applications/World of Warcraft" --product wow_classic_beta \
  --limit-items 5 --limit-sets 2
```

`--limit-items` and `--limit-sets` bake a subset for a quick check (a limited set keeps its pieces).
`--cache <dir>` sets where converted files are cached between runs (default: a temp folder).
`--parallel N` sets how many files convert at once (default: one per CPU core).

Even a small bake writes several hundred MB, because customization textures and animations are a
fixed cost per body whatever the item count.

## Run the site from a bundle

The web app picks its backend at build time. Vite's `static` mode swaps the local server for
`web/src/api/static.ts`, which reads the bundle's JSON once per file and runs item and set search in
the browser (`web/src/api/search.ts`, ported from `ItemCatalog.Search` and `SetCatalog.Search` with
their test cases). The static site has no install picker, no command channel and no prompt box.

```sh
npm --prefix web run dev:static      # serves output/bundle at /bundle
npm --prefix web run build:static    # writes web/dist-static/
npm --prefix web run test:e2e:static # one browser check against the bundle
```

`ALTROBE_BUNDLE_DIR` points the dev server at another bundle folder. At build time,
`VITE_ALTROBE_BUNDLE_URL` (default `/bundle`) says where the site fetches the bundle from, and
`VITE_ALTROBE_PRODUCT` (default `wow_classic_beta`) which product folder it reads.

## The count check

Pass the last published build's manifest with `--previous <manifest.json>`. If any count (races,
looks, items, sets, resolved items, models, textures) drops by more than `--max-drop` (default
`0.05`, 5%), the run prints each drop, exits 1, and leaves `current.json` as it was.

## Publish a build

`npm run publish:bundle` uploads the build that `{product}/current.json` names to the assets bucket,
then switches the site to it. It runs the AWS CLI with whatever credentials it finds: a profile
locally, the deploy role in CI. It writes to AWS, so run it with `--dry-run` first.

```sh
npm run publish:bundle -- --product wow_classic_beta --profile <profile> --dry-run
```

`--bucket` and `--distribution` default to `BUNDLE_BUCKET` and `ASSET_DISTRIBUTION_ID`, the
`AltrobeSite` outputs ([deployment.md](deployment.md)); `--bundle` defaults to `output/bundle`. In
order, it:

1. Syncs `{product}/{build}/` to the bucket.
2. Uploads files over 10,000,000 bytes gzipped with `Content-Encoding: gzip`
   ([deployment.md](deployment.md#publishing-a-bundle) says why).
3. Uploads `{product}/current.json`, so the site moves to the build only once every file is there.
4. Invalidates `/{product}/current.json` in CloudFront.

Rolling back is uploading the previous build's `current.json` and invalidating it; that build's files are still in the bucket.

## Run it from Blizzard's CDN

This downloads several GB from Blizzard's public CDN. It asks the version service for the product's
current build and its CDN hosts, downloads that build's archive indexes and then every file the bake
needs. Nothing in the tests or the local app runs this path.

- **Indexes.** The bake fetches the archive indexes itself, 16 at a time with retries, into the cache
  folder TACTSharp reads first (`CdnIndexPrefetch`), because fetching them all at once timed out.
- **Language.** The CDN has every locale's copy of each text file. Files for English (or for every
  locale) are read first; a local install only has its own locale on disk, so it never had to choose.

A full bake is about 3 GB, with about 5 GB of download cache; a rerun with the cache takes seconds.

```sh
dotnet importer/src/Altrobe.Server/bin/Debug/net10.0/Altrobe.Server.dll bake \
  --out bundle --cdn --product wow_classic_beta --region us --cache bake-cache
```

`--versions-file <file>` uses a saved version-service answer instead of asking the service.
Encrypted files are skipped.

## Item names

Items the game files leave unnamed get Classic names from cmangos classic-db (`ClassicItemNames`);
`catalog.json` marks them `nameSource: "cmangos"` and carries the GPL-3.0 credit. See
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
