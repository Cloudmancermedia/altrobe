// Every request the app makes goes through this module. A build-time switch picks the backend: the
// local server (local.ts), or in Vite's static mode (`vite build --mode static`, `npm run dev:static`)
// a baked bundle (static.ts) for the hosted site. VITE_ALTROBE_BUNDLE_URL (default /bundle) and
// VITE_ALTROBE_PRODUCT (default wow_classic_beta) say where the bundle is. Base looks, resolved
// items and set lists never change for a build, so they are cached here whichever backend answers.
import { readEvents } from '../assistant/events'
import type { Backend } from './backend'
import { ApiError } from './errors'
import { API, localBackend, rememberStatus, request } from './local'
import { createStaticBackend } from './static'
import type {
  ApiErrorBody, AssistantEvent, AssistantSettingsInput, AssistantSettingsView, AssistantTestResult, AssistantTurn, InstallRequest, ItemSearchQuery, ModelSet,
} from './types'

export { ApiError } from './errors'
export { rememberStatus }

const backend: Backend = import.meta.env.MODE === 'static'
  ? createStaticBackend({ baseUrl: import.meta.env.VITE_ALTROBE_BUNDLE_URL || '/bundle', product: import.meta.env.VITE_ALTROBE_PRODUCT || 'wow_classic_beta' })
  : localBackend

/** What the chosen backend offers; the hosted site has no install picker, command channel or prompt box. */
export const features = backend.features

export const getStatus = () => backend.getStatus()
export const selectInstall = (body: InstallRequest) => backend.selectInstall(body)
export const getCharacters = () => backend.getCharacters()

// The cache holds the promise so concurrent callers share one request; failures are evicted so they can retry.
const cache = new Map<string, Promise<unknown>>()
function cached<T>(key: string, load: () => Promise<T>): Promise<T> {
  if (!cache.has(key)) cache.set(key, load().catch((e) => { cache.delete(key); throw e }))
  return cache.get(key) as Promise<T>
}
export function clearCache() {
  cache.clear()
}

export const getBaseLook = (race: number, sex: number, models: ModelSet) =>
  cached(`look:${race}:${sex}:${models}`, () => backend.getBaseLook(race, sex, models))

/** Resolved item data for one character, or null when there is none (404). */
export const getResolvedItem = (itemId: number, race: number, sex: number, models: ModelSet) =>
  cached(`item:${itemId}:${race}:${sex}:${models}`, () => backend.getResolvedItem(itemId, race, sex, models))

export const searchItems = (query: ItemSearchQuery, signal?: AbortSignal) => backend.searchItems(query, signal)

/** The notable sets a race can wear, for one of its classes or (no classId) for every class it can be. */
export const getNotableSets = (race: number, classId?: number) =>
  cached(`notable:${race}:${classId ?? ''}`, () => backend.getNotableSets(race, classId))

/** Item IDs of every set piece, so equipping a set can replace the last one (replacedSetSlots). */
export const getSetPieces = () => cached('set-pieces', () => backend.getSetPieces())
export const getTitles = () => cached('titles', () => backend.getTitles())
export const getNames = () => cached('names', () => backend.getNames())

export const searchSets = (query: { q?: string; limit?: number; offset?: number }, signal?: AbortSignal) => backend.searchSets(query, signal)

export const modelUrl = (build: string, fdid: number, ext: 'glb' | 'json') => backend.modelUrl(build, fdid, ext)
export const animUrl = (build: string, modelFdid: number, animId: number) => backend.animUrl(build, modelFdid, animId)
export const textureUrl = (build: string, fdid: number) => backend.textureUrl(build, fdid)

// Prompt box, local server only (features.assistant). The key goes up once and never comes back.
async function local<T>(path: string, init?: RequestInit): Promise<T> {
  if (!features.assistant) throw new ApiError(400, 'not_available', 'The prompt box runs only in the local app.')
  return request<T>(path, init)
}

export const getAssistantSettings = () => local<AssistantSettingsView>('/assistant/settings')

export const saveAssistantSettings = (body: AssistantSettingsInput) => local<AssistantSettingsView>('/assistant/settings', {
  method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body),
})

/** Answers 200 or 502 with the same shape; a missing setting is an ApiError (409). */
export async function testAssistant(): Promise<AssistantTestResult> {
  if (!features.assistant) throw new ApiError(400, 'not_available', 'The prompt box runs only in the local app.')
  const res = await fetch(`${API}/assistant/test`, { method: 'POST' })
  const body = await res.json()
  if (res.status === 200 || res.status === 502) return body as AssistantTestResult
  throw new ApiError(res.status, body.error?.code ?? `http_${res.status}`, body.error?.message ?? `HTTP ${res.status}`)
}

/** Runs one prompt, calling onEvent for each step as the server reports it. */
export async function promptAssistant(text: string, history: AssistantTurn[], onEvent: (e: AssistantEvent) => void, signal?: AbortSignal): Promise<void> {
  if (!features.assistant) throw new ApiError(400, 'not_available', 'The prompt box runs only in the local app.')
  const res = await fetch(`${API}/assistant/prompt`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ text, history }), signal,
  })
  if (!res.ok || !res.body) {
    let body: Partial<ApiErrorBody> = {}
    try { body = await res.json() } catch { /* not JSON */ }
    throw new ApiError(res.status, body.error?.code ?? `http_${res.status}`, body.error?.message ?? `HTTP ${res.status}`)
  }
  await readEvents(res.body, onEvent)
}
