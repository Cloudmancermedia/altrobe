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

`npm run dev:spike` starts `vite dev` with a dev-only adapter (`dev/spike-adapter.ts`) that answers the API from the `tools/` prototype's converted files. Point it at the prototype's `output/` folder:

```sh
ALTROBE_SPIKE_OUTPUT=/path/to/spike/output npm run dev:spike -- --port 5199
```

The adapter reads those files at request time and copies nothing into the repository. Set `ALTROBE_DEV_NO_INSTALL=1` to start with no install selected and try the install picker. A production build never includes the adapter.

With the real server, build the app and let the server serve `dist/`.

`npm run dev:static` runs the hosted site instead, reading a baked bundle from `../output/bundle`
(or `ALTROBE_BUNDLE_DIR`) through `dev/bundle-server.ts`. `npm run build:static` builds it into
`dist-static/`, and `npm run test:e2e:static` runs one browser check against the bundle with no
install or server. `docs/hosted-pipeline.md` has the details.

## Layout

- Every request goes through `src/api/client.ts`, which answers from the local server or, in a static build, from a baked bundle.
- `src/commands.ts` is the command API that both the UI and the MCP tools drive.
