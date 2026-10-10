# Security policy

## Reporting a vulnerability

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a
vulnerability**. Please don't open a public issue.

Include what you found, how to reproduce it, and what it lets someone do.

## What counts

- **The local app.** The server listens on `127.0.0.1` only. A way for a web page or another machine
  to reach it, drive it, or read files outside the game install and its cache is in scope.
- **The hosted site** at altrobe.com and assets.altrobe.com, and the AWS infrastructure in `infra/`.
- **The release packages** and the workflows in `.github/workflows/` that build and deploy them.

Out of scope: denial of service by flooding, and reports that only list missing headers with no way
to use them.

## Supported versions

Only the latest release and `main` get fixes.
