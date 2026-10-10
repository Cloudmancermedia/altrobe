// Where the app's data comes from: the local server (local.ts) or a baked bundle on the hosted site
// (static.ts). client.ts picks one at build time and adds the session cache in front of it.
import type {
  BaseLook, CharactersResponse, InstallRequest, ItemSearchQuery, ItemSearchResult, ItemSetResult, ModelSet, RaceNames, ResolvedItem, SetGroup, Status, TitleInfo,
} from './types'

/** What only the local app has: picking an install, the MCP command channel, the prompt box. */
export interface Features {
  installs: boolean
  session: boolean
  assistant: boolean
}

export interface Backend {
  readonly features: Features
  getStatus(): Promise<Status>
  selectInstall(body: InstallRequest): Promise<Status>
  getCharacters(): Promise<CharactersResponse>
  getBaseLook(race: number, sex: number, models: ModelSet): Promise<BaseLook>
  /** Resolved item data for one character, or null when there is none. */
  getResolvedItem(itemId: number, race: number, sex: number, models: ModelSet): Promise<ResolvedItem | null>
  searchItems(query: ItemSearchQuery, signal?: AbortSignal): Promise<ItemSearchResult[]>
  searchSets(query: { q?: string; limit?: number; offset?: number }, signal?: AbortSignal): Promise<ItemSetResult[]>
  /** The notable sets a race can wear, for one of its classes or (no classId) for every class it can be. */
  getNotableSets(race: number, classId?: number): Promise<SetGroup[]>
  /** Item IDs of every set piece, so equipping a set can replace the last one (replacedSetSlots). */
  getSetPieces(): Promise<number[]>
  getTitles(): Promise<TitleInfo[]>
  /** Keyed by race ID. */
  getNames(): Promise<Record<string, RaceNames>>
  modelUrl(build: string, fdid: number, ext: 'glb' | 'json'): string
  animUrl(build: string, modelFdid: number, animId: number): string
  textureUrl(build: string, fdid: number): string
}
