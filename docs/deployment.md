# Deployment

The hosted site runs on AWS. Every AWS resource is defined in `infra/` with the AWS CDK in
TypeScript; nothing is changed by hand in the console. GitHub Actions deploys from `main`. This page
covers the first-time setup, how CI deploys, and how a game bundle is published.

## What is deployed

All in one AWS account, in us-east-1 (CloudFront needs its certificate there).

| Stack | Holds |
| --- | --- |
| `AltrobeDns` | the `altrobe.com` public hosted zone; outputs its nameservers |
| `AltrobeCi` | GitHub's OIDC provider and two roles: `altrobe-github-diff` (read only, any branch or pull request in the repository) and `altrobe-github-deploy` (only jobs in the `production` environment) |
| `AltrobeSite` | the certificate for `altrobe.com`, `www.altrobe.com` and `assets.altrobe.com`; a private bucket and CloudFront distribution for the web app (www redirects to the apex); a private bucket and distribution for the game bundles at `assets.altrobe.com`; DNS records; the web app's files |
| `AltrobeBudget` | a $50-a-month cost budget that counts only resources tagged `project=altrobe` (every stack above carries the tag), emailing at 80% of actual spend and 100% of forecast spend. It exists only when an alert address is set; see below. |

Both roles trust the repository by name only (the token's `sub` claim, such as
`repo:Cloudmancermedia/altrobe:environment:production`, plus `aud`), never by numeric ID, so a
deleted and recreated repository keeps working. No AWS account ID or key is stored in the repository:
the account comes from the credentials in use (`CDK_DEFAULT_ACCOUNT`).

## First-time setup

Run once, from a machine with the AWS CLI signed in to the account (a named profile). Bootstrap the
account for CDK first (`npx cdk bootstrap`), with the default qualifier.

1. **Deploy the zone and the GitHub roles.** An account holds one GitHub OIDC provider at most. Check
   for one first (read only):

   ```sh
   aws iam list-open-id-connect-providers --profile <profile>
   ```

   If it lists `token.actions.githubusercontent.com`, add
   `-c githubOidcProviderArn=arn:aws:iam::<account-id>:oidc-provider/token.actions.githubusercontent.com`
   to the deploy below. **WRITE OPERATION:**

   ```sh
   npm --prefix infra ci
   cd infra && npx cdk deploy AltrobeDns AltrobeCi --profile <profile>
   ```

   Note the outputs: `AltrobeDns.NameServers`, `AltrobeCi.DiffRoleArn` and `AltrobeCi.DeployRoleArn`.

2. **Point the domain at the zone.** At the domain registrar, set the nameservers to the
   four in `AltrobeDns.NameServers`. This is the one step outside AWS. Wait until
   `dig +short NS altrobe.com` lists them (minutes to hours).

3. **Set up GitHub** (repository settings):
   - An environment named `production` with yourself as a required reviewer, and deployment branches
     limited to `main`.
   - Repository variables `AWS_DIFF_ROLE_ARN` and `AWS_DEPLOY_ROLE_ARN`, from step 1's outputs.
   - Repository secret `BUDGET_EMAIL`, the address for budget alerts. It is a secret, not a variable,
     so GitHub masks it in this public repository's Actions logs.

4. **Deploy the site.** Either merge to `main` and approve the `production` deployment, or run it
   locally once. **WRITE OPERATION:**

   ```sh
   npm --prefix web ci
   VITE_ALTROBE_BUNDLE_URL=https://assets.altrobe.com npm --prefix web run build:static
   cd infra && npx cdk deploy AltrobeSite --profile <profile>
   ```

   The certificate validates through DNS, so this waits until step 2 has taken effect.

5. Add repository variables `BUNDLE_BUCKET` and `ASSET_DISTRIBUTION_ID` from `AltrobeSite`'s
   outputs, for the bundle publish step.

### The budget

Budgets see a tag only after it is activated for cost allocation, and AWS lists a new tag key for
activation up to 24 hours after resources first carry it. `AltrobeBudget` activates `project` with a
custom resource on every deploy, so the budget starts counting from the first deploy after AWS has
seen the tag. Until then it reports no spend. To deploy it locally, set the address in the
environment: `ALTROBE_BUDGET_EMAIL=you@example.com npx cdk deploy AltrobeBudget --profile <profile>`
(**WRITE OPERATION**).

## How CI deploys

- **Every pull request and push** (`.github/workflows/ci.yml`): the `infra` job type-checks, runs the
  CDK assertion tests and synthesizes offline with a placeholder account. No AWS access.
- **`.github/workflows/deploy.yml`**: on pull requests from this repository and on pushes to `main`,
  the `diff` job builds the static web app and runs `cdk diff --all` with the diff role. On `main`,
  the `deploy` job then waits for approval in the `production` environment, builds the web app and runs
  `cdk deploy --all`. Both jobs skip until their role variable exists. Workflows never use
  `pull_request_target`; top-level permissions are empty and only the jobs that assume a role get
  `id-token: write`.

`npm --prefix infra run synth:offline` and `npm --prefix infra test` run the same checks locally,
without AWS credentials.

Both deploy.yml jobs build the web app with `npm --prefix web run build:static`, the static build
that reads bundles from `assets.altrobe.com`.

## Repository settings

These live in GitHub's settings, not in files, so set them by hand on the repository (again after
recreating it). The files that go with them are in `.github/`: `CODEOWNERS`, `dependabot.yml`, and
the `codeql.yml` and `dependency-review.yml` workflows. Every action in the workflows is pinned to a
commit SHA; Dependabot keeps the pins current.

- **A ruleset on `main`:** require a pull request, require the status checks (CI's `test` and `infra`
  jobs, CodeQL and dependency review), and block force pushes and deletion. Leave required approvals
  at zero: GitHub doesn't let an author approve their own pull request, and only the maintainer has
  write access, so nobody else can merge. `CODEOWNERS` still requests the maintainer's review on
  other people's pull requests.
- **Code security:** turn on the dependency graph (the dependency review workflow fails without it),
  secret scanning with push protection, Dependabot alerts, Dependabot
  security updates, and private vulnerability reporting ([SECURITY.md](SECURITY.md) points reporters
  there).
- **Actions:** set the default workflow permissions to read-only, and require approval before
  running workflows from first-time contributors' forks.

## Publishing a bundle

Bundles are data, not infrastructure: CDK creates the bundle bucket and lets the deploy role write it,
and `npm run publish:bundle` uploads each build (see
[hosted-pipeline.md](hosted-pipeline.md#publish-a-build)). A build goes to `{product}/{build}/` before
`{product}/current.json` is rewritten, then `current.json` is invalidated in CloudFront. Builds are
cached for a year and `current.json` for a minute.

CloudFront compresses only files between 1 KB and 10 MB. The publish step must gzip larger files
itself (such as `catalog.json` and big models) and upload them with `Content-Encoding: gzip`.
