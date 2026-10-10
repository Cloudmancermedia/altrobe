import { useEffect, useState } from 'react'
import { ApiError, getNotableSets, getSetPieces, searchSets, textureUrl } from '../api/client'
import type { ItemSearchResult, ItemSetResult, SetGroup } from '../api/types'
import { commands, store } from '../app-state'
import { replacedSetSlots, setPieceSlots } from '../commands'
import { classChoices } from '../identity/identity'
import { QUALITY_NAMES, SLOT_LABELS } from '../labels'
import type { Look } from '../look/look'
import { rememberItems, setNotices, useStore } from '../store'
import { otherSlots, SLOT_ORDER, type SlotName } from '../viewer/dress'
import { SEARCH_HINT, searchText } from './search-query'
import { readSetPrefs, setQuality, type SetPrefs, withoutNotable, writeSetPrefs } from './sets'

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
  // A slot clicked on the character screen switches to item search for that slot.
  const findSlot = useStore(store, (s) => s.findSlot)
  const [seenFind, setSeenFind] = useState(findSlot?.n)
  if (findSlot && findSlot.n !== seenFind) { setSeenFind(findSlot.n); setMode('items') }
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
  const [groups, setGroups] = useState<SetGroup[]>([])
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')
  const [prefs, setPrefsState] = useState(() => readSetPrefs(storage()))
  const race = useStore(store, (s) => s.look.race)
  const characters = useStore(store, (s) => s.characters)
  const anyClass = useStore(store, (s) => s.anyClass)
  const classes = classChoices(characters, race, anyClass)
  // The character's class (Identity panel) is the filter: one choice for both panels.
  const classId = useStore(store, (s) => s.look.identity?.classId ?? 0)
  const browsing = q.trim() === '' && prefs.notableFirst
  const setPrefs = (next: SetPrefs) => { setPrefsState(next); writeSetPrefs(storage(), next) }

  useEffect(() => {
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      setState('loading')
      const notable = browsing ? getNotableSets(race, classId || undefined) : Promise.resolve([])
      Promise.all([searchSets({ q: q.trim(), limit: 30 }, ctrl.signal), notable]).then(([r, g]) => {
        if (ctrl.signal.aborted) return
        setSets(r)
        setGroups(g)
        setState('idle')
      }, (e) => {
        if (ctrl.signal.aborted) return
        setState('error')
        setNotices(store, 'search', [`set search failed: ${e instanceof ApiError ? e.message : String(e)}`])
      })
    }, 250)
    return () => { clearTimeout(t); ctrl.abort() }
  }, [q, browsing, race, classId])

  const equip = async (s: ItemSetResult) => {
    rememberItems(store, s.pieces.map((p) => ({ itemId: p.itemId, name: p.name, slot: p.slot, quality: p.quality, iconFileDataId: p.iconFileDataId })))
    const pieces = s.pieces.map((p) => ({ slot: p.slot, itemId: p.itemId }))
    let clear: string[] = []
    if (prefs.replace) {
      try { clear = replacedSetSlots(store.get().look.items, pieces, new Set(await getSetPieces())) } catch { /* equip without replacing */ }
    }
    commands.equip_items(pieces, clear)
  }
  const others = browsing ? withoutNotable(sets, groups) : sets
  return (
    <>
      <div className="search-controls">
        <input type="search" placeholder="Set name, piece name or set ID" aria-label="Search sets" value={q} onChange={(e) => setQ(e.target.value)} />
        <select aria-label="Class" value={classId} onChange={(e) => commands.set_identity({ classId: Number(e.target.value) || null })} disabled={!browsing}>
          <option value={0}>All classes</option>
          {classes.map((c) => <option key={c.classId} value={c.classId}>{c.name}</option>)}
        </select>
      </div>
      <div className="set-options small">
        <label><input type="checkbox" checked={prefs.notableFirst} onChange={(e) => setPrefs({ ...prefs, notableFirst: e.target.checked })} /> Notable sets first</label>
        <label><input type="checkbox" checked={prefs.replace} onChange={(e) => setPrefs({ ...prefs, replace: e.target.checked })} /> Replace the previous set</label>
      </div>
      <div aria-live="polite" aria-busy={state === 'loading'}>
        {browsing && groups.map((g) => (
          <div key={g.group}>
            <h3 className="result-group">{g.group}</h3>
            <SetList build={build} sets={g.sets} onPick={equip} />
          </div>
        ))}
        {browsing && groups.length > 0 && others.length > 0 && <h3 className="result-group">All other sets</h3>}
        <SetList build={build} sets={others} onPick={equip} />
      </div>
      {state === 'idle' && sets.length === 0 && groups.length === 0 && <p className="muted small">No sets match.</p>}
      {state === 'loading' && <p className="muted small">Searching…</p>}
    </>
  )
}

