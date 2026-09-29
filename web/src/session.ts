// The command channel from the local server (/api/v1/session). The server's MCP tools send look
// commands here, and this tab runs them through the same command API as its buttons, so a command
// from Claude updates the 3D view live. See importer/src/Altrobe.Server/TabSession.cs.

import type { ModelSet } from './api/types'
import type { Commands } from './commands'
import { characterLabel } from './labels'
import type { View } from './look/look'
import { setNotices, type AppState, type Store } from './store'

/** Close code the server uses when a newer tab takes over (TabSession.Replaced). */
export const REPLACED = 4001

export interface RemoteReply { ok: boolean; result?: unknown; error?: string }

/** What a model sees of the look: names as well as IDs. `full` adds every choice of every option. */
export function describeLook(s: AppState, full = true) {
  const { look } = s
  const choiceName = (name: string, i: number) => name || `#${i + 1}`
  return {
    character: characterLabel(s.characters, look.race, look.sex, look.models),
    race: look.race, sex: look.sex, models: look.models,
    items: Object.fromEntries(Object.entries(look.items).map(([slot, id]) => [slot, { itemId: id, name: s.itemInfo[id as number]?.name ?? null }])),
    hidden: look.hide,
    view: look.cam.view,
    compare: look.compare.map((c) => characterLabel(s.characters, c.race, c.sex, c.models)),
    customization: s.options.map((o) => {
      const current = look.custom[String(o.optionId)] ?? o.defaultChoiceId
      const choices = o.choices.map((c, i) => ({ choiceId: c.choiceId, name: choiceName(c.name, i) }))
      const picked = choices.find((c) => c.choiceId === current) ?? null
      return full ? { optionId: o.optionId, name: o.name, current: picked, choices } : { optionId: o.optionId, name: o.name, current: picked }
    }),
    notices: Object.values(s.notices).flat(),
  }
}

type Args = Record<string, unknown>
const str = (a: Args, k: string) => a[k] as string
const num = (a: Args, k: string) => a[k] as number

/** Runs one command from the server. Only the commands listed here can be called. */
export async function runRemoteCommand(cmd: Commands, store: Store<AppState>, command: string, args: Args = {}): Promise<RemoteReply> {
  const change: Record<string, () => { error?: string }> = {
    equip_item: () => cmd.equip_item(str(args, 'slot'), num(args, 'itemId')),
    unequip: () => cmd.unequip(str(args, 'slot')),
    set_character: () => cmd.set_character(num(args, 'race'), num(args, 'sex'), (args.models ?? undefined) as ModelSet | undefined),
    set_customization: () => cmd.set_customization(num(args, 'optionId'), num(args, 'choiceId')),
    randomize_customization: () => cmd.randomize_customization(),
    reset_customization: () => cmd.reset_customization(),
    compare: () => cmd.compare((args.characters ?? []) as { race: number; sex: number; models?: ModelSet }[]),
    set_visibility: () => cmd.set_visibility(str(args, 'slot'), args.visible as boolean),
    set_view: () => cmd.set_view(str(args, 'view') as View),
  }
  if (command === 'get_look') return { ok: true, result: describeLook(store.get()) }
  if (command === 'share_link') return { ok: true, result: { link: cmd.share_link() } }
  const run = change[command]
  if (!run) return { ok: false, error: `unknown command "${command}"` }
  const r = run()
  if (r.error !== undefined) return { ok: false, error: r.error }
  // Changes answer with the current choices only; get_look lists them all.
  return { ok: true, result: describeLook(store.get(), false) }
}

/** Keeps a connection to the server's session open, reconnecting after it drops. Returns a stop function. */
export function connectSession(cmd: Commands, store: Store<AppState>, url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/api/v1/session`) {
  let ws: WebSocket | null = null
  let stopped = false
  let delay = 1000
  let timer: ReturnType<typeof setTimeout> | undefined
  const open = () => {
    if (stopped) return
    ws = new WebSocket(url)
    ws.onopen = () => { delay = 1000; setNotices(store, 'session', []) }
    ws.onmessage = async (e) => {
      let msg: { type?: string; id?: string; command?: string; args?: Args }
      try { msg = JSON.parse(String(e.data)) } catch { return }
      if (msg.type !== 'command' || !msg.id || !msg.command) return
      let reply: RemoteReply
      try { reply = await runRemoteCommand(cmd, store, msg.command, msg.args ?? {}) } catch (err) { reply = { ok: false, error: (err as Error).message } }
      ws?.send(JSON.stringify({ type: 'result', id: msg.id, ...reply }))
    }
    // The server stopped: try again later, backing off to 30 s. A newer tab taking over is final,
    // or two open tabs would keep taking the connection from each other.
    ws.onclose = (e) => {
      if (stopped) return
      if (e.code === REPLACED) {
        setNotices(store, 'session', ['Commands from Claude now go to another Altrobe tab. Reload this tab to take them back.'])
        return
      }
      timer = setTimeout(open, delay)
      delay = Math.min(delay * 2, 30_000)
    }
  }
  open()
  return () => { stopped = true; clearTimeout(timer); ws?.close() }
}
