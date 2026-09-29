// npm run package -- <rid> [--version 1.2.3]: builds the web app, publishes a self-contained
// single-file server for one runtime, and zips both into dist-packages/Altrobe-<version>-<rid>.zip.
// Without --version, a vX.Y.Z tag on HEAD gives the version, else 0.1.0-dev.
import { spawnSync } from 'node:child_process'
import { chmodSync, cpSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import {
  RIDS,
  checkTools,
  ensureWebDeps,
  exeName,
  fail,
  npmArgs,
  packageName,
  packageVersion,
  root,
  run,
  serverProject,
  webDir,
  writeZip,
} from './lib.mjs'

const args = process.argv.slice(2)
let rid, explicit
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--version') explicit = args[++i]
  else rid ??= args[i]
}
if (!RIDS.includes(rid)) fail(`Usage: npm run package -- <${RIDS.join('|')}> [--version 1.2.3]`)
const tag = spawnSync('git', ['describe', '--tags', '--exact-match', '--match', 'v*', 'HEAD'], { cwd: root, encoding: 'utf8' })
let version
try {
  version = packageVersion({ explicit, tag: tag.status === 0 ? tag.stdout.trim() : undefined })
} catch (e) {
  fail(e.message)
}
const name = packageName(version, rid)
const outDir = join(root, 'dist-packages')
const publishDir = join(outDir, '.publish', rid)
const stageDir = join(outDir, name)

checkTools()
ensureWebDeps()
console.log('Building the web app...')
const [npm, buildArgs] = npmArgs(['run', 'build'])
run(npm, buildArgs, { cwd: webDir })

console.log(`Publishing Altrobe ${version} for ${rid}...`)
rmSync(publishDir, { recursive: true, force: true })
// No trimming: TACTSharp, DBCD and System.Text.Json use reflection that trimming would break.
run('dotnet', [
  'publish', serverProject, '-c', 'Release', '-r', rid, '--self-contained',
  '-p:PublishSingleFile=true', `-p:Version=${version}`, '-p:DebugType=none',
  '-o', publishDir, '--nologo',
])

rmSync(stageDir, { recursive: true, force: true })
mkdirSync(stageDir, { recursive: true })
const exe = exeName(rid)
renameSync(join(publishDir, rid.startsWith('win-') ? 'Altrobe.Server.exe' : 'Altrobe.Server'), join(stageDir, exe))
cpSync(join(publishDir, 'appsettings.json'), join(stageDir, 'appsettings.json'))
cpSync(join(webDir, 'dist'), join(stageDir, 'wwwroot'), { recursive: true })
cpSync(join(root, 'LICENSE'), join(stageDir, 'LICENSE'))
cpSync(join(root, 'THIRD_PARTY_NOTICES.md'), join(stageDir, 'THIRD_PARTY_NOTICES.md'))
let readme = readFileSync(join(root, 'packaging', 'READ ME FIRST.txt'), 'utf8').replaceAll('{{version}}', version)
if (rid.startsWith('win-')) readme = readme.replace(/\r?\n/g, '\r\n')
writeFileSync(join(stageDir, 'READ ME FIRST.txt'), readme)
chmodSync(join(stageDir, exe), 0o755)

const files = []
const walk = (dir) => {
  for (const entry of readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = join(dir, entry.name)
    if (entry.isDirectory()) walk(full)
    else files.push({ name: `${name}/${relative(stageDir, full).split(sep).join('/')}`, data: readFileSync(full), mode: entry.name === exe ? 0o755 : 0o644 })
  }
}
walk(stageDir)
const zipPath = join(outDir, `${name}.zip`)
writeFileSync(zipPath, writeZip(files))

const unzipped = files.reduce((n, f) => n + f.data.length, 0)
rmSync(join(outDir, '.publish'), { recursive: true, force: true })
rmSync(stageDir, { recursive: true, force: true })
const mb = (n) => `${(n / 1024 / 1024).toFixed(1)} MB`
console.log(`\n${relative(root, zipPath)}: ${mb(statSync(zipPath).size)} (${mb(unzipped)} unzipped, ${files.length} files)`)