function SetList({ build, sets, onPick }: { build: string; sets: ItemSetResult[]; onPick: (s: ItemSetResult) => void }) {
  const quality = setQuality
  return (
    <ul className="results">
      {sets.map((s) => (
        <li key={s.setId}>
          <button type="button" className="result" onClick={() => onPick(s)} title={s.pieces.map((p) => p.name).join('\n')}>
            <ItemIcon build={build} fileDataId={s.pieces[0]?.iconFileDataId} quality={quality(s)} />
            <span className="result-text">
              <span className={`qname q${quality(s)}`}>{s.name}</span>
              <span className="muted small">{s.pieces.length} pieces · set {s.setId}{s.unnamed ? ' · pieces unnamed in this build' : ''}{s.internal ? ' · dev set' : ''}</span>
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}

// localStorage, or null where the browser blocks it.
function storage(): Storage | null {
  try { return window.localStorage } catch { return null }
}

function SingleItemSearch({ build }: { build: string }) {
  const [q, setQ] = useState('')
  const [slot, setSlot] = useState('')
  const [quality, setQuality] = useState('')
  // Items the game files and cmangos leave unnamed (Season of Discovery-era IDs) stay out unless asked for.
  const [unnamed, setUnnamed] = useState(false)
  const [results, setResults] = useState<ItemSearchResult[]>([])
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')
  const [more, setMore] = useState(false)
  const [offset, setOffset] = useState(0)
  // A slot clicked on the character screen becomes the slot filter.
  const findSlot = useStore(store, (s) => s.findSlot)
  const [seenFind, setSeenFind] = useState(findSlot?.n)
  if (findSlot && findSlot.n !== seenFind) { setSeenFind(findSlot.n); setOffset(0); setSlot(findSlot.slot) }

  const text = searchText(q, { slot, quality })

  useEffect(() => {
    if (text === null) return
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      setState('loading')
      commands.search_items({ q: text, slot, quality: quality === '' ? undefined : Number(quality), limit: PAGE + 1, offset, ...(unnamed ? { unnamed: 1 as const } : {}) })
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
  }, [text, slot, quality, offset, unnamed])

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
      <div className="set-options small">
        <label><input type="checkbox" checked={unnamed} onChange={(e) => reset(setUnnamed)(e.target.checked)} /> Show unnamed items</label>
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
            {otherSlots(r).map((slot) => (
              <button key={slot} type="button" className="link small" onClick={() => commands.equip_item(slot, r.itemId)}
                title={`Equip ${r.name} in ${SLOT_LABELS[slot]}`}>
                {SLOT_LABELS[slot]}
              </button>
            ))}
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

const WEAPON_SLOTS: SlotName[] = ['mainhand', 'offhand']

/** Quick ways back to a bare character: set pieces, weapons, the animation, or all of them. */
export function ClearButtons({ look }: { look: Look }) {
  const worn = Object.keys(look.items).length > 0
  const clearSetPieces = async () => {
    try {
      commands.unequip_slots(setPieceSlots(store.get().look.items, new Set(await getSetPieces())))
    } catch (e) {
      setNotices(store, 'search', [`couldn't load the set pieces: ${e instanceof ApiError ? e.message : String(e)}`])
    }
  }
  return (
    <div className="clear-buttons">
      <button type="button" disabled={!worn} onClick={clearSetPieces}>Clear set pieces</button>
      <button type="button" disabled={!WEAPON_SLOTS.some((s) => look.items[s])} onClick={() => commands.unequip_slots(WEAPON_SLOTS)}>Clear weapons</button>
      <button type="button" disabled={!look.anim} onClick={() => commands.set_animation('Stand')}>Clear animation</button>
      <button type="button" disabled={!worn && !look.anim} onClick={() => commands.clear_all()}>Clear all</button>
    </div>
  )
}
