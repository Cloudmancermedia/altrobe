import { describe, expect, test } from 'vitest'
import type { CharactersResponse, CustomizationOption, ItemSearchResult } from './api/types'
import { createCommands, transitions } from './commands'
import { decodeLook, emptyLook } from './look/look'
import { createStore, initialState } from './store'

const characters: CharactersResponse = {
  build: 'b1',
  races: [
    { race: 2, name: 'Orc', classes: [], sexes: [{ sex: 0, hd: true, sd: true }, { sex: 1, hd: true, sd: false }] },
    { race: 5, name: 'Undead', classes: [], sexes: [{ sex: 1, hd: true, sd: true }] },
  ],
}

describe('transitions', () => {
  const bare = emptyLook(2, 0)

  test('equip and unequip by slot name; unequip also clears a hidden flag', () => {
    const a = transitions.equipItem(bare, 'head', 12).look!
    expect(a.items).toEqual({ head: 12 })
    const hidden = transitions.setVisibility(a, 'head', false).look!
    expect(hidden.hide).toEqual(['head'])
    const b = transitions.unequip(hidden, 'head').look!
    expect(b.items).toEqual({})
    expect(b.hide).toEqual([])
    expect(bare.items, 'input is not mutated').toEqual({})
  })

  test('equip_items puts several items on in one change and keeps other slots', () => {
    const start = transitions.equipItem(bare, 'feet', 7).look!
    const r = transitions.equipItems(start, [{ slot: 'chest', itemId: 1 }, { slot: 'mainhand', itemId: 2 }]).look!
    expect(r.items).toEqual({ feet: 7, chest: 1, mainhand: 2 })
    expect(transitions.equipItems(bare, [{ slot: 'ring', itemId: 1 }]).error).toMatch(/unknown slot "ring"/)
    expect(transitions.equipItems(bare, []).error).toMatch(/no items/)
  })

  test('equip rejects unknown slots and bad IDs', () => {
    expect(transitions.equipItem(bare, 'ring', 1).error).toMatch(/unknown slot/)
    expect(transitions.equipItem(bare, 'head', -1).error).toMatch(/not an item ID/)
    expect(transitions.equipItem(bare, 'head', 1.5).error).toMatch(/not an item ID/)
  })

  test('set_visibility toggles without duplicates', () => {
    let l = transitions.setVisibility(bare, 'back', false).look!
    l = transitions.setVisibility(l, 'back', false).look!
    expect(l.hide).toEqual(['back'])
    expect(transitions.setVisibility(l, 'back', true).look!.hide).toEqual([])
  })

  test('set_character keeps items, and clears customizations only when the body changes', () => {
    const custom = { ...transitions.equipItem(bare, 'chest', 5).look!, custom: { 20: 390 } }
    expect(transitions.setCharacter(custom, 2, 0, 'hd', characters).look!.custom).toEqual({ 20: 390 })
    const undead = transitions.setCharacter(custom, 5, 1, 'hd', characters).look!
    expect([undead.race, undead.sex, undead.models]).toEqual([5, 1, 'hd'])
    expect(undead.items).toEqual({ chest: 5 })
    expect(undead.custom).toEqual({})
    expect(transitions.setCharacter(custom, 2, 0, 'sd', characters).look!.custom).toEqual({})
  })

  test('set_character refuses characters and model sets the server does not have', () => {
    expect(transitions.setCharacter(bare, 7, 0, 'hd', characters).error).toMatch(/no character data/)
    expect(transitions.setCharacter(bare, 2, 1, 'sd', characters).error).toBe('Orc female has no SD model')
    expect(transitions.setCharacter(bare, 2, 2).error).toMatch(/not a character/)
    // Without the list it only checks the shape.
    expect(transitions.setCharacter(bare, 7, 0).look!.race).toBe(7)
  })

  test('set_customization stores choice IDs by option ID', () => {
    expect(transitions.setCustomization(bare, 20, 390).look!.custom).toEqual({ 20: 390 })
    expect(transitions.setCustomization(bare, 0, 390).error).toBeDefined()
  })

  const options: CustomizationOption[] = [
    { optionId: 19, name: 'Skin Color', defaultChoiceId: 353, choices: [{ choiceId: 353, name: '' }, { choiceId: 354, name: '' }] },
    { optionId: 20, name: 'Face', defaultChoiceId: 384, choices: [{ choiceId: 384, name: '' }, { choiceId: 390, name: '' }] },
  ]

  test('set_customization checks the choice against the options when they are known', () => {
    expect(transitions.setCustomization(bare, 20, 390, options).look!.custom).toEqual({ 20: 390 })
    expect(transitions.setCustomization(bare, 20, 999, options).error).toMatch(/Face has no choice 999/)
    expect(transitions.setCustomization(bare, 7, 1, options).error).toMatch(/no customization option 7/)
  })

  test('randomize picks a choice for every option; reset clears them', () => {
    const r = transitions.randomizeCustomization(bare, options, () => 0.99).look!
    expect(r.custom).toEqual({ 19: 354, 20: 390 })
    expect(transitions.randomizeCustomization(bare, []).error).toMatch(/no customization options/)
    expect(transitions.resetCustomization(r).look!.custom).toEqual({})
  })

  test('compare sets up to three extra characters, in order', () => {
    const r = transitions.compare(bare, [{ race: 5, sex: 1, models: 'sd' }, { race: 2, sex: 1 }], characters).look!
    expect(r.compare).toEqual([{ race: 5, sex: 1, models: 'sd' }, { race: 2, sex: 1, models: 'hd' }])
    const four = Array.from({ length: 4 }, () => ({ race: 2, sex: 0 }))
    expect(transitions.compare(bare, four).error).toMatch(/at most 4/)
    expect(transitions.compare(bare, [{ race: 2, sex: 1, models: 'sd' }], characters).error).toMatch(/no SD model/)
    expect(transitions.compare(r, []).look!.compare).toEqual([])
  })

  test('set_view accepts only the camera presets', () => {
    expect(transitions.setView(bare, 'head').look!.cam).toEqual({ view: 'head' })
    expect(transitions.setView(bare, 'top').error).toBeDefined()
  })
})

