// The hosted site's backend: reads a static bundle written by BundleBaker (docs/hosted-pipeline.md)
// instead of asking a server. Each JSON file is read once; search runs in the browser (search.ts).
// It answers in the local server's shapes, so the rest of the app doesn't know which one it has.
import type { Backend } from './backend'
import { ApiError } from './errors'
import { searchItemsIn, searchSetsIn, toSetResult, type BundleItem, type BundleSet } from './search'
import type { BaseLook, CharactersResponse, ItemSetResult, RaceNames, ResolvedItem, SetGroup, Status, TitleInfo } from './types'

export interface StaticConfig {
  /** Where the bundle is served, such as /bundle or https://assets.example.com. */
  baseUrl: string
  product: string
  fetch?: typeof fetch
}

interface Current { product: string; build: string; buildName?: string; path: string; assetVersion: number; assetPath: string }
interface Characters extends CharactersResponse { looks: Record<string, BaseLook> }
type NotableFile = Record<string, Record<string, { group: string; sets: BundleSet[] }[]>>

// The API's comma-separated list parameters (Api.Split).
const list = (v: string | number | undefined) => (v === undefined || v === '' ? [] : String(v).split(',').map((s) => s.trim()).filter(Boolean))

const orMissing = <T>(p: Promise<T>, fallback: T): Promise<T> =>
  p.catch((e) => { if (e instanceof ApiError && e.status === 404) return fallback; throw e })

export function createStaticBackend(config: StaticConfig): Backend {
  const get = config.fetch ?? ((...a: Parameters<typeof fetch>) => fetch(...a))
  const root = `${config.baseUrl.replace(/\/+$/, '')}/${config.product}`
  const files = new Map<string, Promise<unknown>>()

  // One request per file for the session; a failure is dropped so it can be retried.
  function json<T>(path: string): Promise<T> {
    const url = `${root}/${path}`
    if (!files.has(url)) {
      files.set(url, (async () => {
        let res: Response
        try {
          res = await get(url)
        } catch (e) {
          throw new ApiError(0, 'network', `cannot reach the Altrobe bundle (${(e as Error).message})`)
        }
        if (!res.ok) throw new ApiError(res.status, res.status === 404 ? 'not_found' : `http_${res.status}`, `${path}: HTTP ${res.status}`)
        return res.json()
      })().catch((e) => { files.delete(url); throw e }))
    }
    return files.get(url) as Promise<T>
  }

  let current: Current | undefined
  const loadCurrent = async () => (current ??= await json<Current>('current.json'))
  const build = async (file: string) => json(`${(await loadCurrent()).path}${file}`)
  const characters = () => build('characters.json') as Promise<Characters>
  const catalog = () => (build('catalog.json') as Promise<{ items: BundleItem[] }>).then((c) => c.items)
  const sets = () => (build('sets.json') as Promise<{ sets: BundleSet[] }>).then((s) => s.sets)
  const assetUrl = (b: string, path: string) => {
    // Asset URLs are built synchronously; the app reads status (current.json) before any asset.
    const version = current?.assetVersion
    return version === undefined ? `${root}/${b}/${path}` : `${root}/${b}/v${version}/${path}`
  }

  return {
    features: { installs: false, session: false, assistant: false },

    async getStatus(): Promise<Status> {
      const c = await loadCurrent()
      return { version: 'static', installs: [], active: { path: '', product: c.product, build: c.build, assetVersion: c.assetVersion }, cacheFolder: '' }
    },

    async selectInstall() {
      throw new ApiError(400, 'not_available', 'The hosted site shows one published build; there is no install to choose.')
    },

    async getCharacters() {
      const c = await characters()
      return { build: c.build, races: c.races }
    },

    async getBaseLook(race, sex, models) {
      const look = (await characters()).looks[`${race}-${sex}-${models}`]
      if (!look) throw new ApiError(404, 'character_not_found', `No playable ${models.toUpperCase()} character for race ${race} sex ${sex}.`)
      return look
    },

    async getResolvedItem(itemId, race, sex, models): Promise<ResolvedItem | null> {
      let file: { variants: Record<string, ResolvedItem & { error?: string | null }> }
      try {
        file = await build(`items/${itemId}.json`) as typeof file
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null
        throw e
      }
      const r = file.variants[`${race}-${sex}-${models}`]
      if (!r || r.error) return null
      // An item with a model but no ItemSparse row goes by the name the catalog made for it (as the API does).
      const listed = (await catalog()).find((i) => i.itemId === itemId)
      return listed?.unnamed ? { ...r, name: listed.name } : r
    },

    async searchItems(query) {
      const q = { q: query.q, slots: list(query.slot), qualities: list(query.quality).map(Number), limit: query.limit, offset: query.offset, includeUnnamed: query.unnamed === 1 }
      return searchItemsIn(await catalog(), q).items
    },

    async searchSets(query) {
      return searchSetsIn(await sets(), query).sets.map(toSetResult)
    },

    async getNotableSets(race, classId): Promise<SetGroup[]> {
      const byRace = (await build('notable.json') as NotableFile)[String(race)]
      if (!byRace) throw new ApiError(400, 'bad_request', 'race must be a playable race ID from /characters.')
      return (byRace[classId ? String(classId) : 'all'] ?? []).map((g) => ({ group: g.group, sets: g.sets.map(toSetResult) as ItemSetResult[] }))
    },

    async getSetPieces() {
      return build('set-pieces.json') as Promise<number[]>
    },

    // titles.json and names.json are optional; without them the lists are empty.
    async getTitles() {
      return orMissing(build('titles.json') as Promise<TitleInfo[]>, [])
    },

    async getNames() {
      return orMissing(build('names.json') as Promise<Record<string, RaceNames>>, {})
    },

    modelUrl: (b, fdid, ext) => assetUrl(b, `models/${fdid}.${ext}`),
    animUrl: (b, model, anim) => assetUrl(b, `anims/${model}/${anim}.glb`),
    textureUrl: (b, fdid) => assetUrl(b, `textures/${fdid}.png`),
  }
}
