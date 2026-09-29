import { useRef, useState } from 'react'
import { getStatus, selectInstall } from '../api/client'
import type { CharactersResponse, Status } from '../api/types'
import { commands, store } from '../app-state'
import { characterLabel, pickCharacter } from '../labels'
import { MAX_COMPARE, canonicalJSON, type Look } from '../look/look'
import { setNotices } from '../store'
import { CharacterPicker } from './CharacterPanel'
import { installChoices } from './install-choice'

export function ComparePanel({ characters, look }: { characters: CharactersResponse; look: Look }) {
  const add = () => {
    // Start from the first race that has data and is not already on screen.
    const used = new Set([look, ...look.compare].map((c) => `${c.race}:${c.sex}`))
    for (const r of characters.races) for (const s of r.sexes) {
      if ((s.hd || s.sd) && !used.has(`${r.race}:${s.sex}`)) {
        commands.compare([...look.compare, { race: r.race, sex: s.sex, models: s.hd ? 'hd' : 'sd' }])
        return
      }
    }
    const p = pickCharacter(characters.races.find((r) => r.race === look.race)!, look.sex, look.models)
    if (p) commands.compare([...look.compare, { race: look.race, ...p }])
  }
  return (
    <section className="panel" aria-labelledby="compare-heading">
      <h2 id="compare-heading">Side by side</h2>
      <p className="muted small">The same outfit on up to {MAX_COMPARE + 1} characters. Extra characters use default customizations.</p>
      <ol className="compare-list">
        <li className="muted small">{characterLabel(characters, look.race, look.sex, look.models)} (main)</li>
        {look.compare.map((c, i) => (
          <li key={i}>
            <CharacterPicker characters={characters} race={c.race} sex={c.sex} models={c.models} idPrefix={`cmp${i}`}
              onChange={(race, sex, models) => commands.compare(look.compare.map((x, j) => (j === i ? { race, sex, models } : x)))} />
            <button type="button" onClick={() => commands.compare(look.compare.filter((_, j) => j !== i))}
              aria-label={`Remove ${characterLabel(characters, c.race, c.sex, c.models)}`}>Remove</button>
          </li>
        ))}
      </ol>
      <button type="button" onClick={add} disabled={look.compare.length >= MAX_COMPARE}>Add character</button>
    </section>
  )
}

export function SharePanel({ look, defaults }: { look: Look; defaults: Record<string, number> }) {
  const [copied, setCopied] = useState(false)
  const linkRef = useRef<HTMLInputElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const link = commands.share_link()

  const copy = async () => {
    try { await navigator.clipboard.writeText(link) } catch { linkRef.current?.select(); document.execCommand('copy') }
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }
  const save = () => {
    const a = document.createElement('a')
    a.href = URL.createObjectURL(new Blob([canonicalJSON(look, { defaults }) + '\n'], { type: 'application/json' }))
    a.download = 'altrobe-look.json'
    a.click()
    URL.revokeObjectURL(a.href)
  }
  const open = async (file: File | undefined) => {
    if (!file) return
    try { commands.open_look(JSON.parse(await file.text())) } catch (e) { setNotices(store, 'link', [`could not read ${file.name}: ${(e as Error).message}`]) }
    if (fileRef.current) fileRef.current.value = ''
  }
  return (
    <div className="share" role="group" aria-label="Share and save">
      <input ref={linkRef} className="share-link" readOnly value={link} aria-label="Share link" onFocus={(e) => e.target.select()} />
      <button type="button" onClick={copy}>{copied ? 'Copied' : 'Copy link'}</button>
      <button type="button" onClick={save}>Save look</button>
      <button type="button" onClick={() => fileRef.current?.click()}>Open look…</button>
      <input ref={fileRef} type="file" accept="application/json,.json" hidden onChange={(e) => open(e.target.files?.[0])} />
    </div>
  )
}

const SOURCE_LABELS: Record<string, string> = { link: 'Saved look', commands: '', search: '', install: '', app: '' }

export function Notices({ notices, labels }: { notices: Record<string, string[]>; labels: Record<string, string> }) {
  const [dismissed, setDismissed] = useState<Set<string>>(new Set())
  const list = Object.entries(notices).flatMap(([source, items]) => items.map((text) => ({ source, text, key: `${source}|${text}` })))
    .filter((n) => !dismissed.has(n.key))
  return (
    <div className="notices" role="status" aria-live="polite">
      {list.length > 0 && (
        <ul>
          {list.map((n) => {
            const label = labels[n.source] ?? SOURCE_LABELS[n.source] ?? n.source
            return (
              <li key={n.key}>
                <span>{label && <strong>{label}: </strong>}{n.text}</span>
                <button type="button" className="link" onClick={() => setDismissed(new Set([...dismissed, n.key]))} aria-label="Dismiss notice">Dismiss</button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

export function InstallPicker({ status, onSelected }: { status: Status; onSelected: (s: Status) => void }) {
  const choices = installChoices(status)
  const [pick, setPick] = useState(0)
  const [path, setPath] = useState('')
  const [product, setProduct] = useState(choices[0]?.product ?? '')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (body: { path?: string; product: string }) => {
    setBusy(true)
    setError('')
    try {
      await selectInstall(body)
      onSelected(await getStatus())
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }
  return (
    <section className="panel install" aria-labelledby="install-heading">
      <h2 id="install-heading">Choose a game install</h2>
      <p className="muted small">Altrobe reads items and models from a World of Warcraft: Forever install on this computer.</p>
      {choices.length > 0 && (
        <form onSubmit={(e) => { e.preventDefault(); const c = choices[pick]; submit({ path: c.path, product: c.product }) }}>
          <label htmlFor="install-found">Installs found</label>
          <select id="install-found" value={pick} onChange={(e) => setPick(Number(e.target.value))}>
            {choices.map((c, i) => <option key={i} value={i}>{c.label}</option>)}
          </select>
          <button type="submit" disabled={busy}>Use this install</button>
        </form>
      )}
      <form onSubmit={(e) => { e.preventDefault(); submit({ path: path.trim() || undefined, product }) }}>
        <label htmlFor="install-path">Or enter the install folder</label>
        <input id="install-path" value={path} onChange={(e) => setPath(e.target.value)} placeholder="/Applications/World of Warcraft" />
        <label htmlFor="install-product">Product</label>
        <input id="install-product" value={product} onChange={(e) => setProduct(e.target.value)} required />
        <button type="submit" disabled={busy}>Use this folder</button>
      </form>
      {error && <p className="error" role="alert">{error}</p>}
    </section>
  )
}
