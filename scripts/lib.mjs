// Shared helpers for the root npm scripts. Plain Node, no dependencies.
import { spawnSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

export const root = join(dirname(fileURLToPath(import.meta.url)), '..')
export const webDir = join(root, 'web')
export const serverProject = join(root, 'importer', 'src', 'Altrobe.Server')
export const DEFAULT_PORT = 5161
// Where `dotnet build` puts the server. Running the DLL directly gives one process to stop,
// where `dotnet run` would add a wrapper process in front of it.
export const serverDll = join(serverProject, 'bin', 'Debug', 'net10.0', 'Altrobe.Server.dll')

const major = (v) => Number.parseInt(String(v).replace(/^v/, ''), 10)

export const nodeIsNewEnough = (version) => major(version) >= 20

/** True when `dotnet --list-sdks` output lists a .NET 10 or newer SDK. */
export const hasDotnetSdk = (listSdks) => listSdks.split(/\r?\n/).some((line) => major(line) >= 10)

export function serverPort(env) {
  if (env.ALTROBE_PORT === undefined || env.ALTROBE_PORT === '') return DEFAULT_PORT
  const p = Number(env.ALTROBE_PORT)
  if (!Number.isInteger(p) || p < 1 || p > 65535) throw new Error(`ALTROBE_PORT must be a port number, got "${env.ALTROBE_PORT}"`)
  return p
}

/** Prefixes each complete line of `text`; `rest` is the unfinished last line, for the next chunk. */
export function prefixLines(prefix, text) {
  const lines = text.split(/\r?\n/)
  const rest = lines.pop()
  return { out: lines.map((l) => prefix + l + '\n').join(''), rest }
}

const BLIZZARD_TYPES = ['.blp', '.m2', '.skin', '.skel', '.anim', '.db2', '.dbc', '.casc', '.wmo', '.adt', '.bls']
const MEDIA_TYPES = ['.png', '.glb']
// Our own images and models may live only here. A converted game texture or model is also a .png
// or .glb, so anywhere else is refused.
const MEDIA_DIRS = ['web/public/', 'web/src/']
// Individual files allowed outside MEDIA_DIRS, each with a reason. None so far.
const MEDIA_EXCEPTIONS = []

/** Problems with a list of tracked paths (forward slashes), as readable lines. Empty when clean. */
export function gameContentProblems(paths) {
  const problems = []
  for (const p of paths) {
    const ext = p.slice(p.lastIndexOf('.')).toLowerCase()
    if (BLIZZARD_TYPES.includes(ext)) problems.push(`${p}: Blizzard content type ${ext}`)
    else if (MEDIA_TYPES.includes(ext) && !MEDIA_DIRS.some((d) => p.startsWith(d)) && !MEDIA_EXCEPTIONS.includes(p))
      problems.push(`${p}: ${ext} outside web/public and web/src`)
  }
  return problems
}

export function fail(message) {
  console.error(`\n${message}\n`)
  process.exit(1)
}

/** Stops with install links when Node or the .NET SDK is missing or too old. */
export function checkTools() {
  if (!nodeIsNewEnough(process.versions.node))
    fail(`Altrobe needs Node.js 20 or newer; this is ${process.versions.node}. Get it from https://nodejs.org`)
  const r = spawnSync('dotnet', ['--list-sdks'], { encoding: 'utf8' })
  if (r.error || r.status !== 0)
    fail('Altrobe needs the .NET 10 SDK, and `dotnet` was not found. Get it from https://dotnet.microsoft.com/download')
  if (!hasDotnetSdk(r.stdout))
    fail(`Altrobe needs the .NET 10 SDK. Installed SDKs:\n${r.stdout.trim() || '(none)'}\nGet it from https://dotnet.microsoft.com/download`)
}

/** Runs npm with the same npm that started this script. */
export function npmArgs(args) {
  // npm sets npm_execpath to its own CLI script, which avoids spawning npm.cmd through a shell on Windows.
  const cli = process.env.npm_execpath
  return cli && cli.endsWith('.js') ? [process.execPath, [cli, ...args]] : ['npm', args]
}

/** Runs a command to completion with inherited output; exits this process if it fails. */
export function run(cmd, args, options = {}) {
  const r = spawnSync(cmd, args, { stdio: 'inherit', shell: process.platform === 'win32' && cmd === 'npm', ...options })
  if (r.error) fail(`Could not run ${cmd}: ${r.error.message}`)
  if (r.status !== 0) process.exit(r.status ?? 1)
}

export function ensureWebDeps() {
  if (existsSync(join(webDir, 'node_modules'))) return
  console.log('Installing web app dependencies (npm ci in web/)...')
  const [cmd, args] = npmArgs(['ci'])
  run(cmd, args, { cwd: webDir })
}
