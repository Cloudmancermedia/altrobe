// npm run package:desktop [-- --version 1.2.3]: bundles the Claude Desktop bridge into one file and
// packs it with its manifest into dist-packages/Altrobe-<version>.mcpb. Plain JavaScript, so one
// file serves every OS. Without --version, a vX.Y.Z tag on HEAD gives the version, else 0.1.0-dev.
import { spawnSync } from 'node:child_process'
import { mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { join, relative } from 'node:path'
import { desktopDir, desktopPackageName, ensureWebDeps, fail, packageVersion, parsePackageArgs, root, run, stampManifest } from './lib.mjs'

let explicit
try {
  ({ version: explicit } = parsePackageArgs(process.argv.slice(2)))
} catch (e) {
  fail(e.message)
}
const tag = spawnSync('git', ['describe', '--tags', '--exact-match', '--match', 'v*', 'HEAD'], { cwd: root, encoding: 'utf8' })
let version
try {
  version = packageVersion({ explicit, tag: tag.status === 0 ? tag.stdout.trim() : undefined })
} catch (e) {
  fail(e.message)
}

ensureWebDeps(desktopDir)
const outDir = join(root, 'dist-packages')
const stageDir = join(outDir, '.desktop')
rmSync(stageDir, { recursive: true, force: true })
mkdirSync(join(stageDir, 'server'), { recursive: true })

// One self-contained file: Claude Desktop runs it with its own Node, and the package ships no node_modules.
const esbuild = createRequire(join(desktopDir, 'package.json'))('esbuild')
await esbuild.build({
  entryPoints: [join(desktopDir, 'bridge.mjs')],
  outfile: join(stageDir, 'server', 'index.js'),
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node18',
  legalComments: 'inline',
  logLevel: 'warning',
})
writeFileSync(join(stageDir, 'package.json'), JSON.stringify({ type: 'module' }) + '\n')
const manifest = JSON.parse(readFileSync(join(desktopDir, 'manifest.json'), 'utf8'))
writeFileSync(join(stageDir, 'manifest.json'), JSON.stringify(stampManifest(manifest, version), null, 2) + '\n')

const out = join(outDir, desktopPackageName(version))
const mcpb = join(desktopDir, 'node_modules', '@anthropic-ai', 'mcpb', 'dist', 'cli', 'cli.js')
run(process.execPath, [mcpb, 'pack', stageDir, out])
rmSync(stageDir, { recursive: true, force: true })
console.log(`\n${relative(root, out)}: ${(statSync(out).size / 1024).toFixed(1)} KB`)
