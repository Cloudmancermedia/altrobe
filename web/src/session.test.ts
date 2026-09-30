import { describe, expect, test } from 'vitest'
import type { CharactersResponse } from './api/types'
import { createCommands } from './commands'
import { runRemoteCommand } from './session'
import { createStore, initialState } from './store'

const characters: CharactersResponse = {
  build: 'b1',
  races: [{ race: 2, name: 'Orc', classes: [], sexes: [{ sex: 0, hd: true, sd: true }] }, { race: 5, name: 'Undead', classes: [], sexes: [{ sex: 1, hd: true, sd: true }] }],
}

function setup() {
  const store = createStore(initialState())
  store.set({
    characters,
    itemInfo: { 19019: { name: 'Thunderfury' } },
    options: [{ optionId: 20, name: 'Face', defaultChoiceId: 384, choices: [{ choiceId: 384, name: 'Scarred' }, { choiceId: 390, name: '' }] }],
    defaults: { 20: 384 },
  })
  const cmd = createCommands({ store, searchItems: async () => [], appUrl: () => 'http://app.test/' })
  return { store, run: (command: string, args: Record<string, unknown> = {}) => runRemoteCommand(cmd, store, command, args) }
}

describe('runRemoteCommand', () => {
  test('a look change returns the described look', async () => {
    const { run } = setup()
    const r = await run('equip_item', { slot: 'mainhand', itemId: 19019 })
    expect(r.ok).toBe(true)
    expect(r.result).toMatchObject({ character: 'Orc male HD', items: { mainhand: { itemId: 19019, name: 'Thunderfury' } } })
    expect((r.result as { customization: object[] }).customization).toEqual([{ optionId: 20, name: 'Face', current: { choiceId: 384, name: 'Scarred' } }])
  })

  test('get_look lists customization options with the current choice', async () => {
    const { run } = setup()
    await run('set_customization', { optionId: 20, choiceId: 390 })
    const r = await run('get_look')
    expect(r.result).toMatchObject({
      customization: [{ optionId: 20, name: 'Face', current: { choiceId: 390, name: '#2' }, choices: [{ choiceId: 384, name: 'Scarred' }, { choiceId: 390, name: '#2' }] }],
    })
  })

  test('compare and set_character take their arguments by name', async () => {
    const { run, store } = setup()
    expect((await run('compare', { characters: [{ race: 5, sex: 1, models: 'sd' }] })).ok).toBe(true)
    expect(store.get().look.compare).toEqual([{ race: 5, sex: 1, models: 'sd' }])
    expect((await run('set_character', { race: 5, sex: 1, models: null })).ok).toBe(true)
    expect(store.get().look.race).toBe(5)
  })

  test('equip_items takes a list of slots and item IDs', async () => {
    const { run, store } = setup()
    expect((await run('equip_items', { items: [{ slot: 'chest', itemId: 1 }, { slot: 'mainhand', itemId: 19019 }] })).ok).toBe(true)
    expect(store.get().look.items).toEqual({ chest: 1, mainhand: 19019 })
  })

  test('errors and unknown commands come back as errors', async () => {
    const { run } = setup()
    expect(await run('set_view', { view: 'top' })).toEqual({ ok: false, error: 'unknown view "top"' })
    expect(await run('delete_everything')).toEqual({ ok: false, error: 'unknown command "delete_everything"' })
  })

  test('get_look describes each side-by-side character and its own outfit', async () => {
    const { run } = setup()
    await run('compare', { characters: [{ race: 5, sex: 1, label: 'Level 30', items: { mainhand: 19019 } }, { race: 2, sex: 0 }] })
    const r = await run('get_look')
    expect((r.result as { compare: unknown[] }).compare).toEqual([
      { character: 'Undead female HD', label: 'Level 30', items: { mainhand: { itemId: 19019, name: 'Thunderfury' } } },
      { character: 'Orc male HD', wears: 'main outfit' },
    ])
  })

  test('with a settle function, answers only after the tab has drawn the change', async () => {
    const { store } = setup()
    const cmd = createCommands({ store, searchItems: async () => [], appUrl: () => 'http://app.test/' })
    store.set({ notices: { 'cell:main': ['item 7418 section 1: no texture'] } })
    // The drawing clears the old notice; the answer must come after it.
    const settle = async () => { store.set({ notices: { 'cell:main': [] } }) }
    const r = await runRemoteCommand(cmd, store, 'unequip', { slot: 'chest' }, settle)
    expect((r.result as { notices: string[] }).notices).toEqual([])
  })

  test('share_link returns a link', async () => {
    const { run } = setup()
    const r = await run('share_link')
    expect((r.result as { link: string }).link).toMatch(/^http:\/\/app\.test\/#look=/)
  })
})
