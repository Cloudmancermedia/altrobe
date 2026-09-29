// DEV ONLY. A Vite middleware that answers the /api/v1 and /assets contract from the Phase 1 spike's
// converted files, so the web app runs without the real server. Enabled only by `vite dev` with
// ALTROBE_DEV_ADAPTER=spike; it never runs in, or ships with, a production build.
//
// It reads from ALTROBE_SPIKE_OUTPUT (the spike's output/ folder) at request time and copies nothing
// into this repository. It covers what the spike converted: the Orc male and Undead female base looks
// (HD and SD), and resolved data for the spike's handful of items. Everything else answers 404, as
// the real server would for an item it cannot resolve.
//
// Set ALTROBE_DEV_NO_INSTALL=1 to start with no install selected, to try the install picker.

import { createReadStream, existsSync, readFileSync, readdirSync } from 'node:fs'
import type { IncomingMessage, ServerResponse } from 'node:http'
import { join } from 'node:path'
import type { Plugin } from 'vite'

type Row = Record<string, any> // eslint-disable-line @typescript-eslint/no-explicit-any

// The characters the spike has files for: race and sex -> output/looks/<name>[-sd].json and
// output/resolved/<name>/.
const SPIKE_CHARACTERS: Record<string, string> = { '2:0': 'orc-male', '5:1': 'undead-female' }

// Inventory type -> look slot name. Same mapping as src/viewer/dress.ts; repeated here so this Node
// module does not import browser source.
const SLOT_FOR_INVENTORY_TYPE: Record<number, string> = {
  1: 'head', 2: 'neck', 3: 'shoulder', 4: 'shirt', 5: 'chest', 6: 'waist', 7: 'legs', 8: 'feet', 9: 'wrist', 10: 'hands',
  13: 'mainhand', 14: 'offhand', 15: 'mainhand', 16: 'back', 17: 'mainhand', 19: 'tabard', 20: 'chest', 21: 'mainhand',
  22: 'offhand', 23: 'offhand', 26: 'mainhand',
}

