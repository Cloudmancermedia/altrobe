// The browser's item and set search over a baked bundle. The cases are ported from the server's
// ItemCatalogTests and SetCatalogTests, so the hosted site and the local server answer alike.
import { describe, expect, test } from 'vitest'
import { searchItemsIn, searchSetsIn, toSetResult, type BundleItem, type BundleSet } from './search'

const item = (itemId: number, name: string, slot: string, quality: number, extra: Partial<BundleItem> = {}): BundleItem =>
  ({ itemId, name, slot, inventoryType: 0, quality, iconFileDataId: 0, internal: false, unnamed: false, ...extra })

// ItemCatalogTests.Catalog() as the server orders it (by name): items 1-4 have a visual.
const catalog: BundleItem[] = [
  item(4, 'Frostweave Robe', 'chest', 2),
  item(1, 'Robe of the Archmage', 'chest', 4),
  item(3, 'Robe of Winter Night', 'chest', 3),
  item(2, 'Thunderfury, Blessed Blade of the Windseeker', 'mainhand', 5),
]
const ids = (r: { items: BundleItem[] }) => r.items.map((i) => i.itemId)

describe('item search', () => {
  test('lists every item in catalog order', () => {
    const all = searchItemsIn(catalog, { limit: 100 })
    expect(all.total).toBe(4)
    expect(ids(all)).toEqual([4, 1, 3, 2])
  })

  test('is a case-insensitive substring match', () => {
    const robes = searchItemsIn(catalog, { q: 'robe' })
    expect(robes.total).toBe(3)
    expect(robes.items.map((i) => i.name)).toEqual(['Frostweave Robe', 'Robe of the Archmage', 'Robe of Winter Night'])
  })

  test('filters by slot and quality, each a list', () => {
    expect(ids(searchItemsIn(catalog, { q: 'robe', slots: ['chest'] }))).toEqual([4, 1, 3])
    expect(ids(searchItemsIn(catalog, { slots: ['mainhand'] }))).toEqual([2])
    expect(ids(searchItemsIn(catalog, { slots: ['mainhand', 'head'] }))).toEqual([2])
    expect(ids(searchItemsIn(catalog, { slots: ['chest'], qualities: [4] }))).toEqual([1])
  })

  test('unnamed items stay out unless asked for; an exact ID still finds one', () => {
    const withUnnamed = [...catalog, item(230123, 'Unnamed chest (item 230123)', 'chest', -1, { unnamed: true })]
    expect(ids(searchItemsIn(withUnnamed, { limit: 100 }))).toEqual([4, 1, 3, 2])
    expect(ids(searchItemsIn(withUnnamed, { limit: 100, includeUnnamed: true }))).toEqual([4, 1, 3, 2, 230123])
    expect(ids(searchItemsIn(withUnnamed, { q: '230123' }))).toEqual([230123])
  })

  test('an item ID also matches', () => {
    expect(ids(searchItemsIn(catalog, { q: '2' }))).toEqual([2])
  })

  test('pages with offset and limit', () => {
    const page1 = searchItemsIn(catalog, { q: 'robe', limit: 2 })
    const page2 = searchItemsIn(catalog, { q: 'robe', limit: 2, offset: 2 })
    const past = searchItemsIn(catalog, { q: 'robe', limit: 2, offset: 10 })
    expect(ids(page1)).toEqual([4, 1])
    expect(ids(page2)).toEqual([3])
    expect(past.items).toEqual([])
    for (const p of [page1, page2, past]) expect(p.total).toBe(3)
  })

  test('clamps bad paging values (limit 1 to 200, offset at least 0)', () => {
    expect(searchItemsIn(catalog, { limit: 0 }).items).toHaveLength(1)
    expect(searchItemsIn(catalog, { limit: 100000, offset: -5 }).items).toHaveLength(4)
  })

  test('dev and NPC items are opt-in, except by exact ID', () => {
    const names = ['Arcanite Reaper', 'Testament of Hope', "Zealot's Robe", '(DNT) Moonglaive', 'JEFF TEST SWORD']
    const items = names.map((n, i) => item(i + 1, n, 'mainhand', 2, { internal: i >= 3 }))
    expect(searchItemsIn(items, { limit: 100, includeInternal: false }).items.map((i) => i.name)).toEqual(['Arcanite Reaper', 'Testament of Hope', "Zealot's Robe"])
    expect(searchItemsIn(items, { q: 'moonglaive', includeInternal: false }).items).toEqual([])
    // The search panel, like /api/v1/items/search, includes them (catalog order keeps them last).
    expect(searchItemsIn(items, { limit: 100 }).items).toHaveLength(5)
    expect(searchItemsIn(items, { q: '4', includeInternal: false }).items.map((i) => i.name)).toEqual(['(DNT) Moonglaive'])
  })
})

