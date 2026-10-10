# Altrobe: agent notes

## Branches

Work on a branch cut from `origin/main`, one task at a time. Prefer switching branches to new
worktrees: a worktree reinstalls `web/node_modules` and rebuilds the .NET solution from scratch.

## Dev and test servers

Run one at a time. Before handing over a running server, build that branch's web app and check the
startup log for the web folder it serves: a server can fall back to another checkout's build.

## Tests

Run `npm test` before a commit, not after every edit. Package builds and `npm run smoke:package`
only when packaging changes. The browser checks (`web/e2e`, opt-in) keep converted models in
`~/.altrobe-e2e`; set `ALTROBE_E2E_FRESH=1` only when testing conversion itself.

## AWS and CI/CD

All AWS infrastructure is defined in `infra/` with the AWS CDK in TypeScript; no console changes. GitHub Actions runs the tests and `cdk diff` on pull requests, and deploys from `main` through an OIDC role (no stored keys), behind the `production` environment's approval. The only local deploy is the first, which creates that role. Game bundles are data, not infrastructure: `npm run publish:bundle` uploads them.
