// Starts the real .NET server and the Vite dev server for the browser checks, selects the
// Forever product, and returns the teardown. Tests read the app URL from ALTROBE_E2E_URL.
import { spawn, spawnSync, type ChildProcess } from 'node:child_process'
import { once } from 'node:events'
import { createWriteStream } from 'node:fs'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { createServer as netServer } from 'node:net'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
import { enabled, wowPath } from './env'

const webDir = join(dirname(fileURLToPath(import.meta.url)), '..')
const serverProject = join(webDir, '..', 'importer', 'src', 'Altrobe.Server')
const serverDll = join(serverProject, 'bin', 'Debug', 'net10.0', 'Altrobe.Server.dll')
// Nothing listens on port 9 (discard), so any HTTP(S) request the server makes through the
// default proxy settings fails fast instead of reaching the internet.
const DEAD_PROXY = 'http://127.0.0.1:9'

interface Product { product: string; build: string; isForever: boolean }
interface Status { installs: { path: string; products: Product[] }[] }

function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const s = netServer().listen(0, '127.0.0.1', () => {
      const { port } = s.address() as { port: number }
      s.close(() => resolve(port))
    }).on('error', reject)
  })
}

async function waitForStatus(url: string, server: ChildProcess): Promise<Status> {
  for (let i = 0; i < 120; i++) {
    if (server.exitCode !== null) throw new Error(`the server exited with code ${server.exitCode}`)
    try {
      const r = await fetch(url)
      if (r.ok) return await r.json() as Status
    } catch { /* not listening yet */ }
    await new Promise((r) => setTimeout(r, 500))
  }
  throw new Error(`the server did not answer ${url}`)
}

export default async function globalSetup() {
  if (!enabled) return

  // Build while NuGet is reachable; the server itself then runs with no network.
  const build = spawnSync('dotnet', ['build', serverProject, '--nologo', '-v', 'quiet'], { stdio: 'inherit' })
  if (build.status !== 0) throw new Error('dotnet build failed')

  const cache = await mkdtemp(join(tmpdir(), 'altrobe-e2e-'))
  const [serverPort, webPort] = [await freePort(), await freePort()]
  const api = `http://127.0.0.1:${serverPort}`
  const log = createWriteStream(join(cache, 'server.log'))
  const server = spawn('dotnet', [serverDll, '--no-browser', '--port', String(serverPort)], {
    env: { ...process.env, ALTROBE_CACHE_DIR: cache, HTTPS_PROXY: DEAD_PROXY, HTTP_PROXY: DEAD_PROXY, ALL_PROXY: DEAD_PROXY, NO_PROXY: '' },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  server.stdout!.pipe(log)
  server.stderr!.pipe(log)

  const stop = async () => {
    if (server.exitCode === null) { server.kill(); await once(server, 'exit') }
    await rm(cache, { recursive: true, force: true })
  }

  try {
    const status = await waitForStatus(`${api}/api/v1/status`, server)
    // The server reports full paths without a trailing separator; normalize ALTROBE_WOW_PATH the same way.
    const norm = (p: string) => resolve(p).replace(/[\\/]+$/, '')
    const installs = status.installs.filter((i) => !process.env.ALTROBE_WOW_PATH || norm(i.path) === norm(wowPath!))
    const pick = installs.flatMap((i) => i.products.filter((p) => p.isForever).map((p) => ({ path: i.path, product: p.product })))[0]
    if (!pick) throw new Error(`no World of Warcraft: Forever product found in ${JSON.stringify(status.installs)}`)
    const r = await fetch(`${api}/api/v1/install`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(pick) })
    if (!r.ok) throw new Error(`POST /install failed: ${r.status} ${await r.text()}`)

    // vite.config.ts reads ALTROBE_API to proxy /api and /assets to the server.
    process.env.ALTROBE_API = api
    const vite = await createServer({ root: webDir, logLevel: 'warn', server: { host: '127.0.0.1', port: webPort, strictPort: true } })
    await vite.listen()
    process.env.ALTROBE_E2E_URL = `http://127.0.0.1:${webPort}/`
    process.env.ALTROBE_E2E_SERVER = api

    return async () => { await vite.close(); await stop() }
  } catch (e) {
    console.error(`server log:\n${await readFile(join(cache, 'server.log'), 'utf8').catch(() => '(none)')}`)
    await stop()
    throw e
  }
}
