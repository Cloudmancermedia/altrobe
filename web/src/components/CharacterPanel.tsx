import type { CharactersResponse, CustomizationOption, ModelSet } from '../api/types'
import { commands } from '../app-state'
import { useBaseLook } from '../hooks'
import { pickCharacter, sexLabel } from '../labels'
import { VIEWS, type Look } from '../look/look'

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
        {characters.races.map((x) => {
          const any = x.sexes.some((s) => s.hd || s.sd)
          return <option key={x.race} value={x.race} disabled={!any}>{x.name}{any ? '' : ' (no data)'}</option>
        })}
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
    </section>
  )
}

export function CustomizationPanel({ look }: { look: Look }) {
  const baseLook = useBaseLook(look.race, look.sex, look.models)
  if (!baseLook) return null
  // The base look names only the default choice per option; `options` (all choices) is optional.
  const options = baseLook.choices.map((c): CustomizationOption => {
    const full = baseLook.options?.find((o) => o.optionId === c.optionId)
    return {
      optionId: c.optionId,
      name: c.option,
      defaultChoiceId: c.choiceId,
      choices: full?.choices ?? (c.choiceId ? [{ choiceId: c.choiceId, name: c.choice }] : []),
    }
  }).filter((o) => o.choices.length > 0)

  return (
    <section className="panel" aria-labelledby="custom-heading">
      <h2 id="custom-heading">Customization</h2>
      {options.map((o) => {
        const value = look.custom[String(o.optionId)] ?? o.defaultChoiceId ?? o.choices[0].choiceId
        const isDefault = value === o.defaultChoiceId
        const current = o.choices.find((c) => c.choiceId === value)
        const id = `opt-${o.optionId}`
        return (
          <div className="picker" key={o.optionId}>
            <label htmlFor={id}>{o.name}</label>
            <div className="choice">
              {current?.swatch && <span className="swatch" style={{ background: current.swatch }} aria-hidden="true" />}
              <select id={id} value={value} onChange={(e) => commands.set_customization(o.optionId, Number(e.target.value))}
                aria-describedby={isDefault ? undefined : `${id}-note`}>
                {o.choices.map((c, i) => (
                  <option key={c.choiceId} value={c.choiceId}>
                    {c.name || `#${i + 1}`}{c.choiceId === o.defaultChoiceId ? ' (default)' : ''}
                  </option>
                ))}
              </select>
            </div>
            {!isDefault && <span id={`${id}-note`} className="tag">preview uses defaults</span>}
          </div>
        )
      })}
    </section>
  )
}
