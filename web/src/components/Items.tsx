import { useEffect, useState } from 'react'
import { ApiError, searchSets, textureUrl } from '../api/client'
import type { ItemSearchResult, ItemSetResult } from '../api/types'
import { commands, store } from '../app-state'
import { QUALITY_NAMES, SLOT_LABELS } from '../labels'
import type { Look } from '../look/look'
import { rememberItems, setNotices, useStore } from '../store'
import { SLOT_ORDER, type SlotName } from '../viewer/dress'
import { SEARCH_HINT, searchText } from './search-query'

const PAGE = 50

export function ItemIcon({ build, fileDataId, quality }: { build: string; fileDataId?: number; quality?: number }) {
  const [failed, setFailed] = useState(false)
  const cls = `icon q${quality ?? 1}`
  // Icons are ordinary converted textures; an install that has not converted one gets a blank tile.
  if (!fileDataId || failed) return <span className={`${cls} blank`} aria-hidden="true" />
  return <img className={cls} src={textureUrl(build, fileDataId)} alt="" width={32} height={32} loading="lazy" onError={() => setFailed(true)} />
}

/** The Find panel: single items, or whole sets. */
export function ItemSearch({ build }: { build: string }) {
  const [mode, setMode] = useState<'items' | 'sets'>('items')
  return (
    <section className="panel search" aria-labelledby="search-heading">
      <h2 id="search-heading">Find {mode}</h2>
      <div className="segmented find-mode" role="radiogroup" aria-label="Find">
        {(['items', 'sets'] as const).map((m) => (
          <button key={m} type="button" role="radio" aria-checked={mode === m} onClick={() => setMode(m)}>{m}</button>
        ))}
      </div>
      {mode === 'items' ? <SingleItemSearch build={build} /> : <SetSearch build={build} />}
    </section>
  )
}

