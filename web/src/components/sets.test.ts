import { describe, expect, test } from 'vitest'
import type { ItemSetResult } from '../api/types'
import { DEFAULT_SET_PREFS, readSetPrefs, setQuality, withoutNotable, writeSetPrefs } from './sets'

const set = (setId: number): ItemSetResult => ({ setId, name: `Set ${setId}`, internal: false, unnamed: false, pieces: [], skipped: [] })

function memoryStorage(): Storage {
  const m = new Map<string, string>()
  return {
    get length() { return m.size },
    clear: () => m.clear(),
    getItem: (k) => m.get(k) ?? null,
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => { m.delete(k) },
    setItem: (k, v) => { m.set(k, v) },
  }
}

describe('set panel preferences', () => {
  test('default to notable sets first and replacing the previous set', () => {
    expect(readSetPrefs(memoryStorage())).toEqual({ notableFirst: true, replace: true })
    expect(DEFAULT_SET_PREFS).toEqual({ notableFirst: true, replace: true })
  })

  test('are remembered', () => {
    const s = memoryStorage()
    writeSetPrefs(s, { notableFirst: false, replace: true })
    expect(readSetPrefs(s)).toEqual({ notableFirst: false, replace: true })
  })

  test('fall back to the defaults when storage is missing, throws or holds junk', () => {
    expect(readSetPrefs(null)).toEqual(DEFAULT_SET_PREFS)
    const broken = { ...memoryStorage(), getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') } }
    expect(readSetPrefs(broken)).toEqual(DEFAULT_SET_PREFS)
    expect(() => writeSetPrefs(broken, DEFAULT_SET_PREFS)).not.toThrow()
    const junk = memoryStorage()
    junk.setItem('altrobe.sets', '{not json')
    expect(readSetPrefs(junk)).toEqual(DEFAULT_SET_PREFS)
  })
})

describe('withoutNotable', () => {
  test('leaves the notable sets out of the full list below them', () => {
    const groups = [{ group: 'Tier 1', sets: [set(2)] }, { group: 'Tier 2', sets: [set(4)] }]
    expect(withoutNotable([set(1), set(2), set(3), set(4)], groups).map((s) => s.setId)).toEqual([1, 3])
  })
})

describe('setQuality', () => {
  const piece = (quality: number) => ({ slot: 'chest', itemId: 1, name: 'x', quality, iconFileDataId: 0 })
  test("uses the server's set quality, which covers unnamed tier sets", () => {
    expect(setQuality({ ...set(1), unnamed: true, quality: 4, pieces: [piece(-1), piece(-1)] })).toBe(4)
  })
  test('falls back to the best piece without one', () => {
    expect(setQuality({ ...set(1), pieces: [piece(2), piece(3)] })).toBe(3)
  })
})
