// npm run smoke:package -- dist-packages/Altrobe-<version>-<rid>.zip [--desktop dist-packages/Altrobe-<version>.mcpb]
// Unzips a package into a temp folder, starts it on a free port with a temp cache, and checks that
// /api/v1/status reports the package's version and that / serves the web app. With --desktop, it
// also starts the Claude Desktop bridge from the .mcpb and lists Altrobe's tools through it. Then
// stops everything.
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { exeName, fail, packageName, parsePackageName, readZip } from './lib.mjs'

const [zipPath, desktopFlag, mcpbPath] = process.argv.slice(2)
if (!zipPath || (desktopFlag !== undefined && (desktopFlag !== '--desktop' || !mcpbPath)))
  fail('Usage: npm run smoke:package -- <path to Altrobe-<version>-<rid>.zip> [--desktop <path to Altrobe-<version>.mcpb>]')
const { version, rid } = parsePackageName(zipPath)
const work = mkdtempSync(join(tmpdir(), 'altrobe-smoke-'))
const cache = join(work, 'cache')

for (const entry of readZip(readFileSync(zipPath))) {
  const out = join(work, entry.name)
  mkdirSync(dirname(out), { recursive: true })
  writeFileSync(out, entry.data, { mode: entry.mode })
}
const exe = join(work, packageName(version, rid), exeName(rid))

const port = await new Promise((resolve, reject) => {
  const s = createServer().listen(0, '127.0.0.1', () => {
    const { port } = s.address()
    s.close(() => resolve(port))
  })
  s.on('error', reject)
})
const base = `http://127.0.0.1:${port}`
let output = ''
const children = []
const server = spawn(exe, ['--no-browser', '--port', String(port)], {
  cwd: work,
  env: { ...process.env, ALTROBE_NO_BROWSER: '1', ALTROBE_CACHE_DIR: cache },
})
server.stdout.on('data', (d) => (output += d))
server.stderr.on('data', (d) => (output += d))
let spawnError
const exited = new Promise((resolve) => {
  server.on('exit', resolve)
  server.on('error', (e) => {
    spawnError = e
    resolve()
  })
})

async function check() {
  let status
  for (const started = Date.now(); Date.now() - started < 60_000; await new Promise((r) => setTimeout(r, 250))) {
    if (spawnError) throw new Error(`could not start ${exe}: ${spawnError.message}`)
    if (server.exitCode !== null) throw new Error(`the server exited with code ${server.exitCode}`)
    status = await fetch(`${base}/api/v1/status`).catch(() => undefined)
    if (status) break
  }
  if (!status) throw new Error('the server did not answer within 60 seconds')
  if (status.status !== 200) throw new Error(`GET /api/v1/status answered ${status.status}`)
  const body = await status.json()
  if (body.version !== version) throw new Error(`status reports version ${body.version}, expected ${version}`)
  if (!Array.isArray(body.installs)) throw new Error('status has no installs list')
  console.log(`GET /api/v1/status: 200, version ${body.version}, ${body.installs.length} install(s)`)

  const page = await fetch(`${base}/`)
  const html = await page.text()
  if (page.status !== 200 || !html.includes('<div id="root">') || !html.includes('/static/'))
    throw new Error(`GET / answered ${page.status} without the web app's index.html`)
  console.log('GET /: 200, index.html')

  if (mcpbPath) await checkDesktop()
}

// Speaks newline-delimited JSON-RPC to the bridge over stdio, as Claude Desktop does.
async function checkDesktop() {
  const dir = join(work, 'desktop')
  for (const entry of readZip(readFileSync(mcpbPath))) {
    const out = join(dir, entry.name)
    mkdirSync(dirname(out), { recursive: true })
    writeFileSync(out, entry.data)
  }
  const manifest = JSON.parse(readFileSync(join(dir, 'manifest.json'), 'utf8'))
  if (manifest.version !== version) throw new Error(`the .mcpb manifest says version ${manifest.version}, expected ${version}`)
  const bridge = spawn(process.execPath, [join(dir, manifest.server.entry_point)], {
    env: { ...process.env, ALTROBE_URL: `${base}/mcp` },
    stdio: ['pipe', 'pipe', 'pipe'],
  })
  children.push(bridge)
  let buffer = ''
  const waiting = new Map()
  bridge.stdout.on('data', (d) => {
    buffer += d
    for (let i; (i = buffer.indexOf('\n')) >= 0; buffer = buffer.slice(i + 1)) {
      const message = JSON.parse(buffer.slice(0, i))
      waiting.get(message.id)?.(message)
    }
  })
  bridge.stderr.on('data', (d) => (output += `[bridge] ${d}`))
  const request = (id, method, params) => new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`the bridge did not answer ${method} within 30 seconds`)), 30_000)
    waiting.set(id, (m) => { clearTimeout(timer); resolve(m) })
    bridge.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n')
  })
  const init = await request(1, 'initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'smoke', version: '1' } })
  if (init.error) throw new Error(`initialize through the bridge failed: ${init.error.message}`)
  bridge.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n')
  const list = await request(2, 'tools/list', {})
  const tools = list.result?.tools?.map((t) => t.name) ?? []
  if (!tools.includes('search_items')) throw new Error(`tools/list through the bridge has no search_items: ${JSON.stringify(list)}`)
  console.log(`${mcpbPath}: the bridge lists ${tools.length} tools`)
}

try {
  await check()
  console.log(`${zipPath}: smoke test passed.`)
} catch (e) {
  process.exitCode = 1
  console.error(`${zipPath}: smoke test failed: ${e.message}\n--- server output ---\n${output}`)
} finally {
  for (const child of children) child.kill()
  server.kill()
  await exited
  // Windows can hold the executable open for a moment after the process exits.
  rmSync(work, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })
}