function SetSearch({ build }: { build: string }) {
  const [q, setQ] = useState('')
  const [sets, setSets] = useState<ItemSetResult[]>([])
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')
  useEffect(() => {
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      setState('loading')
      searchSets({ q: q.trim(), limit: 30 }, ctrl.signal).then((r) => {
        if (ctrl.signal.aborted) return
        setSets(r)
        setState('idle')
      }, (e) => {
        if (ctrl.signal.aborted) return
        setState('error')
        setNotices(store, 'search', [`set search failed: ${e instanceof ApiError ? e.message : String(e)}`])
      })
    }, 250)
    return () => { clearTimeout(t); ctrl.abort() }
  }, [q])

  const equip = (s: ItemSetResult) => {
    rememberItems(store, s.pieces.map((p) => ({ itemId: p.itemId, name: p.name, slot: p.slot, quality: p.quality, iconFileDataId: p.iconFileDataId })))
    commands.equip_items(s.pieces.map((p) => ({ slot: p.slot, itemId: p.itemId })))
  }
  const quality = (s: ItemSetResult) => Math.max(...s.pieces.map((p) => p.quality))
  return (
    <>
      <div className="search-controls">
        <input type="search" placeholder="Set name, piece name or set ID" aria-label="Search sets" value={q} onChange={(e) => setQ(e.target.value)} />
      </div>
      <ul className="results" aria-live="polite" aria-busy={state === 'loading'}>
        {sets.map((s) => (
          <li key={s.setId}>
            <button type="button" className="result" onClick={() => equip(s)} title={s.pieces.map((p) => p.name).join('\n')}>
              <ItemIcon build={build} fileDataId={s.pieces[0]?.iconFileDataId} quality={quality(s)} />
              <span className="result-text">
                <span className={`qname q${quality(s)}`}>{s.name}</span>
                <span className="muted small">{s.pieces.length} pieces · set {s.setId}{s.unnamed ? ' · pieces unnamed in this build' : ''}{s.internal ? ' · dev set' : ''}</span>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {state === 'idle' && sets.length === 0 && <p className="muted small">No sets match.</p>}
      {state === 'loading' && <p className="muted small">Searching…</p>}
    </>
  )
}

function SingleItemSearch({ build }: { build: string }) {
  const [q, setQ] = useState('')
  const [slot, setSlot] = useState('')
  const [quality, setQuality] = useState('')
  const [results, setResults] = useState<ItemSearchResult[]>([])
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')
  const [more, setMore] = useState(false)
  const [offset, setOffset] = useState(0)

  const text = searchText(q, { slot, quality })

  useEffect(() => {
    if (text === null) return
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      setState('loading')
      commands.search_items({ q: text, slot, quality: quality === '' ? undefined : Number(quality), limit: PAGE + 1, offset })
        .then((r) => {
          if (ctrl.signal.aborted) return
          rememberItems(store, r)
          setMore(r.length > PAGE)
          setResults((prev) => (offset === 0 ? r.slice(0, PAGE) : [...prev, ...r.slice(0, PAGE)]))
          setState('idle')
        }, (e) => {
          if (ctrl.signal.aborted) return
          setState('error')
          setNotices(store, 'search', [`item search failed: ${e instanceof ApiError ? e.message : String(e)}`])
        })
    }, 250)
    return () => { clearTimeout(t); ctrl.abort() }
  }, [text, slot, quality, offset])

  // Results from an earlier query stay in state; with no query nothing is shown.
  const shown = text === null ? [] : results
  const busy = text !== null && state === 'loading'
  const reset = <T,>(set: (v: T) => void) => (v: T) => { setOffset(0); set(v) }

  return (
    <>
      <div className="search-controls">
        <input type="search" placeholder="Search by name or ID" aria-label="Search items by name or ID" value={q} onChange={(e) => reset(setQ)(e.target.value)} />
        <select aria-label="Slot" value={slot} onChange={(e) => reset(setSlot)(e.target.value)}>
          <option value="">Any slot</option>
          {SLOT_ORDER.map((s) => <option key={s} value={s}>{SLOT_LABELS[s]}</option>)}
        </select>
        <select aria-label="Quality" value={quality} onChange={(e) => reset(setQuality)(e.target.value)}>
          <option value="">Any quality</option>
          {QUALITY_NAMES.map((n, i) => <option key={n} value={i}>{n}</option>)}
        </select>
      </div>
      <ul className="results" aria-live="polite" aria-busy={busy}>
        {shown.map((r) => (
          <li key={r.itemId}>
            <button type="button" className="result" onClick={() => commands.equip_item(r.slot, r.itemId)}
              title={`Equip in ${SLOT_LABELS[r.slot as SlotName] ?? r.slot}`}>
              <ItemIcon build={build} fileDataId={r.iconFileDataId} quality={r.quality} />
              <span className="result-text">
                <span className={`qname q${r.quality}`}>{r.name}</span>
                <span className="muted small">{SLOT_LABELS[r.slot as SlotName] ?? r.slot} · {r.itemId}{r.unnamed ? ' · unnamed in this build' : ''}{r.internal ? ' · dev or NPC item' : ''}</span>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {text === null && <p className="muted small">{SEARCH_HINT}</p>}
      {text !== null && state === 'idle' && results.length === 0 && <p className="muted small">No items match.</p>}
      {busy && <p className="muted small">Searching…</p>}
      {text !== null && more && !busy && <button type="button" onClick={() => setOffset(offset + PAGE)}>Show more</button>}
    </>
  )
}

export function SlotPanel({ look, build }: { look: Look; build: string }) {
  const info = useStore(store, (s) => s.itemInfo)
  const equipped = SLOT_ORDER.filter((s) => look.items[s])
  return (
    <section className="panel" aria-labelledby="slots-heading">
      <h2 id="slots-heading">Equipped</h2>
      {equipped.length === 0 && <p className="muted small">Nothing equipped. Pick items from the search below.</p>}
      <ul className="slots">
        {equipped.map((slot) => {
          const id = look.items[slot]!
          const i = info[id]
          const hidden = look.hide.includes(slot)
          return (
            <li key={slot} className={hidden ? 'hidden-slot' : undefined}>
              <ItemIcon build={build} fileDataId={i?.iconFileDataId} quality={i?.quality} />
              <span className="result-text">
                <span className={`qname q${i?.quality ?? 1}`}>{i?.name ?? `Item ${id}`}</span>
                <span className="muted small">{SLOT_LABELS[slot]}{hidden ? ' · hidden' : ''}</span>
              </span>
              <button type="button" aria-pressed={hidden} onClick={() => commands.set_visibility(slot, hidden)}
                aria-label={`${hidden ? 'Show' : 'Hide'} ${SLOT_LABELS[slot]}`}>{hidden ? 'Show' : 'Hide'}</button>
              <button type="button" onClick={() => commands.unequip(slot)} aria-label={`Clear ${SLOT_LABELS[slot]}`}>Clear</button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
