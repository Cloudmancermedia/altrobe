// npm start: build the web app, then run the local server, which serves it and opens the browser.
// Arguments after `npm start --` go to the server, for example `npm start -- --port 5300`.
import { checkTools, ensureWebDeps, npmArgs, run, serverProject, webDir } from './lib.mjs'

checkTools()
ensureWebDeps()
console.log('Building the web app...')
const [npm, buildArgs] = npmArgs(['run', 'build'])
run(npm, buildArgs, { cwd: webDir })
console.log('Starting the Altrobe server (the first run compiles it)...')
run('dotnet', ['run', '--project', serverProject, '--', ...process.argv.slice(2)])
