# Altrobe web app

The dressing room UI: a React app with a three.js viewer. The local Altrobe server serves the built app and its API (`/api/v1`) on the same origin, along with converted models and textures under `/assets/{build}/`.

```sh
npm install
npm run build   # type-check, then build into dist/
npm run lint    # oxlint
npm test        # Vitest unit tests
```

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
