import { useEffect, useState } from 'react'
import { getNames } from '../api/client'
import type { CharactersResponse, RaceNames, TitleInfo } from '../api/types'
import { commands, store } from '../app-state'
import { setAnyClass, useStore } from '../store'
import { useTitles } from '../hooks'
import { classChoices, titlesFor } from '../identity/identity'
import { generateName } from '../identity/names'
import { MAX_GUILD, MAX_LEVEL, MAX_NAME, type Look } from '../look/look'

/** Name, title, guild, level, class and PvP flag: who the character is, shown over its head and in its tooltip. */
export function IdentityPanel({ characters, look, pinTooltip, onPinTooltip }: {
  characters: CharactersResponse; look: Look; pinTooltip: boolean; onPinTooltip: (pin: boolean) => void
}) {
  const race = characters.races.find((r) => r.race === look.race)
  const titles = titlesFor(useTitles(), race?.faction)
  // A rank from the other faction (after a race change) can't be held, so it goes.
  const titleOk = !look.identity?.titleId || titles.length === 0 || titles.some((t) => t.titleId === look.identity!.titleId)
  useEffect(() => { if (!titleOk) commands.set_identity({ titleId: null }) }, [titleOk])
  const [names, setNames] = useState<Record<string, RaceNames>>({})
  useEffect(() => { getNames().then(setNames, () => setNames({})) }, [])
  // Where the generator is in its sequence. It starts somewhere random; each press moves one step.
  const [seed, setSeed] = useState(() => Math.floor(Math.random() * 1e9))
  const [keep, setKeep] = useState({ first: false, last: false })
  const raceNames = names[String(look.race)]
  const id = look.identity ?? {}
  const anyClass = useStore(store, (s) => s.anyClass)
  const classes = classChoices(characters, look.race, anyClass)
  // A class the race can't be (after a race change) goes, unless any class is allowed.
  const classOk = !look.identity?.classId || classes.some((c) => c.classId === look.identity!.classId)
  useEffect(() => { if (!classOk) commands.set_identity({ classId: null }) }, [classOk])
  const wording = (t: TitleInfo) => (look.sex === 1 ? t.female : t.male).replace('%s', '…')

  return (
    <section className="panel" aria-labelledby="identity-heading">
      <h2 id="identity-heading">Identity</h2>
      <div className="picker">
        <label htmlFor="id-first">First name</label>
        <input id="id-first" maxLength={MAX_NAME} value={id.firstName ?? ''} onChange={(e) => commands.set_identity({ firstName: e.target.value })} />
      </div>
      <div className="picker">
        <label htmlFor="id-last">Last name</label>
        <input id="id-last" maxLength={MAX_NAME} value={id.lastName ?? ''} onChange={(e) => commands.set_identity({ lastName: e.target.value })} />
      </div>
      {raceNames && (
        <div className="actions">
          <button type="button" onClick={() => {
            const next = seed + 1
            setSeed(next)
            commands.set_identity(generateName(raceNames, look.sex, next, {
              ...(keep.first && id.firstName ? { firstName: id.firstName } : {}),
              ...(keep.last && id.lastName ? { lastName: id.lastName } : {}),
            }))
          }}>Generate name</button>
          <label className="small"><input type="checkbox" checked={keep.first} onChange={(e) => setKeep({ ...keep, first: e.target.checked })} /> Keep first</label>
          <label className="small"><input type="checkbox" checked={keep.last} onChange={(e) => setKeep({ ...keep, last: e.target.checked })} /> Keep last</label>
        </div>
      )}
      <div className="picker">
        <label htmlFor="id-title">Title</label>
        <select id="id-title" value={id.titleId ?? ''} onChange={(e) => commands.set_identity({ titleId: e.target.value ? Number(e.target.value) : null })}>
          <option value="">No title</option>
          {titles.map((t) => <option key={t.titleId} value={t.titleId}>{wording(t)}</option>)}
        </select>
      </div>
      <div className="picker">
        <label htmlFor="id-guild">Guild</label>
        <input id="id-guild" maxLength={MAX_GUILD} value={id.guild ?? ''} onChange={(e) => commands.set_identity({ guild: e.target.value })} />
      </div>
      <div className="picker">
        <label htmlFor="id-level">Level</label>
        <input id="id-level" type="number" min={1} max={MAX_LEVEL} value={id.level ?? ''}
          onChange={(e) => {
            const n = Number(e.target.value)
            if (e.target.value === '') commands.set_identity({ level: null })
            else if (Number.isInteger(n) && n >= 1 && n <= MAX_LEVEL) commands.set_identity({ level: n })
          }} />
      </div>
      {classes.length > 0 && (
        <div className="picker">
          <label htmlFor="id-class">Class</label>
          <select id="id-class" value={id.classId ?? ''} onChange={(e) => commands.set_identity({ classId: e.target.value ? Number(e.target.value) : null })}>
            <option value="">No class</option>
            {classes.map((c) => <option key={c.classId} value={c.classId}>{c.name}</option>)}
          </select>
        </div>
      )}
      <label className="small" title="Offer every class, not only the ones this race can be">
        <input type="checkbox" checked={anyClass} onChange={(e) => setAnyClass(store, e.target.checked)} /> Any class
      </label>
      <label className="small">
        <input type="checkbox" checked={id.pvp ?? false} onChange={(e) => commands.set_identity({ pvp: e.target.checked || null })} /> PvP
      </label>
      <label className="small">
        <input type="checkbox" checked={pinTooltip} onChange={(e) => onPinTooltip(e.target.checked)} /> Always show the tooltip
      </label>
    </section>
  )
}
