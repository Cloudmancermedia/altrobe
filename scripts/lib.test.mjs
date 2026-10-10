import assert from 'node:assert/strict'
import { test } from 'node:test'
import { mkdtempSync, mkdirSync, rmSync, utimesSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import {
  publishPlan,
  gameContentProblems,
  hasDotnetSdk,
  nodeIsNewEnough,
  packageName,
  desktopPackageName,
  stampManifest,
  packageVersion,
  parsePackageName,
  prefixLines,
  readZip,
  serverPort,
  webDepsStale,
  writeZip,
} from './lib.mjs'

test('node 20 or newer is accepted', () => {
  assert.equal(nodeIsNewEnough('20.0.0'), true)
  assert.equal(nodeIsNewEnough('24.14.1'), true)
  assert.equal(nodeIsNewEnough('18.19.0'), false)
})

test('a .NET 10 or newer SDK is found in dotnet --list-sdks output', () => {
  assert.equal(hasDotnetSdk('8.0.404 [/usr/share/dotnet/sdk]\n10.0.401 [/usr/share/dotnet/sdk]\n'), true)
  assert.equal(hasDotnetSdk('11.0.100-preview.1 [C:\\Program Files\\dotnet\\sdk]\r\n'), true)
  assert.equal(hasDotnetSdk('8.0.404 [/usr/share/dotnet/sdk]\n9.0.100 [/x]\n'), false)
  assert.equal(hasDotnetSdk(''), false)
})

test('the server port comes from ALTROBE_PORT, else 5161', () => {
  assert.equal(serverPort({}), 5161)
  assert.equal(serverPort({ ALTROBE_PORT: '5299' }), 5299)
  assert.throws(() => serverPort({ ALTROBE_PORT: 'abc' }), /ALTROBE_PORT/)
  assert.throws(() => serverPort({ ALTROBE_PORT: '70000' }), /ALTROBE_PORT/)
})

test('prefixLines tags each complete line and keeps the partial rest', () => {
  assert.deepEqual(prefixLines('[web] ', 'a\nb\nc'), { out: '[web] a\n[web] b\n', rest: 'c' })
  assert.deepEqual(prefixLines('[web] ', 'a\r\n'), { out: '[web] a\n', rest: '' })
})

test('game content: Blizzard file types are refused anywhere', () => {
  assert.deepEqual(gameContentProblems(['web/src/a.ts', 'tools/x.BLP', 'out/model.m2', 'a.db2']), [
    'tools/x.BLP: Blizzard content type .blp',
    'out/model.m2: Blizzard content type .m2',
    'a.db2: Blizzard content type .db2',
  ])
})

test('game content: images and models only in web/public and web/src', () => {
  assert.deepEqual(gameContentProblems(['web/public/logo.png', 'web/src/ui/x.glb', 'docs/shot.png', 'tools/out/a.glb']), [
    'docs/shot.png: .png outside web/public and web/src',
    'tools/out/a.glb: .glb outside web/public and web/src',
  ])
})

test('web dependencies are stale when missing or older than the lockfile', (t) => {
  const dir = mkdtempSync(join(tmpdir(), 'altrobe-deps-'))
  t.after(() => rmSync(dir, { recursive: true, force: true }))
  writeFileSync(join(dir, 'package-lock.json'), '{}')
  assert.equal(webDepsStale(dir), true, 'no node_modules')
  mkdirSync(join(dir, 'node_modules'))
  assert.equal(webDepsStale(dir), true, 'node_modules without an installed lockfile')
  writeFileSync(join(dir, 'node_modules', '.package-lock.json'), '{}')
  const old = new Date(Date.now() - 60_000), now = new Date()
  utimesSync(join(dir, 'node_modules', '.package-lock.json'), old, old)
  utimesSync(join(dir, 'package-lock.json'), now, now)
  assert.equal(webDepsStale(dir), true, 'lockfile changed after the install')
  utimesSync(join(dir, 'node_modules', '.package-lock.json'), now, now)
  utimesSync(join(dir, 'package-lock.json'), old, old)
  assert.equal(webDepsStale(dir), false, 'installed after the lockfile')
})

test('package version: an explicit version wins, then a v tag on HEAD, else 0.1.0-dev', () => {
  assert.equal(packageVersion({ explicit: '1.2.3', tag: 'v9.9.9' }), '1.2.3')
  assert.equal(packageVersion({ tag: 'v0.2.0' }), '0.2.0')
  assert.equal(packageVersion({ tag: 'v0.2.0-beta.1' }), '0.2.0-beta.1')
  assert.equal(packageVersion({ tag: 'not-a-release' }), '0.1.0-dev')
  assert.equal(packageVersion({}), '0.1.0-dev')
  assert.equal(packageVersion({ explicit: 'v1.0.0' }), '1.0.0')
  assert.throws(() => packageVersion({ explicit: '1.0' }), /version/)
  assert.throws(() => packageVersion({ explicit: '1.0.0/../x' }), /version/)
})

test('package names round-trip and only known runtimes are accepted', () => {
  assert.equal(packageName('0.2.0', 'osx-arm64'), 'Altrobe-0.2.0-osx-arm64')
  assert.deepEqual(parsePackageName('dist-packages/Altrobe-0.2.0-beta.1-win-x64.zip'), { version: '0.2.0-beta.1', rid: 'win-x64' })
  assert.throws(() => packageName('0.2.0', 'freebsd-x64'), /runtime/)
  assert.throws(() => parsePackageName('Other-1.0.0-linux-x64.zip'), /package/)
})

test('zip: files, folders and the executable bit survive a round trip', () => {
  const big = Buffer.alloc(200_000, 'altrobe ')
  const zip = writeZip([
    { name: 'Altrobe-1.0.0-linux-x64/Altrobe', data: Buffer.from('#!/bin/sh\n'), mode: 0o755 },
    { name: 'Altrobe-1.0.0-linux-x64/wwwroot/index.html', data: big, mode: 0o644 },
    { name: 'Altrobe-1.0.0-linux-x64/READ ME FIRST.txt', data: Buffer.alloc(0), mode: 0o644 },
  ])
  assert.ok(zip.length < big.length / 10, 'compressed')
  const entries = readZip(zip)
  assert.deepEqual(entries.map((e) => [e.name, e.mode]), [
    ['Altrobe-1.0.0-linux-x64/Altrobe', 0o755],
    ['Altrobe-1.0.0-linux-x64/wwwroot/index.html', 0o644],
    ['Altrobe-1.0.0-linux-x64/READ ME FIRST.txt', 0o644],
  ])
  assert.ok(entries[1].data.equals(big))
  assert.equal(entries[0].data.toString(), '#!/bin/sh\n')
})

test('zip: names that would escape the extract folder are refused', () => {
  assert.throws(() => writeZip([{ name: '../evil', data: Buffer.alloc(1), mode: 0o644 }]), /name/)
  assert.throws(() => writeZip([{ name: '/abs', data: Buffer.alloc(1), mode: 0o644 }]), /name/)
})

test('package args: rid and an optional --version', async () => {
  const { parsePackageArgs } = await import('./lib.mjs')
  assert.deepEqual(parsePackageArgs(['osx-arm64']), { rid: 'osx-arm64', version: undefined })
  assert.deepEqual(parsePackageArgs(['win-x64', '--version', '0.1.0-beta.1']), { rid: 'win-x64', version: '0.1.0-beta.1' })
  assert.throws(() => parsePackageArgs(['linux-x64', '--version']), /--version needs a value/)
  assert.throws(() => parsePackageArgs(['linux-x64', '--version', '--other']), /--version needs a value/)
})

test('desktop package: one file for every OS, the manifest stamped with the version', () => {
  assert.equal(desktopPackageName('0.2.0-beta.1'), 'Altrobe-0.2.0-beta.1.mcpb')
  assert.deepEqual(stampManifest({ name: 'altrobe', version: '0.0.0' }, '0.2.0-beta.1'), { name: 'altrobe', version: '0.2.0-beta.1' })
})

test('publishPlan syncs the build, gzips files CloudFront will not compress, then switches current.json last', () => {
  const plan = publishPlan({
    bundleDir: '/b', product: 'wow_classic_beta', bucket: 'assets-bucket', distribution: 'E123',
    current: { build: '1.60.1.70245', path: '1.60.1.70245/' },
    files: [{ path: 'catalog.json', size: 4_200_000 }, { path: 'v2/anims/1/2.glb', size: 12_000_000 }, { path: 'items/9.json', size: 10_000_000 }],
  })
  assert.deepEqual(plan.map((s) => s.kind), ['sync', 'gzip', 'current', 'invalidate'])
  const [sync, gzip, current, invalidate] = plan
  assert.deepEqual(sync.args, ['s3', 'sync', join('/b', 'wow_classic_beta', '1.60.1.70245'), 's3://assets-bucket/wow_classic_beta/1.60.1.70245', '--exclude', 'v2/anims/1/2.glb'])
  assert.equal(gzip.file, join('/b', 'wow_classic_beta', '1.60.1.70245', 'v2/anims/1/2.glb'))
  assert.deepEqual(gzip.args, ['s3', 'cp', '{gz}', 's3://assets-bucket/wow_classic_beta/1.60.1.70245/v2/anims/1/2.glb', '--content-encoding', 'gzip', '--content-type', 'model/gltf-binary'])
  assert.deepEqual(current.args, ['s3', 'cp', join('/b', 'wow_classic_beta', 'current.json'), 's3://assets-bucket/wow_classic_beta/current.json', '--content-type', 'application/json'])
  assert.deepEqual(invalidate.args, ['cloudfront', 'create-invalidation', '--distribution-id', 'E123', '--paths', '/wow_classic_beta/current.json'])
})

test('publishPlan passes a profile to every command and refuses a current.json that names another folder', () => {
  const opts = { bundleDir: '/b', product: 'p', bucket: 'x', distribution: 'E1', profile: 'example', current: { build: '1', path: '1/' }, files: [] }
  assert.ok(publishPlan(opts).every((s) => s.args.slice(-2).join(' ') === '--profile example'))
  assert.throws(() => publishPlan({ ...opts, current: { build: '1', path: '../x/' } }), /current.json/)
})
