import assert from 'node:assert/strict'
import { test } from 'node:test'
import { gameContentProblems, hasDotnetSdk, nodeIsNewEnough, prefixLines, serverPort } from './lib.mjs'

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
