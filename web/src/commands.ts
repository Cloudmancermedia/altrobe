// The command API. The UI changes the look only through these functions, and Phase 3's
// plain-language layer will call the same ones. `transitions` are pure (look in, look out);
// createCommands wires them to the app store and the server.

import type { CharactersResponse, CustomizationOption, ItemSearchQuery, ItemSearchResult, ModelSet } from './api/types'
import {
  MAX_COMPARE, MAX_LABEL, MODEL_SETS, VIEWS, canonicalLook, normalizeLook, shareUrl,
  type CompareCharacter, type Look, type View,
} from './look/look'
import { randomCustomization } from './viewer/customize'
import { isSlotName, type SlotName } from './viewer/dress'
import type { AppState, Store } from './store'

export type Result = { look: Look; error?: undefined } | { look?: undefined; error: string }

/** A side-by-side character as commands take it: models defaults to hd; the rest is optional. */
export interface CompareInput {
  race: number
  sex: number
  models?: ModelSet
  label?: string
  items?: Record<string, number>
  hide?: string[]
  custom?: Record<string, number>
}

const isId = (x: unknown): x is number => Number.isInteger(x) && (x as number) > 0

/** Checks a character against the server's list when it is known. Returns an error or null. */
function characterError(characters: CharactersResponse | null | undefined, race: number, sex: number, models: ModelSet): string | null {
  if (!Number.isInteger(race) || !(sex === 0 || sex === 1)) return `not a character: race ${race} sex ${sex}`
  if (!MODEL_SETS.includes(models)) return `unknown models "${models}"`
  if (!characters) return null
  const r = characters.races.find((x) => x.race === race)
  const s = r?.sexes.find((x) => x.sex === sex)
  if (!r || !s) return `no character data for race ${race} sex ${sex}`
  if (!s[models]) return `${r.name} ${sex === 0 ? 'male' : 'female'} has no ${models.toUpperCase()} model`
  return null
}

export const transitions = {
  equipItem(look: Look, slot: string, itemId: number): Result {
    if (!isSlotName(slot)) return { error: `unknown slot "${slot}"` }
    if (!isId(itemId)) return { error: `not an item ID: ${itemId}` }
    return { look: { ...look, items: { ...look.items, [slot]: itemId } } }
  },

  /** Several items in one change, such as the pieces of a set. Other slots keep what they have. */
  equipItems(look: Look, items: { slot: string; itemId: number }[]): Result {
    if (!Array.isArray(items) || items.length === 0) return { error: 'no items to equip' }
    let next = look
    for (const { slot, itemId } of items) {
      const r = transitions.equipItem(next, slot, itemId)
      if (r.error !== undefined) return r
      next = r.look
    }
    return { look: next }
  },

  unequip(look: Look, slot: string): Result {
    if (!isSlotName(slot)) return { error: `unknown slot "${slot}"` }
    const items = { ...look.items }
    delete items[slot]
    return { look: { ...look, items, hide: look.hide.filter((s) => s !== slot) } }
  },

  /** Customization option IDs belong to one body model, so changing race, sex or models clears them. */
  setCharacter(look: Look, race: number, sex: number, models: ModelSet = look.models, characters?: CharactersResponse | null): Result {
    const err = characterError(characters, race, sex, models)
    if (err) return { error: err }
    const same = look.race === race && look.sex === sex && look.models === models
    return { look: { ...look, race, sex: sex as 0 | 1, models, custom: same ? look.custom : {} } }
  },

  /** `options` are the main character's, from its base look; without them only the IDs' shape is checked. */
  setCustomization(look: Look, optionId: number, choiceId: number, options?: CustomizationOption[] | null): Result {
    if (!isId(optionId) || !isId(choiceId)) return { error: `not an option ID and choice ID: ${optionId}, ${choiceId}` }
    if (options?.length) {
      const o = options.find((x) => x.optionId === optionId)
      if (!o) return { error: `no customization option ${optionId} for this character` }
      if (!o.choices.some((c) => c.choiceId === choiceId)) return { error: `${o.name || `option ${optionId}`} has no choice ${choiceId}` }
    }
    return { look: { ...look, custom: { ...look.custom, [String(optionId)]: choiceId } } }
  },

  /** A random choice for every option of the main character. */
  randomizeCustomization(look: Look, options: CustomizationOption[] | null | undefined, random?: () => number): Result {
    if (!options?.length) return { error: 'no customization options for this character yet' }
    return { look: { ...look, custom: randomCustomization(options, random) } }
  },

  /** Back to the default choice for every option. */
  resetCustomization(look: Look): Result {
    return { look: { ...look, custom: {} } }
  },

  /**
   * The characters shown next to the main one, in order; at most MAX_COMPARE. A character with
   * `items` wears its own outfit (and `hide`); without, it wears the main outfit.
   */
  compare(look: Look, characters: CompareInput[], known?: CharactersResponse | null): Result {
    if (!Array.isArray(characters)) return { error: 'compare needs a list of characters' }
    if (characters.length > MAX_COMPARE) return { error: `side by side shows at most ${MAX_COMPARE + 1} characters` }
    const list: CompareCharacter[] = []
    for (const c of characters) {
      const models = c.models ?? 'hd'
      const err = characterError(known, c.race, c.sex, models)
      if (err) return { error: err }
      const entry: CompareCharacter = { race: c.race, sex: c.sex as 0 | 1, models }
      const label = typeof c.label === 'string' ? c.label.trim().slice(0, MAX_LABEL) : ''
      if (label) entry.label = label
      if (c.items) {
        const items: CompareCharacter['items'] = {}
        for (const [slot, id] of Object.entries(c.items)) {
          if (!isSlotName(slot)) return { error: `unknown slot "${slot}"` }
          if (!isId(id)) return { error: `not an item ID: ${id}` }
          items[slot] = id
        }
        entry.items = items
      }
      if (c.hide) {
        const bad = c.hide.find((s) => !isSlotName(s))
        if (bad !== undefined) return { error: `unknown slot "${bad}"` }
        entry.hide = [...new Set(c.hide)] as SlotName[]
      }
      if (c.custom) {
        for (const [o, ch] of Object.entries(c.custom)) if (!isId(Number(o)) || !isId(ch)) return { error: `not an option ID and choice ID: ${o}, ${ch}` }
        entry.custom = { ...c.custom }
      }
      list.push(entry)
    }
    return { look: { ...look, compare: list } }
  },

  /** A side-by-side character (0-based) drops its own outfit and wears the main one again. */
  wearMainOutfit(look: Look, index: number): Result {
    if (!Number.isInteger(index) || index < 0 || index >= look.compare.length) return { error: `no side-by-side character ${index + 1}` }
    const compare = look.compare.map((c, i) => {
      if (i !== index) return c
      const next = { ...c }
      delete next.items
      delete next.hide
      return next
    })
    return { look: { ...look, compare } }
  },

  setVisibility(look: Look, slot: string, visible: boolean): Result {
    if (!isSlotName(slot)) return { error: `unknown slot "${slot}"` }
    const hide = look.hide.filter((s) => s !== slot)
    if (!visible) hide.push(slot)
    return { look: { ...look, hide } }
  },

  setView(look: Look, view: string): Result {
    if (!VIEWS.includes(view as View)) return { error: `unknown view "${view}"` }
    return { look: { ...look, cam: { view: view as View } } }
  },
}

