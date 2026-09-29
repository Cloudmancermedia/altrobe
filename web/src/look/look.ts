// Saved looks and share links. Pure; runs in the browser and in Node.
//
// A look is a small JSON object that names a character and what it wears by game IDs only:
//   { v: 1, game: "forever", build: "1.60.1.70009", models: "hd" | "sd", race: 2, sex: 0,
//     custom: { "<optionId>": choiceId },   only choices that differ from the data-driven defaults
//     items: { "<slot>": itemId },          slot names from dress.ts SLOT_NAMES
//     hide: ["head", "back"],               optional hidden slots
//     cam: { view: "front" | "side" | "back" | "head" },
//     compare: [{ race, sex, models,        optional, up to 5 more characters shown side by side
//                 items?, hide?, custom?,   an own outfit; without "items" the entry wears the main one
//                 label? }] }
// A share link carries it in the URL fragment, which browsers never send to a server:
//   /#look=<base64url(canonical JSON)>
// docs/look-format.md describes the format, including the "compare" addition.
//
// Loading is forgiving: unknown fields are ignored, bad entries are dropped, and every change is
// reported as a notice. Only a look it cannot render at all (no race, sex or game) fails.

import type { ModelSet, ResolvedItem } from '../api/types'
import { SLOT_NAMES, SHOULDER_SLOT_L, SHOULDER_SLOT_R, isSlotName, slotsForInventoryType, type DressState, type SlotName } from '../viewer/dress'

export const LOOK_VERSION = 1
export const GAME = 'forever'
export const VIEWS = ['front', 'side', 'back', 'head'] as const
export type View = (typeof VIEWS)[number]
export const MODEL_SETS: readonly ModelSet[] = ['hd', 'sd']
/** Side by side shows the main character plus up to this many more. */
export const MAX_COMPARE = 5
export const MAX_LABEL = 40

export interface CompareCharacter {
  race: number
  sex: 0 | 1
  models: ModelSet
  /** Shown above the character, such as "Level 30". */
  label?: string
  /** The character's own outfit. Without it, the character wears the main outfit (and its hidden slots). */
  items?: Partial<Record<SlotName, number>>
  hide?: SlotName[]
  /** Customization choices for this character's own body model. */
  custom?: Record<string, number>
}

export interface Look {
  v: number
  game: string
  build: string
  models: ModelSet
  race: number
  sex: 0 | 1
  custom: Record<string, number>
  items: Partial<Record<SlotName, number>>
  hide: SlotName[]
  cam: { view: View }
  compare: CompareCharacter[]
}

export interface LookResult {
  look: Look | null
  notices: string[]
}

/** Race and sex pairs the server has data for, to flag looks for characters it cannot show. */
export type KnownCharacters = { race: number; sex: number }[]

const isId = (x: unknown): x is number => Number.isInteger(x) && (x as number) > 0
const isIdKey = (k: string) => /^[1-9]\d*$/.test(k)
const isSex = (x: unknown): x is 0 | 1 => x === 0 || x === 1

/** A bare look for a character: no items, no customizations. */
export function emptyLook(race: number, sex: 0 | 1, models: ModelSet = 'hd', build = ''): Look {
  return { v: LOOK_VERSION, game: GAME, build, models, race, sex, custom: {}, items: {}, hide: [], cam: { view: 'front' }, compare: [] }
}

/**
 * Reads any parsed JSON value as a v1 look. Unknown fields are dropped; malformed entries are dropped
 * with a notice. Returns { look: null } only when the look cannot be rendered at all.
 */
