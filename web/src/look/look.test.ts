// Ported from the spike's tools/viewer-test/look.test.mjs, plus the "compare" addition.
import { describe, expect, test } from 'vitest'
import type { ResolvedItem } from '../api/types'
import {
  canonicalJSON, checkAgainstData, decodeLook, defaultsFrom, dressStateFor, encodeLook, lookFromFragment,
  normalizeLook, shareUrl, type Look,
} from './look'

const outfitA = {
  v: 1, game: 'forever', build: '1.60.1.70009', models: 'hd', race: 2, sex: 0,
  items: { head: 12640, shoulder: 231534, chest: 220794, hands: 220806, feet: 16734, mainhand: 19019, back: 15138 },
}
const norm = (x: unknown) => normalizeLook(x).look as Look
const item = (itemId: number, inventoryType: number) => ({ itemId, inventoryType }) as ResolvedItem

test('round-trip: decode(encode(look)) gives the same look', () => {
  const look = norm({ ...outfitA, hide: ['back'], cam: { view: 'side' }, custom: { 20: 385 } })
  const back = decodeLook(encodeLook(look))
  expect(back.notices).toEqual([])
  expect(back.look).toEqual(look)
})

test('encoding is URL-safe and survives a share URL', () => {
  const look = norm(outfitA)
  const url = shareUrl('http://localhost:5199/?x=1#x', look)
  expect(url).toMatch(/^http:\/\/localhost:5199\/#look=[A-Za-z0-9_-]+$/)
  expect(lookFromFragment(new URL(url).hash)?.look).toEqual(look)
  expect(lookFromFragment('#other=1')).toBeNull()
  // A mangled fragment gives a notice, not a throw.
  const bad = lookFromFragment('#look=%%%not-base64')!
  expect(bad.look).toBeNull()
  expect(bad.notices[0]).toMatch(/could not read the look link/)
})

test('canonical form: key order, duplicates and defaults do not change the output', () => {
  const a = norm(outfitA)
  const shuffled = norm({
    cam: { view: 'front' }, hide: [], custom: {}, sex: 0, race: 2, models: 'hd', build: '1.60.1.70009', game: 'forever', v: 1,
    items: Object.fromEntries(Object.entries(outfitA.items).reverse()),
  })
  expect(canonicalJSON(a)).toBe(canonicalJSON(shuffled))
  expect(encodeLook(a)).toBe(encodeLook(shuffled))
  // Stable across calls, and default-valued optional fields are omitted.
  expect(canonicalJSON(norm(JSON.parse(canonicalJSON(a))))).toBe(canonicalJSON(a))
  expect(Object.keys(JSON.parse(canonicalJSON(a)))).toEqual(['build', 'game', 'items', 'models', 'race', 'sex', 'v'])
  // hide is sorted and deduplicated.
  const h = norm({ ...outfitA, hide: ['head', 'back', 'head'] })
  expect(JSON.parse(canonicalJSON(h)).hide).toEqual(['back', 'head'])
})

test('canonical form omits customizations equal to the defaults', () => {
  const defaults = defaultsFrom({ choices: [{ optionId: 19, choiceId: 353 }, { optionId: 20, choiceId: 384 }, { optionId: 21, choiceId: null }] })
  expect(defaults).toEqual({ 19: 353, 20: 384 })
  const withDefault = norm({ ...outfitA, custom: { 19: 353, 20: 390 } })
  expect(JSON.parse(canonicalJSON(withDefault, { defaults })).custom).toEqual({ 20: 390 })
  const allDefault = norm({ ...outfitA, custom: { 19: 353 } })
  expect(JSON.parse(canonicalJSON(allDefault, { defaults })).custom).toBeUndefined()
})

test('unknown fields are ignored and not carried into the canonical form', () => {
  const { look, notices } = normalizeLook({ ...outfitA, pet: 'wolf', cam: { view: 'head', fov: 40 } })
  expect(notices).toEqual([])
  const json = canonicalJSON(look!)
  expect(json).not.toMatch(/pet|fov/)
  expect(JSON.parse(json).cam).toEqual({ view: 'head' })
})

test('version handling: missing v, newer v, and invalid v', () => {
  const { v: _v, ...noV } = outfitA
  const missing = normalizeLook(noV)
  expect(missing.look?.v).toBe(1)
  expect(missing.notices.join()).toMatch(/no schema version; read as v1/)

  const newer = normalizeLook({ ...outfitA, v: 2, lighting: 'dusk' })
  expect(newer.look?.v).toBe(1)
  expect(newer.notices.join()).toMatch(/v2, this viewer reads v1/)
  expect(newer.look?.items.head).toBe(12640)

  for (const bad of [0, -1, 1.5, '1']) {
    const r = normalizeLook({ ...outfitA, v: bad })
    expect(r.look, `v=${JSON.stringify(bad)}`).toBeNull()
    expect(r.notices[0]).toMatch(/unsupported look version/)
  }
})

test('unrenderable looks fail with a notice, never a throw', () => {
  for (const input of [null, 42, [], { ...outfitA, game: 'retail' }, { ...outfitA, race: 'orc' }, { ...outfitA, sex: 2 }]) {
    const r = normalizeLook(input)
    expect(r.look).toBeNull()
    expect(r.notices).toHaveLength(1)
  }
  for (const text of ['', '!!!', 'bm90IGpzb24', encodeLook({ v: 1 } as Look).slice(0, 5)]) {
    const r = decodeLook(text)
    expect(r.look, text).toBeNull()
    expect(r.notices.length).toBeGreaterThan(0)
  }
})

test('invalid item IDs, slots, choices and views are dropped with notices', () => {
  const { look, notices } = normalizeLook({
    ...outfitA,
    items: { head: 12640, chest: -5, hands: 'gloves', ring: 1234, feet: 1.5 },
    custom: { 20: 390, abc: 1, 21: 0 },
    hide: ['back', 'tail'],
    cam: { view: 'top' },
    models: 'ultra',
  })
  expect(look!.items).toEqual({ head: 12640 })
  expect(look!.custom).toEqual({ 20: 390 })
  expect(look!.hide).toEqual(['back'])
  expect(look!.cam.view).toBe('front')
  expect(look!.models).toBe('hd')
  expect(notices, notices.join('\n')).toHaveLength(9)
})

test('a look for a character the server has no data for gets a notice', () => {
  const characters = [{ race: 2, sex: 0 }]
  expect(normalizeLook(outfitA, { characters }).notices).toEqual([])
  expect(normalizeLook({ ...outfitA, race: 7 }, { characters }).notices).toEqual(['no character data for race 7 sex 0'])
})

test('checkAgainstData drops items without data or in the wrong slot, and flags build and choices', () => {
  const look = norm({ ...outfitA, items: { head: 12640, chest: 999999, feet: 19019, offhand: 19019 }, custom: { 19: 353, 20: 390, 5: 1 } })
  const resolvedById = new Map([[12640, item(12640, 1)], [19019, item(19019, 13)]])
  const r = checkAgainstData(look, { build: '1.60.2.1', defaults: { 19: 353, 20: 384 }, resolvedById })
  expect(r.look.items).toEqual({ head: 12640, offhand: 19019 }) // one-hander may go in the off hand
  expect(r.look.custom).toEqual({ 20: 390 })
  const text = r.notices.join('\n')
  expect(text).toMatch(/build 1\.60\.1\.70009; showing it with data from build 1\.60\.2\.1/)
  expect(text).toMatch(/dropped chest item 999999: no item data/)
  expect(text).toMatch(/dropped feet item 19019: inventory type 13/)
  expect(text).toMatch(/dropped customization option 5/)
  expect(text).toMatch(/20=390 kept but not drawn/)
  expect(look.items.chest, 'input look is not mutated').toBe(999999)
  expect(checkAgainstData(norm(outfitA), { build: '1.60.1.70009' }).notices).toEqual([])
})

test('dressStateFor maps slot names to slot IDs, doubles shoulders and skips hidden slots', () => {
  const state = dressStateFor({ ...norm(outfitA), hide: ['back'] })
  expect(state.character).toBe('2-0-hd')
  expect(state.items).toEqual({ 1: 12640, 3: 231534, 30: 231534, 5: 220794, 10: 220806, 8: 16734, 16: 19019 })
})

describe('compare (side by side) addition', () => {
  test('round-trips in order and is left out when empty', () => {
    const look = norm({ ...outfitA, compare: [{ race: 5, sex: 1, models: 'sd' }, { race: 1, sex: 0, models: 'hd' }] })
    expect(look.compare).toEqual([{ race: 5, sex: 1, models: 'sd' }, { race: 1, sex: 0, models: 'hd' }])
    expect(decodeLook(encodeLook(look)).look).toEqual(look)
    expect(JSON.parse(canonicalJSON(look)).compare).toEqual([{ models: 'sd', race: 5, sex: 1 }, { models: 'hd', race: 1, sex: 0 }])
    expect(JSON.parse(canonicalJSON(norm(outfitA))).compare).toBeUndefined()
  })

  test('drops bad entries and anything past three, with notices', () => {
    const r = normalizeLook({ ...outfitA, compare: [{ race: 5, sex: 1 }, { race: 'x', sex: 0 }, { race: 1, sex: 0, models: 'ultra' }, { race: 3, sex: 0 }, { race: 4, sex: 1 }] })
    expect(r.look!.compare).toEqual([{ race: 5, sex: 1, models: 'hd' }, { race: 1, sex: 0, models: 'hd' }, { race: 3, sex: 0, models: 'hd' }])
    expect(r.notices).toHaveLength(3)
    expect(normalizeLook({ ...outfitA, compare: 'no' }).notices).toEqual(['dropped "compare": not a list'])
  })

  test('a look without compare reads the same as before the addition', () => {
    // v1 looks written by the spike have no "compare"; their canonical JSON must not change.
    expect(canonicalJSON(norm(outfitA))).toBe(
      '{"build":"1.60.1.70009","game":"forever","items":{"back":15138,"chest":220794,"feet":16734,"hands":220806,"head":12640,"mainhand":19019,"shoulder":231534},"models":"hd","race":2,"sex":0,"v":1}')
  })
})
