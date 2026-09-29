// npm run smoke:package -- dist-packages/Altrobe-<version>-<rid>.zip
// Unzips a package into a temp folder, starts it on a free port with a temp cache, and checks that
// /api/v1/status reports the package's version and that / serves the web app. Then stops it.
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { exeName, fail, packageName, parsePackageName, readZip } from './lib.mjs'

const zipPath = process.argv[2]
if (!zipPath) fail('Usage: npm run smoke:package -- <path to Altrobe-<version>-<rid>.zip>')
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
}

try {
  await check()
  console.log(`${zipPath}: smoke test passed.`)
} catch (e) {
  process.exitCode = 1
  console.error(`${zipPath}: smoke test failed: ${e.message}\n--- server output ---\n${output}`)
} finally {
  server.kill()
  await exited
  // Windows can hold the executable open for a moment after the process exits.
  rmSync(work, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })
}
