// DEV ONLY. Serves a baked bundle (output/bundle, from `bake`) at /bundle for `npm run dev:static`,
// so the static site can be tried locally before anything is uploaded. Enabled only by `vite` in
// static mode; it never runs in, or ships with, a build. Reads files at request time and copies nothing.

import { createReadStream, statSync } from 'node:fs'
import { extname, join, resolve, sep } from 'node:path'
import type { Plugin } from 'vite'

const PREFIX = '/bundle/'
const TYPES: Record<string, string> = { '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png' }

export const contentType = (file: string) => TYPES[extname(file).toLowerCase()] ?? 'application/octet-stream'

/** The file a /bundle URL names, or null if it is not one or would leave the bundle folder. */
export function bundleFile(root: string, url: string): string | null {
  const path = url.split('?')[0]
  if (!path.startsWith(PREFIX)) return null
  let rel: string
  try { rel = decodeURIComponent(path.slice(PREFIX.length)) } catch { return null }
  const base = resolve(root)
  const file = resolve(join(base, rel))
  return file.startsWith(base + sep) ? file : null
}

export function bundleServer({ root }: { root: string }): Plugin {
  return {
    name: 'altrobe-bundle-server',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const file = bundleFile(root, req.url ?? '')
        if (!file) return next()
        try {
          if (!statSync(file).isFile()) throw new Error('not a file')
        } catch {
          res.statusCode = 404
          res.end('not in the bundle')
          return
        }
        res.setHeader('content-type', contentType(file))
        createReadStream(file).pipe(res)
      })
    },
  }
}
