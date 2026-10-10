// The local app's backend: the Altrobe server on this computer (/api/v1 and /assets).
import type { Backend } from './backend'
import { ApiError } from './errors'
import type { ApiErrorBody, CharactersResponse, ItemSearchResult, ItemSetResult, RaceNames, ResolvedItem, SetGroup, Status, BaseLook, TitleInfo } from './types'

export const API = '/api/v1'

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
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

// The converter version of the selected build, from the last status seen. Asset URLs carry it so a
// new converter's files get new URLs; the server caches assets in the browser for a year.
let assetVersion: number | undefined

export function rememberStatus(status: Status): Status {
  assetVersion = status.active?.assetVersion
  return status
}

const query = (q: object) => {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== '') p.set(k, String(v))
  return p
}

const versioned = (url: string) => (assetVersion === undefined ? url : `${url}?v=${assetVersion}`)

export const localBackend: Backend = {
  features: { installs: true, session: true, assistant: true },
  getStatus: () => request<Status>('/status').then(rememberStatus),
  selectInstall: (body) => request<Status>('/install', {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body),
  }).then(rememberStatus),
  getCharacters: () => request<CharactersResponse>('/characters'),
  getBaseLook: (race, sex, models) => request<BaseLook>(`/characters/${race}/${sex}?models=${models}`),
  getResolvedItem: (itemId, race, sex, models) =>
    request<ResolvedItem>(`/items/${itemId}/resolved?race=${race}&sex=${sex}&models=${models}`)
      .catch((e) => { if (e instanceof ApiError && e.status === 404) return null; throw e }),
  searchItems: (q, signal) => request<ItemSearchResult[]>(`/items/search?${query(q)}`, { signal }),
  searchSets: (q, signal) => request<ItemSetResult[]>(`/sets/search?${query(q)}`, { signal }),
  getNotableSets: (race, classId) => request<SetGroup[]>(`/sets/notable?race=${race}${classId ? `&class=${classId}` : ''}`),
  getSetPieces: () => request<number[]>('/sets/pieces'),
  getTitles: () => request<TitleInfo[]>('/titles'),
  getNames: () => request<Record<string, RaceNames>>('/names'),
  modelUrl: (build, fdid, ext) => versioned(`/assets/${encodeURIComponent(build)}/models/${fdid}.${ext}`),
  animUrl: (build, model, anim) => versioned(`/assets/${encodeURIComponent(build)}/anims/${model}/${anim}.glb`),
  textureUrl: (build, fdid) => versioned(`/assets/${encodeURIComponent(build)}/textures/${fdid}.png`),
}
