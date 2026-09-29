// npm start: build the web app, then run the local server, which serves it and opens the browser.
// Arguments after `npm start --` go to the server, for example `npm start -- --port 5300`.
import { spawn } from 'node:child_process'
import { checkTools, ensureWebDeps, npmArgs, run, serverDll, serverProject, webDir } from './lib.mjs'

checkTools()
ensureWebDeps()
console.log('Building the web app...')
const [npm, buildArgs] = npmArgs(['run', 'build'])
run(npm, buildArgs, { cwd: webDir })
console.log('Building the Altrobe server...')
run('dotnet', ['build', serverProject, '--nologo', '-v', 'quiet'])

// Not `dotnet run`: it ignores a signal sent to it alone and leaves the server running, so this
// script runs the built server itself and passes Ctrl+C or a stop request on to it.
const server = spawn('dotnet', [serverDll, ...process.argv.slice(2)], { stdio: 'inherit' })
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => server.kill(signal))
server.on('error', (e) => { console.error(`Could not start the server: ${e.message}`); process.exit(1) })
server.on('exit', (code, signal) => process.exit(code ?? (signal ? 0 : 1)))