export interface CommandContext {
  store: Store<AppState>
  searchItems: (query: ItemSearchQuery) => Promise<ItemSearchResult[]>
  /** The app's own URL, for share links. */
  appUrl: () => string
}

export type Commands = ReturnType<typeof createCommands>

export function createCommands({ store, searchItems, appUrl }: CommandContext) {
  // Every change is stamped with the build of the data it was made with.
  function apply(r: Result): Result {
    if (r.error !== undefined) {
      store.set((s) => ({ notices: { ...s.notices, commands: [r.error] } }))
      return r
    }
    const build = store.get().status?.active?.build
    const look = build ? { ...r.look, build } : r.look
    store.set((s) => ({ look, notices: { ...s.notices, commands: [] } }))
    return { look }
  }
  const look = () => store.get().look
  const characters = () => store.get().characters

  return {
    search_items: (query: ItemSearchQuery) => searchItems(query),
    equip_item: (slot: SlotName | string, itemId: number) => apply(transitions.equipItem(look(), slot, itemId)),
    equip_items: (items: { slot: SlotName | string; itemId: number }[]) => apply(transitions.equipItems(look(), items)),
    unequip: (slot: SlotName | string) => apply(transitions.unequip(look(), slot)),
    set_character: (race: number, sex: number, models?: ModelSet) =>
      apply(transitions.setCharacter(look(), race, sex, models, characters())),
    set_customization: (optionId: number, choiceId: number) => apply(transitions.setCustomization(look(), optionId, choiceId, store.get().options)),
    randomize_customization: () => apply(transitions.randomizeCustomization(look(), store.get().options)),
    reset_customization: () => apply(transitions.resetCustomization(look())),
    compare: (list: CompareInput[]) => apply(transitions.compare(look(), list, characters())),
    wear_main_outfit: (index: number) => apply(transitions.wearMainOutfit(look(), index)),
    set_visibility: (slot: SlotName | string, visible: boolean) => apply(transitions.setVisibility(look(), slot, visible)),
    set_view: (view: View | string) => apply(transitions.setView(look(), view)),
    /** Replaces the whole look, e.g. from a saved file. Returns the notices from reading it. */
    open_look: (input: unknown): { look: Look | null; notices: string[] } => {
      const r = normalizeLook(input, { characters: characters()?.races.flatMap((x) => x.sexes.map((s) => ({ race: x.race, sex: s.sex }))) })
      if (r.look) store.set((s) => ({ look: r.look!, notices: { ...s.notices, link: r.notices, commands: [] } }))
      else store.set((s) => ({ notices: { ...s.notices, link: r.notices } }))
      return r
    },
    /** The look in canonical form (sorted keys, defaults left out), as saved and shared. */
    get_look: () => canonicalLook(look(), { defaults: store.get().defaults }),
    share_link: () => shareUrl(appUrl(), look(), { defaults: store.get().defaults }),
  }
}
