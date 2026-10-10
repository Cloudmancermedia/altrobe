// Checks Node and the .NET SDK, and installs the web app's and the Claude Desktop extension's
// dependencies if they are missing or stale. Runs before npm test.
import { checkTools, desktopDir, ensureWebDeps } from './lib.mjs'

checkTools()
ensureWebDeps()
ensureWebDeps(desktopDir)
