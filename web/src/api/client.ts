// Every request the app makes to the local server goes through this module.
import type {
  ApiErrorBody, BaseLook, CharactersResponse, InstallRequest, ItemSearchQuery, ItemSearchResult, ItemSetResult,
  ModelSet, ResolvedItem, Status,
} from './types'

export class ApiError extends Error {
  readonly code: string
  readonly status: number
  constructor(status: number, code: string, message: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

const API = '/api/v1'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(`${API}${path}`, init)
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e
    throw new ApiError(0, 'network', `cannot reach the Altrobe server (${(e as Error).message})`)
  }
  if (res.ok) return res.json() as Promise<T>
  let body: Partial<ApiErrorBody> = {}
  try { body = await res.json() } catch { /* not JSON */ }
  throw new ApiError(res.status, body.error?.code ?? `http_${res.status}`, body.error?.message ?? `${path}: HTTP ${res.status}`)
}

export const getStatus = () => request<Status>('/status')

export const selectInstall = (body: InstallRequest) => request<Status>('/install', {
  method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body),
})

export const getCharacters = () => request<CharactersResponse>('/characters')

// Base looks and resolved items never change for a build, so cache them for the session. The cache
// holds the promise so concurrent callers share one request; failures are evicted so they can retry.
const cache = new Map<string, Promise<unknown>>()
function cached<T>(key: string, load: () => Promise<T>): Promise<T> {
  if (!cache.has(key)) cache.set(key, load().catch((e) => { cache.delete(key); throw e }))
  return cache.get(key) as Promise<T>
}
export function clearCache() {
  cache.clear()
}

export const getBaseLook = (race: number, sex: number, models: ModelSet) =>
  cached(`look:${race}:${sex}:${models}`, () => request<BaseLook>(`/characters/${race}/${sex}?models=${models}`))

/** Resolved item data for one character, or null when the server has none (404). */
export const getResolvedItem = (itemId: number, race: number, sex: number, models: ModelSet) =>
  cached(`item:${itemId}:${race}:${sex}:${models}`, () =>
    request<ResolvedItem>(`/items/${itemId}/resolved?race=${race}&sex=${sex}&models=${models}`)
      .catch((e) => { if (e instanceof ApiError && e.status === 404) return null; throw e }))

export function searchItems(query: ItemSearchQuery, signal?: AbortSignal) {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== '') p.set(k, String(v))
  return request<ItemSearchResult[]>(`/items/search?${p}`, { signal })
}

export function searchSets(query: { q?: string; limit?: number; offset?: number }, signal?: AbortSignal) {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== '') p.set(k, String(v))
  return request<ItemSetResult[]>(`/sets/search?${p}`, { signal })
}

export const modelUrl = (build: string, fdid: number, ext: 'glb' | 'json') =>
  `/assets/${encodeURIComponent(build)}/models/${fdid}.${ext}`
export const textureUrl = (build: string, fdid: number) =>
  `/assets/${encodeURIComponent(build)}/textures/${fdid}.png`
