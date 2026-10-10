// The static backend over an in-memory bundle laid out as BundleBaker writes it.
import { describe, expect, test } from 'vitest'
import { ApiError } from './errors'
import { createStaticBackend } from './static'
import type { BundleItem, BundleSet } from './search'

const item = (itemId: number, name: string, extra: Partial<BundleItem> = {}): BundleItem =>
  ({ itemId, name, slot: 'chest', inventoryType: 5, quality: 4, iconFileDataId: 9000 + itemId, internal: false, unnamed: false, ...extra })
const set = (setId: number, name: string, pieces: BundleItem[]): BundleSet =>
  ({ setId, name, pieces: pieces.map((i) => ({ slot: i.slot, item: i })), skipped: [], internal: false, unnamed: false, quality: 4, classMask: -1 })

const might = set(100, 'Battlegear of Might', [item(1, 'Breastplate of Might')])
const files: Record<string, unknown> = {
  '/bundle/wow_classic_beta/current.json': { product: 'wow_classic_beta', build: '1.60.1.70245', buildName: 'WOW-70245patch1.60.1_ForeverBeta', path: '1.60.1.70245/', assetVersion: 2, assetPath: '1.60.1.70245/v2/' },
  '/bundle/wow_classic_beta/1.60.1.70245/characters.json': {
    build: '1.60.1.70245',
    races: [{ race: 2, name: 'Orc', classes: [{ classId: 1, name: 'Warrior' }], sexes: [{ sex: 0, hd: true, sd: false }], faction: 'horde' }],
    looks: { '2-0-hd': { build: '1.60.1.70245', race: 2, sex: 0, modelFileDataId: 7000 } },
  },
  '/bundle/wow_classic_beta/1.60.1.70245/catalog.json': { build: '1.60.1.70245', items: [item(1, 'Breastplate of Might'), item(30, 'Battlegear of Wrath: chest', { unnamed: true, quality: -1 }), item(2, 'Thunderfury', { slot: 'mainhand', quality: 5 })] },
  '/bundle/wow_classic_beta/1.60.1.70245/sets.json': { build: '1.60.1.70245', sets: [might] },
  '/bundle/wow_classic_beta/1.60.1.70245/notable.json': { 2: { all: [{ group: 'Tier 1', sets: [might] }], 1: [{ group: 'Tier 1', sets: [might] }] } },
  '/bundle/wow_classic_beta/1.60.1.70245/set-pieces.json': [1],
  '/bundle/wow_classic_beta/1.60.1.70245/titles.json': [{ titleId: 1017, male: '%s of the Magram', female: '%s of the Magram' }],
  '/bundle/wow_classic_beta/1.60.1.70245/items/1.json': { itemId: 1, variants: { '2-0-hd': { itemId: 1, name: 'Breastplate of Might', race: 2 } } },
  // The resolver leaves the made-up name off an unnamed piece; the API puts the catalog's name on.
  '/bundle/wow_classic_beta/1.60.1.70245/items/30.json': { itemId: 30, variants: { '2-0-hd': { itemId: 30, name: '', race: 2 } } },
}
const asked: string[] = []
const fakeFetch = (async (url: string) => {
  asked.push(url)
  return url in files
    ? new Response(JSON.stringify(files[url]), { status: 200 })
    : new Response('missing', { status: 404 })
}) as typeof fetch

const backend = () => createStaticBackend({ baseUrl: '/bundle', product: 'wow_classic_beta', fetch: fakeFetch })

describe('static backend', () => {
  test('reports the published build as the active one, with no installs to pick', async () => {
    const s = await backend().getStatus()
    expect(s.active).toEqual({ path: '', product: 'wow_classic_beta', build: '1.60.1.70245', assetVersion: 2 })
    expect(s.installs).toEqual([])
  })

  test('has none of the local-only features', () => {
    expect(backend().features).toEqual({ installs: false, session: false, assistant: false })
  })

  test('serves characters and base looks from characters.json', async () => {
    const b = backend()
    expect((await b.getCharacters()).races.map((r) => r.name)).toEqual(['Orc'])
    expect((await b.getBaseLook(2, 0, 'hd')).modelFileDataId).toBe(7000)
    await expect(b.getBaseLook(2, 1, 'hd')).rejects.toMatchObject({ status: 404, code: 'character_not_found' })
  })

  test('resolves an item for one body, with the catalog name on unnamed pieces, else null', async () => {
    const b = backend()
    expect((await b.getResolvedItem(1, 2, 0, 'hd'))?.name).toBe('Breastplate of Might')
    expect((await b.getResolvedItem(30, 2, 0, 'hd'))?.name).toBe('Battlegear of Wrath: chest')
    expect(await b.getResolvedItem(1, 5, 0, 'hd')).toBeNull() // no variant for that body
    expect(await b.getResolvedItem(999, 2, 0, 'hd')).toBeNull() // not in the bundle (404)
  })

  test('searches the catalog and sets in the browser, with comma lists as the API takes them', async () => {
    const b = backend()
    expect((await b.searchItems({ q: 'might' })).map((i) => i.itemId)).toEqual([1])
    expect((await b.searchItems({ slot: 'mainhand,head' })).map((i) => i.itemId)).toEqual([2])
    expect((await b.searchItems({ quality: 4 })).map((i) => i.itemId)).toEqual([1])
    const sets = await b.searchSets({ q: 'battlegear' })
    expect(sets[0].pieces[0]).toEqual({ slot: 'chest', itemId: 1, name: 'Breastplate of Might', quality: 4, iconFileDataId: 9001, unnamed: false })
  })

  test('serves notable sets per race and class, flattened, and refuses an unknown race', async () => {
    const b = backend()
    expect((await b.getNotableSets(2)).map((g) => [g.group, g.sets[0].setId])).toEqual([['Tier 1', 100]])
    expect((await b.getNotableSets(2, 1))[0].sets[0].pieces[0].itemId).toBe(1)
    expect(await b.getNotableSets(2, 8)).toEqual([]) // a class the race can't be
    await expect(b.getNotableSets(99)).rejects.toBeInstanceOf(ApiError)
    expect(await b.getSetPieces()).toEqual([1])
  })

  test('serves titles and names from the bundle; a bundle baked before they existed has none', async () => {
    const b = backend()
    expect(await b.getTitles()).toEqual([{ titleId: 1017, male: '%s of the Magram', female: '%s of the Magram' }])
    expect(await b.getNames()).toEqual({}) // no names.json in this bundle
  })

  test('asset URLs point into the build folder for the converter version', async () => {
    const b = backend()
    await b.getStatus()
    expect(b.modelUrl('1.60.1.70245', 42, 'glb')).toBe('/bundle/wow_classic_beta/1.60.1.70245/v2/models/42.glb')
    expect(b.textureUrl('1.60.1.70245', 7)).toBe('/bundle/wow_classic_beta/1.60.1.70245/v2/textures/7.png')
    expect(b.animUrl('1.60.1.70245', 7000, 5)).toBe('/bundle/wow_classic_beta/1.60.1.70245/v2/anims/7000/5.glb')
  })

  test('reads each file once', async () => {
    asked.length = 0
    const b = backend()
    await b.searchItems({ q: 'a' })
    await b.searchItems({ q: 'b' })
    expect(asked.filter((u) => u.endsWith('catalog.json'))).toHaveLength(1)
  })

  test('selecting an install is refused: the site has one build', async () => {
    await expect(backend().selectInstall({ product: 'wow' })).rejects.toBeInstanceOf(ApiError)
  })
})
