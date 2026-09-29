# Altrobe web app

The dressing room UI: a React app with a three.js viewer. The local Altrobe server serves the built app and its API (`/api/v1`) on the same origin, along with converted models and textures under `/assets/{build}/`.

```sh
npm install
npm run build   # type-check, then build into dist/
npm run lint    # oxlint
npm test        # Vitest unit tests
```

## Browser checks

`e2e/` holds Playwright checks that run the real app against a real World of Warcraft: Forever
install. They are opt-in and skip unless `ALTROBE_E2E=1` is set and an install is found
(`ALTROBE_WOW_PATH`, else `/Applications/World of Warcraft`):

```sh
npx playwright install chromium   # once; the browser goes to your user cache, not the repo
ALTROBE_E2E=1 npm run test:e2e
```

The setup (`e2e/global-setup.ts`) builds and starts the .NET server with `--no-browser`, a free
port and a temporary cache folder, selects the Forever product, and starts Vite with
`ALTROBE_API` pointing at it. The server runs with `HTTPS_PROXY` and `HTTP_PROXY` set to a dead
address, so any attempt to reach the internet fails. Each check also fails on a console error, a
failed request, or a request that leaves the machine. The checks drive the app through the
dev-only `window.__altrobe` hooks in `src/test-hooks.ts`.

## Running without the server

`npm run dev:spike` starts `vite dev` with a dev-only adapter (`dev/spike-adapter.ts`) that answers the API from the Phase 1 spike's converted files. Point it at the spike's `output/` folder:

```sh
ALTROBE_SPIKE_OUTPUT=/path/to/spike/output npm run dev:spike -- --port 5199
```

The adapter reads those files at request time and copies nothing into the repository. It covers what the spike converted: the Orc male and Undead female (HD and SD), and resolved data for the spike's handful of items. Set `ALTROBE_DEV_NO_INSTALL=1` to start with no install selected and try the install picker. A production build never includes the adapter.

With the real server, build the app and let the server serve `dist/`.

## Layout

- `src/api/` holds the API types and the one client module that fetches from the server.
- `src/viewer/` holds dressing (`dress.ts`), skin compositing, M2 materials, the character builder and the shared three.js stage.
- `src/look/look.ts` reads, writes and checks saved looks. `docs/look-format.md` describes the format.
- `src/commands.ts` is the command API that the UI calls, and that Phase 3's plain-language layer will call.
- `src/components/` holds the panels.
