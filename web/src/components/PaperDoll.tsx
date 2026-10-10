import { useEffect, type ReactNode } from 'react'
import { searchItems } from '../api/client'
import { commands, store } from '../app-state'
import type { Look } from '../look/look'
import { rememberItems, useStore } from '../store'
import { isSlotName } from '../viewer/dress'
import { ItemIcon, ClearButtons } from './Items'
import { DOLL, type DollSlot } from './doll-layout'
import { SLOT_ART } from './slot-art'

/** The character screen: gear slots around the 3D view. */
export function PaperDoll({ look, build, children }: { look: Look; build: string; children: ReactNode }) {
  // Items equipped from a share link or a set come without icon and quality; an exact-ID search has them.
  const info = useStore(store, (st) => st.itemInfo)
  const missing = Object.values(look.items).filter((id): id is number => !!id && info[id]?.quality === undefined).sort().join()
  useEffect(() => {
    for (const id of missing ? missing.split(',') : []) {
      searchItems({ q: id, limit: 1, unnamed: 1 }).then((r) => rememberItems(store, r.filter((i) => String(i.itemId) === id)), () => {})
    }
  }, [missing])
  return (
    <div className="doll">
      <div className="doll-column" aria-label="Armor">{DOLL.left.map((s) => <Slot key={s.slot} s={s} look={look} build={build} />)}</div>
      <div className="doll-view">{children}</div>
      <div className="doll-column" aria-label="Accessories">{DOLL.right.map((s) => <Slot key={s.slot} s={s} look={look} build={build} />)}</div>
      <div className="doll-bottom">
        <div className="doll-weapons" aria-label="Weapons">
          {DOLL.bottom.map((s) => <Slot key={s.slot} s={s} look={look} build={build} />)}
          <button type="button" className="sheathe" aria-pressed={!!look.sheathed} onClick={() => commands.set_sheathed(!look.sheathed)}
            title={look.sheathed ? 'Take weapons out' : 'Put weapons away: on the back or hip (a ranged weapon is not shown)'}>
            {look.sheathed ? 'Draw weapons' : 'Sheathe weapons'}
          </button>
        </div>
        <ClearButtons look={look} />
      </div>
    </div>
  )
}

function Slot({ s, look, build }: { s: DollSlot; look: Look; build: string }) {
  const info = useStore(store, (st) => st.itemInfo)
  if (!s.equippable || !isSlotName(s.slot)) {
    return <div className="doll-slot dim" title={`${s.label}: not shown on the character`}><Empty slot={s.slot} /></div>
  }
  const slot = s.slot
  const id = look.items[slot]
  const item = id ? info[id] : undefined
  const hidden = look.hide.includes(slot)
  const name = id ? item?.name ?? `Item ${id}` : `${s.label}: empty`
  const find = () => store.set((st) => ({ findSlot: { slot, n: (st.findSlot?.n ?? 0) + 1 } }))
  return (
    <div className={`doll-slot${hidden ? ' hidden-slot' : ''}${s.drawn ? '' : ' undrawn'}`}>
      <button type="button" className="doll-pick" onClick={find} title={`${name}${s.drawn ? '' : ' (not shown on the character)'}. Click to find ${s.label.toLowerCase()} items.`}
        aria-label={id ? `${s.label}: ${name}. Find ${s.label} items` : `Find ${s.label} items`}>
        {id ? <ItemIcon build={build} fileDataId={item?.iconFileDataId} quality={item?.quality} /> : <Empty slot={s.slot} />}
      </button>
      {id && (
        <span className="doll-actions">
          <span className={`qname q${item?.quality ?? 1}`}>{name}</span>
          <span className="muted small">{s.label}{hidden ? ' · hidden' : ''}</span>
          <span className="doll-buttons">
          <button type="button" className="link small" aria-pressed={hidden} onClick={() => commands.set_visibility(slot, hidden)}>{hidden ? 'Show' : 'Hide'}</button>
          <button type="button" className="link small" onClick={() => commands.unequip(slot)} aria-label={`Clear ${s.label}`}>Clear</button>
          </span>
        </span>
      )}
    </div>
  )
}

/** An empty slot: the slot's silhouette, faint on a dark bevelled tile. */
function Empty({ slot }: { slot: DollSlot['slot'] }) {
  return (
    <span className="icon empty" aria-hidden="true">
      {SLOT_ART[slot] && <svg viewBox="0 0 24 24"><path d={SLOT_ART[slot]} /></svg>}
    </span>
  )
}
