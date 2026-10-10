// The browser checks keep converted assets between runs, so a run only converts what changed.
// The server already files its cache by product and build; this keys it by the converter's
// sources as well, so a change to the importer never runs against assets the old code made.
import { createHash } from 'node:crypto'
import { mkdirSync, readdirSync, readFileSync, rmSync } from 'node:fs'
import { homedir } from 'node:os'
import { join, relative } from 'node:path'

export const defaultCacheRoot = () => process.env.ALTROBE_E2E_CACHE || join(homedir(), '.altrobe-e2e')

// A short hash of every file under dir, skipping build output.
export function sourceHash(dir: string): string {
  const hash = createHash('sha256')
  const walk = (d: string) => {
    for (const e of readdirSync(d, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const p = join(d, e.name)
      if (e.isDirectory()) { if (e.name !== 'bin' && e.name !== 'obj') walk(p) }
      else if (e.isFile()) hash.update(relative(dir, p)).update('\0').update(readFileSync(p)).update('\0')
    }
  }
  walk(dir)
  return hash.digest('hex').slice(0, 12)
}

// Returns root/key, emptied first when fresh is set. Caches for other keys are deleted so old
// converter output doesn't pile up.
export function prepareCache(root: string, key: string, fresh: boolean): string {
  mkdirSync(root, { recursive: true })
  for (const name of readdirSync(root)) {
    if (name !== key || fresh) rmSync(join(root, name), { recursive: true, force: true })
  }
  const cache = join(root, key)
  mkdirSync(cache, { recursive: true })
  return cache
}
