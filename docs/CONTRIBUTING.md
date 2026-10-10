# Contributing

Thanks for helping. Bug reports, fixes and new features are all welcome.

## Before you start

- For anything larger than a small fix, open an issue first so we can agree on the approach.
- The [README](../README.md#developer-quick-start) covers setup. You need the .NET 10 SDK, Node.js 20
  or newer, and World of Warcraft: Forever installed.

## Making a change

1. Branch from `main`.
2. Add a test that fails without your change, then make it pass.
3. Run `npm test` from the repository root. If you changed `infra/`, also run
   `npm --prefix infra test` and `npm --prefix infra run synth:offline`.
4. Open a pull request that says what changed and why. Conventional commit titles
   (`fix: ...`, `feat: ...`) are welcome but not required.

## Rules every change follows

- **No game content.** Never commit models, textures, tables or anything else taken from the game.
  `scripts/check-no-game-content.mjs` checks this in CI.
- **No secrets and no AWS account IDs.** Deploy settings come from the GitHub environment, not the
  repository.
- **Some paths need the maintainer's review:** `.github/`, `infra/`, packaging and the content
  check. [`.github/CODEOWNERS`](../.github/CODEOWNERS) lists them.

## How CI treats pull requests

Pull requests from forks run the tests with read-only permissions and no secrets. Nothing a pull
request runs can deploy; deploys happen only from `main`, after the maintainer approves them. See
[deployment.md](deployment.md).

## Security issues

Don't open a public issue. See [SECURITY.md](SECURITY.md).
