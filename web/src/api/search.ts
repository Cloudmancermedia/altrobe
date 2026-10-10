// Item and set search in the browser, over a baked bundle's catalog.json and sets.json (static
// backend). Ports ItemCatalog.Search and SetCatalog.Search: the catalogs come already ordered (by
// name, dev and NPC entries last), so a search keeps their order. search.test.ts runs the server's
// test cases against these.
import type { ItemSearchResult, ItemSetResult } from './types'

/** An ItemSummary as catalog.json and sets.json hold it; the search API sends the same fields. */
export interface BundleItem extends ItemSearchResult {
  weapon?: string | null
  hands?: string | null
  requiredLevel?: number | null
  itemLevel?: number | null
  armor?: string | null
  allowableClass?: number
}

/** An ItemSetInfo as sets.json and notable.json hold it: each piece wraps the whole item. */
export interface BundleSet {
  setId: number
  name: string
  pieces: { slot: string; item: BundleItem }[]
  skipped: { itemId: number; reason: string }[]
  internal: boolean
  unnamed?: boolean
  quality?: number | null
  classMask?: number
}

export interface ItemQuery {
  q?: string
  slots?: string[]
  qualities?: number[]
  limit?: number
  offset?: number
  /** The search panel, like /api/v1/items/search, includes dev and NPC items; MCP tools do not. */
  includeInternal?: boolean
  /** Also list items with no name, as /api/v1/items/search?unnamed=1 does. */
  includeUnnamed?: boolean
}

export interface SetQuery {
  q?: string
  limit?: number
  offset?: number
  includeInternal?: boolean
}

const ITEM_LIMIT = { fallback: 50, max: 200 }
const SET_LIMIT = { fallback: 20, max: 50 }

// int.TryParse on trimmed text: an optional sign, digits, within 32 bits.
function asId(text: string): number | null {
  if (!/^[+-]?\d+$/.test(text)) return null
  const n = Number(text)
  return Number.isSafeInteger(n) && n >= -2147483648 && n <= 2147483647 ? n : null
}

// string.Contains(text, StringComparison.OrdinalIgnoreCase).
const contains = (name: string, text: string) => name.toUpperCase().includes(text.toUpperCase())

function page<T>(matches: T[], limit: number | undefined, offset: number | undefined, bounds: { fallback: number; max: number }) {
  const l = Math.min(Math.max(limit ?? bounds.fallback, 1), bounds.max)
  const o = Math.max(0, offset ?? 0)
  return matches.slice(o, o + l)
}

export function searchItemsIn(items: BundleItem[], query: ItemQuery): { total: number; items: BundleItem[] } {
  const text = query.q?.trim() ?? ''
  const id = asId(text)
  const internal = query.includeInternal ?? true
  const matches = items.filter((i) =>
    (i.itemId === id || ((internal || !i.internal) && (query.includeUnnamed || !i.unnamed) && (text === '' || contains(i.name, text))))
    && (!query.slots?.length || query.slots.includes(i.slot))
    && (!query.qualities?.length || query.qualities.includes(i.quality)))
  return { total: matches.length, items: page(matches, query.limit, query.offset, ITEM_LIMIT) }
}

export function searchSetsIn(sets: BundleSet[], query: SetQuery): { total: number; sets: BundleSet[] } {
  const text = query.q?.trim() ?? ''
  const id = asId(text)
  const internal = query.includeInternal ?? true
  const matches = sets.filter((s) => s.setId === id || ((internal || !s.internal) && (text === ''
    || contains(s.name, text)
    || s.pieces.some((p) => (internal || !p.item.internal) && contains(p.item.name, text)))))
  return { total: matches.length, sets: page(matches, query.limit, query.offset, SET_LIMIT) }
}

/** A set as the API sends it (Api.SetJson): pieces flattened to item fields plus the slot. */
export function toSetResult(s: BundleSet): ItemSetResult {
  return {
    setId: s.setId, name: s.name, internal: s.internal, unnamed: s.unnamed, quality: s.quality,
    pieces: s.pieces.map((p) => ({ slot: p.slot, itemId: p.item.itemId, name: p.item.name, quality: p.item.quality, iconFileDataId: p.item.iconFileDataId, unnamed: p.item.unnamed })),
    skipped: s.skipped,
  }
}
