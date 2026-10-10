import { useSyncExternalStore } from 'react'
import type { AnimationInfo, CharactersResponse, CustomizationOption, ItemSearchResult, Status } from './api/types'
import { emptyLook, type Look } from './look/look'

export interface ItemInfo {
  name: string
  quality?: number
  iconFileDataId?: number
}

export interface AppState {
  look: Look
  /** Notices by source (e.g. "link", "cell:0"), so each source replaces only its own. */
  notices: Record<string, string[]>
  status: Status | null
  characters: CharactersResponse | null
  /** Default choice per option ID for the main character, from its base look. */
  defaults: Record<string, number>
  /** Every customization option and its choices for the main character, from its base look. */
  options: CustomizationOption[]
  /** Animations the main character's body model has. */
  animations: AnimationInfo[]
  /** Names and icons for item IDs seen in search results or resolved data. */
  itemInfo: Record<number, ItemInfo>
  /** Offer every class, not only the race's own (a Tauren in priest sets). Remembered in this browser. */
  anyClass: boolean
  /** A request from the character screen to search one slot's items; `n` changes on every request. */
  findSlot: { slot: string; n: number } | null
}

export interface Store<T> {
  get(): T
  set(update: Partial<T> | ((s: T) => Partial<T>)): void
  subscribe(listener: () => void): () => void
}

export function createStore<T extends object>(initial: T): Store<T> {
  let state = initial
  const listeners = new Set<() => void>()
  return {
    get: () => state,
    set(update) {
      const patch = typeof update === 'function' ? update(state) : update
      state = { ...state, ...patch }
      for (const l of listeners) l()
    },
    subscribe(l) {
      listeners.add(l)
      return () => listeners.delete(l)
    },
  }
}

export const initialState = (): AppState => ({
  look: emptyLook(2, 0),
  notices: {},
  status: null,
  characters: null,
  defaults: {},
  options: [],
  animations: [],
  itemInfo: {},
  anyClass: readAnyClass(),
  findSlot: null,
})

const ANY_CLASS_KEY = 'altrobe.anyClass'
// Storage can be missing or throw (private windows, blocked site data); the setting then just isn't kept.
function readAnyClass(): boolean {
  try { return globalThis.localStorage?.getItem(ANY_CLASS_KEY) === '1' } catch { return false }
}
export function setAnyClass(store: Store<AppState>, anyClass: boolean) {
  store.set({ anyClass })
  try { globalThis.localStorage?.setItem(ANY_CLASS_KEY, anyClass ? '1' : '0') } catch { /* not kept */ }
}

export function useStore<T, R>(store: Store<T>, select: (s: T) => R): R {
  return useSyncExternalStore(store.subscribe, () => select(store.get()))
}

export function rememberItems(store: Store<AppState>, items: (ItemSearchResult | { itemId: number; name: string })[]) {
  const known = store.get().itemInfo
  const fresh = items.filter((i) => !known[i.itemId] || ('quality' in i && known[i.itemId].quality === undefined))
  if (!fresh.length) return
  store.set((s) => ({
    itemInfo: {
      ...s.itemInfo,
      ...Object.fromEntries(fresh.map((i) => [i.itemId, { ...s.itemInfo[i.itemId], ...('quality' in i ? { name: i.name, quality: i.quality, iconFileDataId: i.iconFileDataId } : { name: i.name }) }])),
    },
  }))
}

export function setNotices(store: Store<AppState>, source: string, list: string[]) {
  const cur = store.get().notices[source] ?? []
  if (cur.length === list.length && cur.every((n, i) => n === list[i])) return
  store.set((s) => ({ notices: { ...s.notices, [source]: list } }))
}
