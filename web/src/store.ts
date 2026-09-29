import { useSyncExternalStore } from 'react'
import type { CharactersResponse, CustomizationOption, ItemSearchResult, Status } from './api/types'
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
  /** Names and icons for item IDs seen in search results or resolved data. */
  itemInfo: Record<number, ItemInfo>
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
  itemInfo: {},
})

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
