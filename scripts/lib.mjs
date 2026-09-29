// Shared helpers for the root npm scripts. Plain Node, no dependencies.
import { spawnSync } from 'node:child_process'
import { existsSync, statSync } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import { deflateRawSync, inflateRawSync } from 'node:zlib'
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

// npm writes node_modules/.package-lock.json on install; if the checked-in lockfile is newer, a
// pull added or changed dependencies since the last install.
export function webDepsStale(dir) {
  const installed = join(dir, 'node_modules', '.package-lock.json')
  if (!existsSync(installed)) return true
  return statSync(join(dir, 'package-lock.json')).mtimeMs > statSync(installed).mtimeMs
}

export function ensureWebDeps() {
  if (!webDepsStale(webDir)) return
  console.log('Installing web app dependencies (npm ci in web/)...')
  const [cmd, args] = npmArgs(['ci'])
  run(cmd, args, { cwd: webDir })
}

// Runtimes the downloadable packages are built for (dotnet publish -r).
export const RIDS = ['win-x64', 'osx-arm64', 'osx-x64', 'linux-x64']
export const DEV_VERSION = '0.1.0-dev'
const VERSION = /^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$/

/** The package version: `explicit` (a leading v is dropped), else a vX.Y.Z `tag`, else DEV_VERSION. */
export function packageVersion({ explicit, tag } = {}) {
  if (explicit !== undefined) {
    const v = explicit.replace(/^v/, '')
    if (!VERSION.test(v)) throw new Error(`"${explicit}" is not a version like 1.2.3 or 1.2.3-beta.1`)
    return v
  }
  const fromTag = tag?.replace(/^v/, '')
  return tag?.startsWith('v') && VERSION.test(fromTag) ? fromTag : DEV_VERSION
}

export const exeName = (rid) => (rid.startsWith('win-') ? 'Altrobe.exe' : 'Altrobe')

export function packageName(version, rid) {
  if (!RIDS.includes(rid)) throw new Error(`Unknown runtime "${rid}". Use one of: ${RIDS.join(', ')}`)
  return `Altrobe-${version}-${rid}`
}

export function parsePackageName(file) {
  const m = new RegExp(`^Altrobe-(.+)-(${RIDS.join('|')})\\.zip$`).exec(basename(file))
  if (!m || !VERSION.test(m[1])) throw new Error(`${file} is not an Altrobe package name like Altrobe-1.2.3-linux-x64.zip`)
  return { version: m[1], rid: m[2] }
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})

function crc32(buf) {
  let c = 0xffffffff
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function dosDateTime(d) {
  return {
    time: (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1),
    date: ((d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate(),
  }
}

/**
 * A zip archive of `files` ({name, data, mode}), deflated. Entries record Unix permissions, so the
 * executable stays executable when unzipped on macOS and Linux. No zip64: every file under 4 GB.
 */
export function writeZip(files, when = new Date()) {
  const { time, date } = dosDateTime(when)
  const locals = [], centrals = []
  let offset = 0
  for (const { name, data, mode } of files) {
    if (!name || name.startsWith('/') || name.includes('\\') || name.split('/').includes('..') || /^[A-Za-z]:/.test(name))
      throw new Error(`Bad zip entry name "${name}"`)
    const nameBuf = Buffer.from(name, 'utf8')
    const packed = deflateRawSync(data, { level: 9 })
    const crc = crc32(data)
    const local = Buffer.alloc(30)
    local.writeUInt32LE(0x04034b50, 0)
    local.writeUInt16LE(20, 4)
    local.writeUInt16LE(0x0800, 6) // names are UTF-8
    local.writeUInt16LE(8, 8)
    local.writeUInt16LE(time, 10)
    local.writeUInt16LE(date, 12)
    local.writeUInt32LE(crc, 14)
    local.writeUInt32LE(packed.length, 18)
    local.writeUInt32LE(data.length, 22)
    local.writeUInt16LE(nameBuf.length, 26)
    const central = Buffer.alloc(46)
    central.writeUInt32LE(0x02014b50, 0)
    central.writeUInt16LE((3 << 8) | 20, 4) // made by Unix, so unzip reads the permissions below
    central.writeUInt16LE(20, 6)
    central.writeUInt16LE(0x0800, 8)
    central.writeUInt16LE(8, 10)
    central.writeUInt16LE(time, 12)
    central.writeUInt16LE(date, 14)
    central.writeUInt32LE(crc, 16)
    central.writeUInt32LE(packed.length, 20)
    central.writeUInt32LE(data.length, 24)
    central.writeUInt16LE(nameBuf.length, 28)
    central.writeUInt32LE(((0o100000 | mode) << 16) >>> 0, 38) // regular file + mode
    central.writeUInt32LE(offset, 42)
    locals.push(local, nameBuf, packed)
    centrals.push(central, nameBuf)
    offset += local.length + nameBuf.length + packed.length
  }
  const cd = Buffer.concat(centrals)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(files.length, 8)
  end.writeUInt16LE(files.length, 10)
  end.writeUInt32LE(cd.length, 12)
  end.writeUInt32LE(offset, 16)
  return Buffer.concat([...locals, cd, end])
}

/** The entries of a zip made by writeZip (or any plain, non-zip64 zip): {name, data, mode}. */
export function readZip(buf) {
  let end = buf.length - 22
  while (end >= 0 && buf.readUInt32LE(end) !== 0x06054b50) end--
  if (end < 0) throw new Error('Not a zip file')
  const count = buf.readUInt16LE(end + 10)
  let p = buf.readUInt32LE(end + 16)
  const entries = []
  for (let i = 0; i < count; i++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('Corrupt zip central directory')
    const method = buf.readUInt16LE(p + 10)
    const size = buf.readUInt32LE(p + 20)
    const nameLen = buf.readUInt16LE(p + 28), extraLen = buf.readUInt16LE(p + 30), commentLen = buf.readUInt16LE(p + 32)
    const mode = (buf.readUInt32LE(p + 38) >>> 16) & 0o777
    const local = buf.readUInt32LE(p + 42)
    const name = buf.toString('utf8', p + 46, p + 46 + nameLen)
    const start = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28)
    const raw = buf.subarray(start, start + size)
    entries.push({ name, mode, data: method === 8 ? inflateRawSync(raw) : Buffer.from(raw) })
    p += 46 + nameLen + extraLen + commentLen
  }
  return entries
}
