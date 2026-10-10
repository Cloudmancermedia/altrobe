import { mkdirSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { prepareCache, sourceHash } from './cache'

let dir: string
beforeEach(() => { dir = mkdtempSync(join(tmpdir(), 'altrobe-e2e-cache-test-')) })
afterEach(() => rmSync(dir, { recursive: true, force: true }))

describe('sourceHash', () => {
  it('changes when a source file changes and ignores bin and obj', () => {
    const src = join(dir, 'src')
    mkdirSync(join(src, 'a', 'bin'), { recursive: true })
    mkdirSync(join(src, 'a', 'obj'), { recursive: true })
    writeFileSync(join(src, 'a', 'X.cs'), 'one')
    const first = sourceHash(src)
    writeFileSync(join(src, 'a', 'bin', 'X.dll'), 'built')
    writeFileSync(join(src, 'a', 'obj', 'X.json'), 'restored')
    expect(sourceHash(src)).toBe(first)
    writeFileSync(join(src, 'a', 'X.cs'), 'two')
    expect(sourceHash(src)).not.toBe(first)
  })
})

describe('prepareCache', () => {
  it('keeps the cache for the current sources and removes older ones', () => {
    mkdirSync(join(dir, 'aaa', 'wow_classic_beta'), { recursive: true })
    writeFileSync(join(dir, 'aaa', 'wow_classic_beta', 'model.glb'), 'x')
    mkdirSync(join(dir, 'old'))
    const cache = prepareCache(dir, 'aaa', false)
    expect(cache).toBe(join(dir, 'aaa'))
    expect(readdirSync(dir)).toEqual(['aaa'])
    expect(readdirSync(join(cache, 'wow_classic_beta'))).toEqual(['model.glb'])
  })

  it('starts empty when fresh is set', () => {
    mkdirSync(join(dir, 'aaa'))
    writeFileSync(join(dir, 'aaa', 'model.glb'), 'x')
    const cache = prepareCache(dir, 'aaa', true)
    expect(readdirSync(cache)).toEqual([])
  })

  it('creates the root when it does not exist', () => {
    const cache = prepareCache(join(dir, 'missing'), 'aaa', false)
    expect(readdirSync(cache)).toEqual([])
  })
})