export function spikeAdapter({ outputDir }: { outputDir: string }): Plugin {
  if (!existsSync(join(outputDir, 'looks'))) throw new Error(`ALTROBE_SPIKE_OUTPUT=${outputDir} has no looks/ folder`)
  const tables = new Map<string, { rows: Row[]; version: string; product: string }>()
  const table = (name: string) => {
    if (!tables.has(name)) tables.set(name, JSON.parse(readFileSync(join(outputDir, 'tables', `${name}.json`), 'utf8')))
    return tables.get(name)!
  }
  const build = table('ChrCustomization').version
  const product = table('ChrCustomization').product
  let active: { path: string; product: string; build: string } | null =
    process.env.ALTROBE_DEV_NO_INSTALL === '1' ? null : { path: outputDir, product, build }

  const lookFile = (race: number, sex: number, models: string) => {
    const name = SPIKE_CHARACTERS[`${race}:${sex}`]
    const file = name && join(outputDir, 'looks', `${name}${models === 'sd' ? '-sd' : ''}.json`)
    return file && existsSync(file) ? file : null
  }

  // Search index, built on first use: every item that goes in a slot, the spike's resolved items first.
  let index: { itemId: number; name: string; lower: string; slot: string; quality: number; iconFileDataId: number; resolved: boolean }[] | null = null
  const searchIndex = () => {
    if (index) return index
    const icons = new Map(table('Item').rows.map((r) => [r.ID, r.IconFileDataID]))
    const resolved = new Set(Object.values(SPIKE_CHARACTERS).flatMap((n) => {
      try { return resolvedItemIds(join(outputDir, 'resolved', n)) } catch { return [] }
    }))
    index = table('ItemSparse').rows
      .filter((r) => SLOT_FOR_INVENTORY_TYPE[r.InventoryType] && r.Display_lang)
      .map((r) => ({
        itemId: r.ID, name: r.Display_lang, lower: String(r.Display_lang).toLowerCase(), slot: SLOT_FOR_INVENTORY_TYPE[r.InventoryType],
        quality: r.OverallQualityID, iconFileDataId: icons.get(r.ID) ?? 0, resolved: resolved.has(r.ID),
      }))
      .sort((a, b) => Number(b.resolved) - Number(a.resolved) || a.itemId - b.itemId)
    return index
  }

  // Selectable choices per option, filtered like the spike's tools/looks: a fresh, non-Death-Knight
  // character of the look's class, overrideArchive 0. Proposed contract field "options".
  const optionsFor = (look: Row) => {
    const req = new Map(table('ChrCustomizationReq').rows.map((r) => [r.ID, r]))
    const classId = look.classId ?? 1
    const ok = (reqId: number) => {
      if (!reqId) return true
      const r = req.get(reqId)
      if (!r) return false
      if (r.ReqType & 4 && !(r.ReqType & 2)) return false
      if (r.ReqType & 1 && !(r.ClassMask & (1 << (classId - 1)))) return false
      if (r.OverrideArchive !== -1 && r.OverrideArchive !== 0) return false
      return !(r.ReqAchievementID || r.ReqQuestID || r.ReqItemModifiedAppearanceID)
    }
    const byOption = new Map<number, Row[]>()
    for (const c of table('ChrCustomizationChoice').rows) {
      if (!byOption.has(c.ChrCustomizationOptionID)) byOption.set(c.ChrCustomizationOptionID, [])
      byOption.get(c.ChrCustomizationOptionID)!.push(c)
    }
    return (look.choices as Row[]).map((d) => ({
      optionId: d.optionId,
      name: d.option,
      defaultChoiceId: d.choiceId,
      choices: (byOption.get(d.optionId) ?? [])
        .filter((c) => ok(c.ChrCustomizationReqID) || c.ID === d.choiceId)
        .sort((a, b) => a.OrderIndex - b.OrderIndex || a.ID - b.ID)
        .map((c) => ({
          choiceId: c.ID, name: c.Name_lang,
          ...(c.SwatchColor?.[0] ? { swatch: `#${((c.SwatchColor[0] >>> 0) & 0xffffff).toString(16).padStart(6, '0')}` } : {}),
        })),
    }))
  }

  const characters = () => {
    const classes = new Map(table('ChrClasses').rows.map((r) => [r.ID, r.Name_lang]))
    const races = new Map(table('ChrRaces').rows.map((r) => [r.ID, r]))
    const byRace = new Map<number, number[]>()
    for (const r of table('CharBaseInfo').rows) {
      if (!byRace.has(r.RaceID)) byRace.set(r.RaceID, [])
      byRace.get(r.RaceID)!.push(r.ClassID)
    }
    const sexesOf = (race: number) => [...new Set(table('ChrRaceXChrModel').rows.filter((x) => x.ChrRacesID === race).map((x) => x.Sex as number))].sort()
    return {
      build,
      races: [...byRace].sort(([a], [b]) => a - b).map(([race, classIds]) => ({
        race,
        name: races.get(race)?.Name_lang ?? `Race ${race}`,
        classes: [...new Set(classIds)].sort((a, b) => a - b).map((classId) => ({ classId, name: classes.get(classId) ?? `Class ${classId}` })),
        // Availability here means "the spike converted this body", not what the game has.
        sexes: sexesOf(race).map((sex) => ({ sex, hd: !!lookFile(race, sex, 'hd'), sd: !!lookFile(race, sex, 'sd') })),
      })),
    }
  }

  async function handle(req: IncomingMessage, res: ServerResponse): Promise<boolean> {
    const url = new URL(req.url ?? '/', 'http://localhost')
    const p = url.pathname
    const q = url.searchParams
    if (!p.startsWith('/api/v1/') && !p.startsWith('/assets/')) return false

    if (p === '/api/v1/status' && req.method === 'GET') {
      return json(res, 200, { version: '0.0.0-dev (spike adapter)', installs: [{ path: outputDir, products: [{ product, build }] }], active, cacheFolder: outputDir })
    }
    if (p === '/api/v1/install' && req.method === 'POST') {
      const body = JSON.parse((await readBody(req)) || '{}')
      if (body.product !== product) return error(res, 400, 'unknown_product', `no product "${body.product}" in this install`)
      if (body.path && body.path !== outputDir) return error(res, 404, 'install_not_found', `no install at ${body.path}`)
      active = { path: outputDir, product, build }
      return json(res, 200, { ok: true })
    }
    if (!active) return error(res, 409, 'no_install', 'No game install selected. POST /api/v1/install first.')

    let m: RegExpExecArray | null
    if (p === '/api/v1/characters') return json(res, 200, characters())
    if ((m = /^\/api\/v1\/characters\/(\d+)\/(\d+)$/.exec(p))) {
      const models = q.get('models') ?? 'hd'
      if (models !== 'hd' && models !== 'sd') return error(res, 400, 'bad_request', 'models must be hd or sd')
      const file = lookFile(Number(m[1]), Number(m[2]), models)
      if (!file) return error(res, 404, 'not_found', `no ${models} base look for race ${m[1]} sex ${m[2]}`)
      const look = JSON.parse(readFileSync(file, 'utf8'))
      return json(res, 200, { ...look, options: optionsFor(look) })
    }
    if (p === '/api/v1/items/search') {
      const text = (q.get('q') ?? '').trim().toLowerCase()
      const slot = q.get('slot') || null
      const quality = q.get('quality') ? Number(q.get('quality')) : null
      const limit = Math.min(Number(q.get('limit') ?? 50) || 50, 200)
      const offset = Number(q.get('offset') ?? 0) || 0
      const hits = searchIndex().filter((i) => (!text || i.lower.includes(text) || String(i.itemId) === text) && (!slot || i.slot === slot) && (quality === null || i.quality === quality))
      return json(res, 200, hits.slice(offset, offset + limit).map(({ itemId, name, slot, quality, iconFileDataId }) => ({ itemId, name, slot, quality, iconFileDataId })))
    }
    if ((m = /^\/api\/v1\/items\/(\d+)\/resolved$/.exec(p))) {
      const name = SPIKE_CHARACTERS[`${q.get('race')}:${q.get('sex')}`]
      if (!q.get('race') || !q.get('sex')) return error(res, 400, 'bad_request', 'race and sex are required')
      const file = name && join(outputDir, 'resolved', name, `${m[1]}.json`)
      if (!file || !existsSync(file)) return error(res, 404, 'not_found', `no resolved data for item ${m[1]} on race ${q.get('race')} sex ${q.get('sex')}`)
      // The spike resolved items per character, not per model set, so hd and sd share a file.
      return json(res, 200, JSON.parse(readFileSync(file, 'utf8')))
    }
    if ((m = /^\/assets\/[^/]+\/(models|textures)\/(\d+)\.(glb|json|png)$/.exec(p))) {
      const [, kind, fdid, ext] = m
      if ((kind === 'textures') !== (ext === 'png')) return error(res, 404, 'not_found', p)
      const file = join(outputDir, kind, `${fdid}.${ext}`)
      if (!existsSync(file)) return error(res, 404, 'not_found', `${kind}/${fdid}.${ext} is not converted`)
      res.writeHead(200, { 'content-type': ext === 'glb' ? 'model/gltf-binary' : ext === 'png' ? 'image/png' : 'application/json', 'cache-control': 'no-cache' })
      createReadStream(file).pipe(res)
      return true
    }
    return error(res, 404, 'not_found', `no route ${req.method} ${p}`)
  }

  return {
    name: 'altrobe-spike-adapter',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        handle(req, res).then((done) => { if (!done) next() }, (e) => error(res, 500, 'internal', (e as Error).message))
      })
    },
  }
}

function resolvedItemIds(dir: string): number[] {
  // Resolved files are <itemId>.json; base.json is the body and is skipped.
  return readdirSync(dir).filter((f) => /^\d+\.json$/.test(f)).map((f) => Number(f.slice(0, -5)))
}

function json(res: ServerResponse, status: number, body: unknown): true {
  res.writeHead(status, { 'content-type': 'application/json' })
  res.end(JSON.stringify(body))
  return true
}
function error(res: ServerResponse, status: number, code: string, message: string): true {
  return json(res, status, { error: { code, message } })
}
function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    let s = ''
    req.on('data', (c) => { s += c })
    req.on('end', () => resolve(s))
    req.on('error', reject)
  })
}
