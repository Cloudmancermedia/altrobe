// Checks Node and the .NET SDK, and installs the web app's dependencies if they are missing.
import { checkTools, ensureWebDeps } from './lib.mjs'

checkTools()
ensureWebDeps()