// SetCatalogTests.Catalogs() as the server orders it: named sets by name, dev sets last.
const piece = (slot: string, itemId: number, name: string, extra: Partial<BundleItem> = {}) => ({ slot, item: item(itemId, name, slot, 4, extra) })
const set = (setId: number, name: string, pieces: BundleSet['pieces'], extra: Partial<BundleSet> = {}): BundleSet =>
  ({ setId, name, pieces, skipped: [], internal: false, unnamed: false, quality: 4, classMask: -1, ...extra })
const sets: BundleSet[] = [
  set(100, 'Battlegear of Might', [piece('head', 1, 'Lionheart Helm'), piece('chest', 2, 'Breastplate of Wrath'), piece('mainhand', 3, 'Sword of Might'), piece('offhand', 4, 'Mace of Might')]),
  set(218, 'Battlegear of Wrath', [piece('chest', 30, 'Battlegear of Wrath: chest', { unnamed: true, quality: -1 }), piece('legs', 31, 'Battlegear of Wrath: legs', { unnamed: true, quality: -1 })], { unnamed: true }),
  set(102, 'Test Set [PH]', [piece('chest', 2, 'Breastplate of Wrath')], { internal: true }),
]
const setIds = (r: { sets: BundleSet[] }) => r.sets.map((s) => s.setId)

describe('set search', () => {
  test('dev sets sort last and are opt-in; an exact set ID always finds one', () => {
    expect(setIds(searchSetsIn(sets, { includeInternal: false }))).toEqual([100, 218])
    expect(setIds(searchSetsIn(sets, {}))).toEqual([100, 218, 102])
    expect(setIds(searchSetsIn(sets, { q: '102', includeInternal: false }))).toEqual([102])
  })

  test('matches set names and piece names', () => {
    expect(setIds(searchSetsIn(sets, { q: 'battlegear', includeInternal: false }))).toEqual([100, 218])
    expect(setIds(searchSetsIn(sets, { q: 'wrath', includeInternal: false }))).toEqual([100, 218])
    expect(setIds(searchSetsIn(sets, { q: 'wrath' }))).toEqual([100, 218, 102])
    expect(setIds(searchSetsIn(sets, { q: '100' }))).toEqual([100])
    expect(setIds(searchSetsIn(sets, { q: 'battlegear of wrath' }))).toContain(218)
  })

  test('pages with the total of every match (limit 1 to 50)', () => {
    const one = searchSetsIn(sets, { limit: 1 })
    expect(one.total).toBe(3)
    expect(one.sets).toHaveLength(1)
    expect(searchSetsIn(sets, { limit: 1000 }).sets).toHaveLength(3)
  })

  test('a set is flattened the way the API sends it (Api.SetJson)', () => {
    expect(toSetResult(sets[1])).toEqual({
      setId: 218, name: 'Battlegear of Wrath', internal: false, unnamed: true, quality: 4,
      pieces: [
        { slot: 'chest', itemId: 30, name: 'Battlegear of Wrath: chest', quality: -1, iconFileDataId: 0, unnamed: true },
        { slot: 'legs', itemId: 31, name: 'Battlegear of Wrath: legs', quality: -1, iconFileDataId: 0, unnamed: true },
      ],
      skipped: [],
    })
  })
})
