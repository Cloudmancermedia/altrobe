// Customization choices. Pure; no three.js or DOM.
//
// The server works out the default look (LookResolver.cs) and lists every choice with the geosets
// and texture layers it brings. This applies the look's own choices on top, with the same rules:
// - Geosets: for each changed option, every geoset the option names is turned off (except 0, the
//   body), then the chosen choice's geosets are shown, if the body mesh has them.
// - Layers: rebuilt from the active choice of every option. A layer tied to a related choice (a
//   face texture made for one skin color) is used only while that choice is active.

import type { BaseLook, CustomizationOption, TextureLayer } from '../api/types'

export interface Customized {
  geosets: number[]
  layers: TextureLayer[]
  notes: string[]
}

type CustomizeLook = Pick<BaseLook, 'geosets' | 'layers' | 'options' | 'meshGeosets'>

/** The geosets and texture layers of a base look with `custom` choices (option ID -> choice ID) applied. */
export function customize(look: CustomizeLook, custom: Record<string, number>): Customized {
  const options = look.options
  if (!options?.length) {
    const notes = Object.keys(custom).length ? ['this character has no customization data; showing defaults'] : []
    return { geosets: look.geosets, layers: look.layers, notes }
  }
  const notes: string[] = []
  const active = options.map((o) => {
    const want = custom[String(o.optionId)]
    const pick = want === undefined ? undefined : o.choices.find((c) => c.choiceId === want)
    if (want !== undefined && !pick) notes.push(`option ${o.optionId} has no choice ${want}; using the default`)
    const chosen = pick ?? o.choices.find((c) => c.choiceId === o.defaultChoiceId)
    return { option: o, chosen, changed: !!pick && pick.choiceId !== o.defaultChoiceId }
  })
  const known = new Set(options.map((o) => String(o.optionId)))
  for (const k of Object.keys(custom)) if (!known.has(k)) notes.push(`no option ${k} for this character`)
  if (!active.some((a) => a.changed)) return { geosets: look.geosets, layers: look.layers, notes }

  const mesh = look.meshGeosets ? new Set(look.meshGeosets) : null
  const show = new Set(look.geosets)
  for (const { option, chosen, changed } of active) {
    if (!changed || !chosen) continue
    for (const g of option.geosets ?? []) if (g !== 0) show.delete(g)
    // A geoset group (id / 100) shows one variant, so the chosen one replaces every other, including
    // the reset rule's xx01 that no choice names (LookResolver does the same). 0 is the body.
    const groups = new Set((chosen.geosets ?? []).map((g) => Math.floor(g / 100)))
    for (const g of [...show]) if (g !== 0 && groups.has(Math.floor(g / 100))) show.delete(g)
    for (const g of chosen.geosets ?? []) if (!mesh || mesh.has(g)) show.add(g)
  }

  const activeIds = new Set(active.map((a) => a.chosen?.choiceId).filter((id): id is number => id !== undefined))
  const layers = active.flatMap(({ chosen }) => (chosen?.layers ?? [])
    .filter((l) => l.relatedChoiceId === undefined || activeIds.has(l.relatedChoiceId))
    .map((l) => { const out = { ...l }; delete out.relatedChoiceId; return out }))
    .sort((a, b) => a.textureType - b.textureType || a.layer - b.layer)

  return { geosets: [...show].sort((a, b) => a - b), layers, notes }
}

/** One random choice per option (option ID -> choice ID), for the dice button. */
/**
 * The dropdowns for the customization panel. The base look names a default for every option, including
 * ones character creation hides (Eye Style); when the server sends `options`, only those are shown.
 * Without it, each default stands alone.
 */
export function panelOptions(look: Pick<BaseLook, 'choices' | 'options'>): CustomizationOption[] {
  return look.choices.flatMap((c): CustomizationOption[] => {
    const full = look.options?.find((o) => o.optionId === c.optionId)
    if (look.options && !full) return []
    const choices = full?.choices ?? (c.choiceId ? [{ choiceId: c.choiceId, name: c.choice }] : [])
    return choices.length ? [{ optionId: c.optionId, name: c.option, defaultChoiceId: c.choiceId, choices }] : []
  })
}

export function randomCustomization(options: CustomizationOption[], random: () => number = Math.random): Record<string, number> {
  const out: Record<string, number> = {}
  for (const o of options) {
    if (!o.choices.length) continue
    out[String(o.optionId)] = o.choices[Math.min(o.choices.length - 1, Math.floor(random() * o.choices.length))].choiceId
  }
  return out
}
