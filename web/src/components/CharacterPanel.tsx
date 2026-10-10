import type { CharactersResponse, ModelSet } from '../api/types'
import { commands, store } from '../app-state'
import { useStore } from '../store'
import { useBaseLook } from '../hooks'
import { pickCharacter, racesByFaction, sexLabel, type Faction } from '../labels'
import { VIEWS, type Look } from '../look/look'
import { panelOptions } from '../viewer/customize'

const FACTION_LABELS: Record<Faction, string> = { alliance: 'Alliance', horde: 'Horde', neutral: 'Other' }

export function CharacterPicker({ characters, race, sex, models, onChange, idPrefix }: {
  characters: CharactersResponse; race: number; sex: number; models: ModelSet
  onChange: (race: number, sex: 0 | 1, models: ModelSet) => void; idPrefix: string
}) {
  const r = characters.races.find((x) => x.race === race)
  const s = r?.sexes.find((x) => x.sex === sex)
  return (
    <div className="picker">
      <label htmlFor={`${idPrefix}-race`}>Race</label>
      <select
        id={`${idPrefix}-race`}
        value={race}
        onChange={(e) => {
          const next = characters.races.find((x) => x.race === Number(e.target.value))!
          const p = pickCharacter(next, sex, models)
          if (p) onChange(next.race, p.sex, p.models)
        }}
      >
        {racesByFaction(characters.races).map((g) => (
          <optgroup key={g.faction} label={FACTION_LABELS[g.faction]}>
            {g.races.map((x) => {
              const any = x.sexes.some((s) => s.hd || s.sd)
              return <option key={x.race} value={x.race} disabled={!any}>{x.name}{any ? '' : ' (no data)'}</option>
            })}
          </optgroup>
        ))}
      </select>

      <span className="label">Sex</span>
      <div className="segmented" role="radiogroup" aria-label="Sex">
        {([0, 1] as const).map((v) => {
          const entry = r?.sexes.find((x) => x.sex === v)
          const ok = !!entry && (entry.hd || entry.sd)
          return (
            <button key={v} type="button" role="radio" aria-checked={sex === v} disabled={!ok}
              onClick={() => entry && onChange(race, v, entry[models] ? models : entry.hd ? 'hd' : 'sd')}>
              {sexLabel(v)}
            </button>
          )
        })}
      </div>

      <span className="label">Models</span>
      <div className="segmented" role="radiogroup" aria-label="Models">
        {(['hd', 'sd'] as const).map((m) => (
          <button key={m} type="button" role="radio" aria-checked={models === m} disabled={!s?.[m]}
            title={s?.[m] ? undefined : `No ${m.toUpperCase()} model for this character`}
            onClick={() => onChange(race, sex as 0 | 1, m)}>
            {m.toUpperCase()}
          </button>
        ))}
      </div>
    </div>
  )
}

export function CharacterPanel({ characters, look }: { characters: CharactersResponse; look: Look }) {
  const race = characters.races.find((r) => r.race === look.race)
  const animations = useStore(store, (s) => s.animations)
  return (
    <section className="panel" aria-labelledby="character-heading">
      <h2 id="character-heading">Character</h2>
      <CharacterPicker characters={characters} race={look.race} sex={look.sex} models={look.models} idPrefix="main"
        onChange={(r, s, m) => commands.set_character(r, s, m)} />
      {race && race.classes.length > 0 && (
        <p className="muted small">Classes: {race.classes.map((c) => c.name).join(', ')}</p>
      )}
      <div className="picker">
        <label htmlFor="cam-view">View</label>
        <select id="cam-view" value={look.cam.view} onChange={(e) => commands.set_view(e.target.value)}>
          {VIEWS.map((v) => <option key={v} value={v}>{v[0].toUpperCase() + v.slice(1)}</option>)}
        </select>
      </div>
      {animations.length > 0 && (
        <div className="picker">
          <label htmlFor="anim">Animation</label>
          <select id="anim" value={look.anim ?? 'Stand'} onChange={(e) => commands.set_animation(e.target.value)}>
            {animations.map((a) => <option key={a.id} value={a.name}>{a.name}</option>)}
          </select>
        </div>
      )}
    </section>
  )
}

export function CustomizationPanel({ look }: { look: Look }) {
  const changed = Object.keys(look.custom).length > 0
  const baseLook = useBaseLook(look.race, look.sex, look.models)
  if (!baseLook) return null
  const options = panelOptions(baseLook)

  return (
    <section className="panel" aria-labelledby="custom-heading">
      <h2 id="custom-heading">Customization</h2>
      <div className="actions">
        <button type="button" onClick={() => commands.randomize_customization()} title="Pick a random choice for every option">
          <span aria-hidden="true">🎲</span> Randomize
        </button>
        <button type="button" onClick={() => commands.reset_customization()} disabled={!changed}>Reset to defaults</button>
      </div>
      {options.map((o) => {
        const value = look.custom[String(o.optionId)] ?? o.defaultChoiceId ?? o.choices[0].choiceId
        const current = o.choices.find((c) => c.choiceId === value)
        const id = `opt-${o.optionId}`
        return (
          <div className="picker" key={o.optionId}>
            <label htmlFor={id}>{o.name}</label>
            <div className="choice">
              {current?.swatch && <span className="swatch" style={{ background: current.swatch }} aria-hidden="true" />}
              <select id={id} value={value} onChange={(e) => commands.set_customization(o.optionId, Number(e.target.value))}>
                {o.choices.map((c, i) => (
                  <option key={c.choiceId} value={c.choiceId}>
                    {c.name || `#${i + 1}`}{c.choiceId === o.defaultChoiceId ? ' (default)' : ''}
                  </option>
                ))}
              </select>
            </div>
          </div>
        )
      })}
    </section>
  )
}