export function normalizeLook(input: unknown, { characters }: { characters?: KnownCharacters } = {}): LookResult {
  const notices: string[] = []
  if (!isPlainObject(input)) return { look: null, notices: ['not a look: expected a JSON object'] }

  // Version. There is no older format, so a missing "v" is read as v1 and said so; a newer one is read
  // as v1 too (its extra fields are ignored), which is the forward-compatibility promise.
  const v = input.v
  if (v === undefined) notices.push(`look has no schema version; read as v${LOOK_VERSION}`)
  else if (!Number.isInteger(v) || (v as number) < 1) return { look: null, notices: [`unsupported look version ${JSON.stringify(v)}`] }
  else if ((v as number) > LOOK_VERSION) notices.push(`look is v${v}, this viewer reads v${LOOK_VERSION}; fields it does not know were ignored`)

  const game = input.game ?? GAME
  if (game !== GAME) return { look: null, notices: [`look is for game "${String(game)}", this viewer shows "${GAME}"`] }
  if (!Number.isInteger(input.race) || !isSex(input.sex)) return { look: null, notices: ['look needs an integer race and a sex of 0 or 1'] }

  const look = emptyLook(input.race as number, input.sex, 'hd', typeof input.build === 'string' ? input.build : '')
  if (typeof input.build !== 'string') notices.push('look has no game build')

  if (MODEL_SETS.includes(input.models as ModelSet)) look.models = input.models as ModelSet
  else if (input.models !== undefined) notices.push(`unknown models "${String(input.models)}"; using hd`)

  look.custom = readCustom(input.custom, notices)
  look.items = readItems(input.items, notices)
  look.hide = readHide(input.hide, notices)
  const view = isPlainObject(input.cam) ? input.cam.view : undefined
  if (VIEWS.includes(view as View)) look.cam.view = view as View
  else if (view !== undefined) notices.push(`unknown camera view "${String(view)}"; using front`)

  if (input.compare !== undefined) {
    const list = Array.isArray(input.compare) ? input.compare : []
    if (!Array.isArray(input.compare)) notices.push('dropped "compare": not a list')
    for (const c of list) {
      if (!isPlainObject(c) || !Number.isInteger(c.race) || !isSex(c.sex)) { notices.push(`dropped compare entry ${JSON.stringify(c)}`); continue }
      if (look.compare.length >= MAX_COMPARE) { notices.push(`dropped compare entries past ${MAX_COMPARE}`); break }
      const models = MODEL_SETS.includes(c.models as ModelSet) ? c.models as ModelSet : 'hd'
      if (c.models !== undefined && models !== c.models) notices.push(`unknown models "${String(c.models)}" in compare; using hd`)
      const entry: CompareCharacter = { race: c.race as number, sex: c.sex, models }
      const label = typeof c.label === 'string' ? c.label.trim().slice(0, MAX_LABEL) : ''
      if (label) entry.label = label
      if (c.items !== undefined) entry.items = readItems(c.items, notices)
      if (c.hide !== undefined) entry.hide = readHide(c.hide, notices)
      if (c.custom !== undefined) entry.custom = readCustom(c.custom, notices)
      look.compare.push(entry)
    }
  }

  if (characters) {
    for (const c of [look, ...look.compare]) {
      if (!characters.some((k) => k.race === c.race && k.sex === c.sex)) notices.push(`no character data for race ${c.race} sex ${c.sex}`)
    }
  }
  return { look, notices }
}

function readCustom(input: unknown, notices: string[]): Record<string, number> {
  const out: Record<string, number> = {}
  for (const [k, val] of Object.entries(isPlainObject(input) ? input : {})) {
    if (isIdKey(k) && isId(val)) out[k] = val
    else notices.push(`dropped customization ${k}: ${JSON.stringify(val)} (not an option ID and choice ID)`)
  }
  return out
}

function readItems(input: unknown, notices: string[]): Partial<Record<SlotName, number>> {
  const out: Partial<Record<SlotName, number>> = {}
  for (const [slot, id] of Object.entries(isPlainObject(input) ? input : {})) {
    if (!isSlotName(slot)) notices.push(`dropped item in unknown slot "${slot}"`)
    else if (!isId(id)) notices.push(`dropped ${slot}: ${JSON.stringify(id)} is not an item ID`)
    else out[slot] = id
  }
  return out
}

function readHide(input: unknown, notices: string[]): SlotName[] {
  const out: SlotName[] = []
  for (const slot of Array.isArray(input) ? input : []) {
    if (typeof slot !== 'string' || !isSlotName(slot)) notices.push(`dropped hidden slot "${String(slot)}"`)
    else if (!out.includes(slot)) out.push(slot)
  }
  return out
}

type Json = Record<string, unknown>

/**
 * Canonical form: sorted keys at every level, empty or default optional fields left out, and
 * customizations equal to the defaults left out. The same look always gives the same JSON.
 * @param defaults  default choice per option ID, from defaultsFrom(baseLook)
 */
export function canonicalLook(look: Look, { defaults = {} }: { defaults?: Record<string, number> } = {}): Json {
  const out: Json = { build: look.build, game: look.game, models: look.models, race: look.race, sex: look.sex, v: look.v }
  const custom = sortKeys(Object.fromEntries(Object.entries(look.custom ?? {}).filter(([k, v]) => defaults[k] !== v)))
  const items = sortKeys(look.items ?? {})
  const hide = [...new Set(look.hide ?? [])].sort()
  if (look.cam?.view && look.cam.view !== 'front') out.cam = { view: look.cam.view }
  if (Object.keys(custom).length) out.custom = custom
  if (hide.length) out.hide = hide
  if (Object.keys(items).length) out.items = items
  // Order matters here (it is the order on screen), so the list is not sorted.
  if (look.compare?.length) out.compare = look.compare.map((c) => {
    const e: Json = { models: c.models, race: c.race, sex: c.sex }
    if (c.label) e.label = c.label
    // An own outfit is kept even when empty: it means "wears nothing", not "wears the main outfit".
    if (c.items) e.items = sortKeys(c.items)
    if (c.hide?.length) e.hide = [...new Set(c.hide)].sort()
    if (c.custom && Object.keys(c.custom).length) e.custom = sortKeys(c.custom)
    return sortKeys(e)
  })
  return sortKeys(out)
}

export function canonicalJSON(look: Look, opts?: { defaults?: Record<string, number> }): string {
  return JSON.stringify(canonicalLook(look, opts))
}

