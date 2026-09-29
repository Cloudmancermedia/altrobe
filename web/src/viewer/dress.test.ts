// dress() over tiny hand-written fixtures (made-up IDs, not game data).
import { describe, expect, test } from 'vitest'
import type { ResolvedItem } from '../api/types'
import { dress, slotNameForInventoryType, slotsForInventoryType } from './dress'

const base = {
  geosets: [0, 401, 501, 702, 1301, 1501, 2001, 2701],
  sections: [
    { sectionType: 0, x: 0, y: 0, width: 256, height: 128 },
    { sectionType: 3, x: 256, y: 0, width: 256, height: 64 },
  ],
  sectionLayers: [
    { sectionType: 0, textureType: 1, blendMode: 15 },
    { sectionType: 3, textureType: 1, blendMode: 15 },
  ],
}

function item(itemId: number, inventoryType: number, extra: Partial<ResolvedItem> = {}): ResolvedItem {
  return { itemId, name: `item ${itemId}`, inventoryType, geosetGroup: [0, 0, 0, 0, 0, 0], helmHideGeosetGroups: [], models: [], bodyTextures: [], ...extra }
}
const byId = (...items: ResolvedItem[]) => new Map(items.map((i) => [i.itemId, i]))

describe('slots', () => {
  test('inventory types map to look slot names', () => {
    expect(slotNameForInventoryType(1)).toBe('head')
    expect(slotNameForInventoryType(20)).toBe('chest') // robe
    expect(slotNameForInventoryType(16)).toBe('back')
    expect(slotNameForInventoryType(13)).toBe('mainhand')
    expect(slotNameForInventoryType(24)).toBeNull() // ammo is not worn
    expect(slotsForInventoryType(13)).toEqual(['mainhand', 'offhand'])
    expect(slotsForInventoryType(17)).toEqual(['mainhand'])
  })
})

describe('geosets', () => {
  test('a robe sets sleeves and trousers from its geoset groups and clears the old variant', () => {
    const robe = item(1, 20, { geosetGroup: [1, 0, 1, 0, 0, 0] })
    const d = dress(base, { character: 'x', items: { 5: 1 } }, byId(robe))
    expect(d.geosets).toContain(802) // sleeves 8 * 100 + 1 + 1
    expect(d.geosets).toContain(1302) // trousers replace 1301
    expect(d.geosets).not.toContain(1301)
    expect(d.geosets).toContain(1001) // chest group 10, GeosetGroup[1] = 0
  })

  test('when the chest and the legs both set trousers, the chest wins', () => {
    const chest = item(1, 5, { geosetGroup: [0, 0, 2, 0, 0, 0] })
    const legs = item(2, 7, { geosetGroup: [0, 0, 0, 0, 0, 0] })
    const d = dress(base, { character: 'x', items: { 7: 2, 5: 1 } }, byId(chest, legs))
    expect(d.geosets.filter((g) => Math.floor(g / 100) === 13)).toEqual([1303])
    expect(d.geosetChanges.find((c) => c.group === 13)?.fromSlot).toBe(5)
  })

  test('feet use GeosetGroup[1] as is, and 2 when it is 0', () => {
    const boots = (id: number, g1: number) => item(id, 8, { geosetGroup: [0, g1, 0, 0, 0, 0] })
    expect(dress(base, { character: 'x', items: { 8: 1 } }, byId(boots(1, 0))).geosets).toContain(2002)
    expect(dress(base, { character: 'x', items: { 8: 1 } }, byId(boots(1, 3))).geosets).toContain(2003)
  })

  test('a helm hides the geoset groups it lists and leaves others alone', () => {
    const helm = item(1, 1, { helmHideGeosetGroups: [0, 7] })
    const d = dress(base, { character: 'x', items: { 1: 1 } }, byId(helm))
    expect(d.geosets).not.toContain(702) // ears (group 7) hidden
    expect(d.geosets).toContain(0) // xx00 is never cleared
    expect(d.geosets).toContain(401)
    expect(d.geosets).toContain(2701) // helm group 27 = 1 + GeosetGroup[0]
  })

  test('without a head item nothing is hidden', () => {
    expect(dress(base, { character: 'x', items: {} }, new Map()).geosets).toEqual(base.geosets)
  })
})

describe('texture layers', () => {
  test('body textures go into their section above the customization layers', () => {
    const chest = item(1, 5, { bodyTextures: [{ section: 3, textures: [{ fileDataId: 900 }] }, { section: 0, textures: [{ fileDataId: 901 }] }] })
    const gloves = item(2, 10, { bodyTextures: [{ section: 0, textures: [{ fileDataId: 902 }] }] })
    const d = dress(base, { character: 'x', items: { 5: 1, 10: 2 } }, byId(chest, gloves))
    expect(d.layers.map((l) => [l.fileDataId, l.layer])).toEqual([[901, 1300], [900, 1303], [902, 2000]])
    expect(d.layers[1].section).toEqual({ sectionType: 3, x: 256, y: 0, width: 256, height: 64 })
    expect(d.layers[0].blendMode).toBe(15)
  })

  test('a section the layout does not have is reported, not drawn', () => {
    const chest = item(1, 5, { bodyTextures: [{ section: 9, textures: [{ fileDataId: 900 }] }] })
    const d = dress(base, { character: 'x', items: { 5: 1 } }, byId(chest))
    expect(d.layers).toEqual([])
    expect(d.notes).toEqual(['item 1 section 9: no section rectangle'])
  })
})

describe('models', () => {
  test('a cloak without a model paints the cape geoset through texture type 2', () => {
    const cloak = item(1, 16, { geosetGroup: [1, 0, 0, 0, 0, 0], models: [{ slot: 0, models: [], textures: [{ fileDataId: 700 }] }] })
    const d = dress(base, { character: 'x', items: { 15: 1 } }, byId(cloak))
    expect(d.replaceableTextures).toEqual({ 2: 700 })
    expect(d.geosets).toContain(1502)
    expect(d.attachments).toEqual([])
  })

  test('shoulders pick the left and right models by position', () => {
    const pair = { slot: 0, models: [{ fileDataId: 10, position: 0 }, { fileDataId: 11, position: 1 }], textures: [{ fileDataId: 12 }] }
    const sh = item(1, 3, { models: [pair, { ...pair, slot: 1 }] })
    const d = dress(base, { character: 'x', items: { 3: 1, 30: 1 } }, byId(sh))
    expect(d.attachments.map((a) => [a.attachmentId, a.modelFileDataId])).toEqual([[6, 10], [5, 11]])
    expect(d.attachments[0].replaceableTextures).toEqual({ 2: 12 })
  })

  test('a weapon attaches to the right hand; items without data are noted and skipped', () => {
    const sword = item(1, 13, { models: [{ slot: 0, models: [{ fileDataId: 20 }], textures: [{ fileDataId: 21 }] }] })
    const d = dress(base, { character: 'x', items: { 16: 1, 1: 99 } }, byId(sword))
    expect(d.attachments).toEqual([{ slot: 16, itemId: 1, attachmentId: 1, modelFileDataId: 20, replaceableTextures: { 2: 21 } }])
    expect(d.notes).toEqual(['slot 1: no resolved data'])
  })
})
