// npm run dev: the .NET server (no browser) and Vite with hot reload, which proxies /api and
// /assets to it. Ctrl+C stops both, and so does either one exiting.
// Arguments after `npm run dev --` go to Vite, for example `npm run dev -- --port 5180`.
import { spawn } from 'node:child_process'
import { join } from 'node:path'
import { checkTools, ensureWebDeps, fail, prefixLines, run, serverDll, serverPort, serverProject, webDir } from './lib.mjs'

checkTools()
ensureWebDeps()
let port
try { port = serverPort(process.env) } catch (e) { fail(e.message) }
run('dotnet', ['build', serverProject, '--nologo', '-v', 'quiet'])

const children = []
let stopping = false
let exitCode = 0

function start(name, cmd, args, options) {
  const child = spawn(cmd, args, { ...options, stdio: ['ignore', 'pipe', 'pipe'] })
  for (const stream of [child.stdout, child.stderr]) {
    let rest = ''
    stream.setEncoding('utf8')
    stream.on('data', (chunk) => {
      const r = prefixLines(`[${name}] `, rest + chunk)
      rest = r.rest
      process.stdout.write(r.out)
    })
    stream.on('end', () => { if (rest) process.stdout.write(`[${name}] ${rest}\n`) })
  }
  child.on('error', (e) => { console.error(`[${name}] could not start: ${e.message}`); stop(1) })
  child.on('exit', (code, signal) => {
    if (!stopping) console.log(`[${name}] exited (${signal ?? code}); stopping the other process`)
    stop(code ?? 0)
  })
  children.push(child)
}

function stop(code) {
  if (!stopping) exitCode = code
  stopping = true
  for (const c of children) if (c.exitCode === null && c.signalCode === null) c.kill()
  if (children.every((c) => c.exitCode !== null || c.signalCode !== null)) process.exit(exitCode)
}

process.on('SIGINT', () => stop(0))
process.on('SIGTERM', () => stop(0))

start('server', 'dotnet', [serverDll, '--no-browser', '--port', String(port)], { env: process.env })
start('web', process.execPath, [join(webDir, 'node_modules', 'vite', 'bin', 'vite.js'), ...process.argv.slice(2)], {
  cwd: webDir,
  env: { ...process.env, ALTROBE_API: `http://127.0.0.1:${port}` },
})