/** base64url(canonical JSON), for the #look= fragment. */
export function encodeLook(look: Look, opts?: { defaults?: Record<string, number> }): string {
  const bytes = new TextEncoder().encode(canonicalJSON(look, opts))
  let bin = ''
  for (const b of bytes) bin += String.fromCharCode(b)
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/** Inverse of encodeLook, then normalizeLook. Never throws. Accepts a percent-encoded value. */
export function decodeLook(text: string, opts?: { characters?: KnownCharacters }): LookResult {
  let parsed: unknown
  try {
    const b64 = decodeURIComponent(text).replace(/-/g, '+').replace(/_/g, '/')
    const bin = atob(b64 + '='.repeat((4 - (b64.length % 4)) % 4))
    parsed = JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0))))
  } catch (e) {
    return { look: null, notices: [`could not read the look link: ${(e as Error).message}`] }
  }
  return normalizeLook(parsed, opts)
}

/** The look in a URL fragment ("#look=..."), or null when there is none. */
export function lookFromFragment(hash: string | null | undefined, opts?: { characters?: KnownCharacters }): LookResult | null {
  const m = /(?:^#|&)look=([^&]*)/.exec(hash ?? '')
  return m ? decodeLook(m[1], opts) : null
}

/** App URL (no query, no fragment) + #look=... */
export function shareUrl(appUrl: string, look: Look, opts?: { defaults?: Record<string, number> }): string {
  return `${appUrl.split(/[?#]/)[0]}#look=${encodeLook(look, opts)}`
}

/** Default choice per option ID from a base look. */
export function defaultsFrom(baseLook: { choices?: { optionId: number; choiceId: number | null }[] } | null | undefined): Record<string, number> {
  return Object.fromEntries((baseLook?.choices ?? []).filter((c) => isId(c.choiceId)).map((c) => [String(c.optionId), c.choiceId as number]))
}

/** Selectable choice IDs per option ID from a base look. */
export function choicesFrom(baseLook: { options?: { optionId: number; choices: { choiceId: number }[] }[] } | null | undefined): Record<string, number[]> {
  return Object.fromEntries((baseLook?.options ?? []).map((o) => [String(o.optionId), o.choices.map((c) => c.choiceId)]))
}

/**
 * Checks a normalized look against the loaded game data and drops what cannot be shown.
 * @param data.build  the build of the loaded data
 * @param data.defaults  from defaultsFrom()
 * @param data.choices  from choicesFrom(); when given, a choice not listed for its option is dropped
 * @param data.resolvedById  resolved item data; a missing entry means the item has no data for this character
 * @returns a new look plus notices
 */
export function checkAgainstData(look: Look, { build, defaults, choices, resolvedById }: {
  build?: string; defaults?: Record<string, number>; choices?: Record<string, number[]>; resolvedById?: Map<number, ResolvedItem | null>
} = {}): { look: Look; notices: string[] } {
  const notices: string[] = []
  const out: Look = { ...look, custom: { ...look.custom }, items: { ...look.items }, hide: [...look.hide], cam: { ...look.cam }, compare: look.compare.map((c) => ({ ...c })) }
  if (build && look.build !== build) notices.push(`look was made on build ${look.build || '(unknown)'}; showing it with data from build ${build}`)
  if (defaults) for (const [opt, choice] of Object.entries(out.custom)) {
    if (!(opt in defaults)) { notices.push(`dropped customization option ${opt}: not an option for this character (${look.models})`); delete out.custom[opt] }
    else if (defaults[opt] === choice) delete out.custom[opt]
    else if (choices?.[opt] && !choices[opt].includes(choice)) {
      notices.push(`dropped customization ${opt}=${choice}: not a choice for this character (${look.models})`)
      delete out.custom[opt]
    }
  }
  if (resolvedById) for (const [slot, id] of Object.entries(out.items) as [SlotName, number][]) {
    const r = resolvedById.get(id)
    if (!r || r.error) { notices.push(`dropped ${slot} item ${id}: no item data${r?.error ? ` (${r.error})` : ''}`); delete out.items[slot] }
    else if (!slotsForInventoryType(r.inventoryType).includes(slot)) {
      notices.push(`dropped ${slot} item ${id}: inventory type ${r.inventoryType} does not go in that slot`)
      delete out.items[slot]
    }
  }
  return { look: out, notices }
}

/** The dress.ts state for a look: { character, items: { slotId: itemId } }, hidden slots left out. */
export function dressStateFor(look: Pick<Look, 'race' | 'sex' | 'models' | 'items' | 'hide'>): DressState {
  const items: Record<number, number> = {}
  for (const [slot, id] of Object.entries(look.items) as [SlotName, number][]) {
    if (look.hide.includes(slot)) continue
    items[SLOT_NAMES[slot]] = id
    if (SLOT_NAMES[slot] === SHOULDER_SLOT_L) items[SHOULDER_SLOT_R] = id
  }
  return { character: `${look.race}-${look.sex}-${look.models}`, items }
}

function isPlainObject(x: unknown): x is Record<string, unknown> {
  return !!x && typeof x === 'object' && !Array.isArray(x)
}
function sortKeys<T extends object>(o: T): T {
  return Object.fromEntries(Object.keys(o).sort().map((k) => [k, (o as Record<string, unknown>)[k]])) as T
}