describe('createCommands', () => {
  function setup() {
    const store = createStore(initialState())
    store.set({ characters, status: { version: '0', installs: [], cacheFolder: '', active: { path: '/x', product: 'p', build: 'b2' } } })
    const searched: unknown[] = []
    const results: ItemSearchResult[] = [{ itemId: 12640, name: 'Lionheart Helm', slot: 'head', quality: 4, iconFileDataId: 1 }]
    const cmd = createCommands({ store, appUrl: () => 'http://app.test/?q=1', searchItems: async (q) => { searched.push(q); return results } })
    return { store, cmd, searched }
  }

  test('commands update the store look and stamp the current build', () => {
    const { store, cmd } = setup()
    cmd.equip_item('head', 12640)
    cmd.set_character(5, 1, 'sd')
    cmd.set_visibility('head', false)
    const look = store.get().look
    expect(look.build).toBe('b2')
    expect([look.race, look.sex, look.models]).toEqual([5, 1, 'sd'])
    expect(look.items).toEqual({ head: 12640 })
    expect(look.hide).toEqual(['head'])
  })

  test('a rejected command leaves the look alone and raises a notice', () => {
    const { store, cmd } = setup()
    const before = store.get().look
    expect(cmd.equip_item('ring', 1).error).toMatch(/unknown slot/)
    expect(store.get().look).toBe(before)
    expect(store.get().notices.commands).toEqual(['unknown slot "ring"'])
    cmd.unequip('head')
    expect(store.get().notices.commands).toEqual([])
  })

  test('get_look is canonical and share_link round-trips', () => {
    const { store, cmd } = setup()
    store.set({ defaults: { 20: 384 } })
    cmd.equip_item('chest', 220794)
    cmd.set_customization(20, 384) // the default: left out of the canonical form
    cmd.compare([{ race: 5, sex: 1, models: 'hd' }])
    expect(cmd.get_look()).toEqual({ build: 'b2', compare: [{ models: 'hd', race: 5, sex: 1 }], game: 'forever', items: { chest: 220794 }, models: 'hd', race: 2, sex: 0, v: 1 })
    const link = cmd.share_link()
    expect(link.startsWith('http://app.test/#look=')).toBe(true)
    const back = decodeLook(link.split('#look=')[1]).look!
    expect(back.items).toEqual({ chest: 220794 })
    expect(back.compare).toEqual([{ race: 5, sex: 1, models: 'hd' }])
  })

  test('open_look replaces the look and reports what it dropped', () => {
    const { store, cmd } = setup()
    const r = cmd.open_look({ v: 1, game: 'forever', build: 'b1', race: 5, sex: 1, items: { head: 1, ring: 2 } })
    expect(r.look!.items).toEqual({ head: 1 })
    expect(store.get().look.race).toBe(5)
    expect(store.get().notices.link).toEqual(['dropped item in unknown slot "ring"'])
    expect(cmd.open_look('nope').look).toBeNull()
    expect(store.get().look.race, 'a bad look does not replace the current one').toBe(5)
  })

  test('search_items passes the query to the server', async () => {
    const { cmd, searched } = setup()
    const r = await cmd.search_items({ q: 'helm', slot: 'head' })
    expect(r[0].itemId).toBe(12640)
    expect(searched).toEqual([{ q: 'helm', slot: 'head' }])
  })
})
