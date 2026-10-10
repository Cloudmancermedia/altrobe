import type { CharacterRace, CharactersResponse, ModelSet } from './api/types'
import type { SlotName } from './viewer/dress'

export const SLOT_LABELS: Record<SlotName, string> = {
  head: 'Head', neck: 'Neck', shoulder: 'Shoulders', shirt: 'Shirt', chest: 'Chest', waist: 'Waist', legs: 'Legs',
  feet: 'Feet', wrist: 'Wrists', hands: 'Hands', back: 'Back', mainhand: 'Main hand', offhand: 'Off hand', ranged: 'Ranged', tabard: 'Tabard',
}

export const QUALITY_NAMES = ['Poor', 'Common', 'Uncommon', 'Rare', 'Epic', 'Legendary', 'Artifact', 'Heirloom']

export const sexLabel = (sex: number) => (sex === 0 ? 'male' : 'female')

export function characterLabel(characters: CharactersResponse | null, race: number, sex: number, models: string) {
  const name = characters?.races.find((r) => r.race === race)?.name ?? `Race ${race}`
  return `${name} ${sexLabel(sex)} ${models.toUpperCase()}`
}

/** Picks the nearest available sex and model set when the race changes. */
export function pickCharacter(race: CharacterRace, sex: number, models: ModelSet): { sex: 0 | 1; models: ModelSet } | null {
  const usable = race.sexes.filter((s) => s.hd || s.sd)
  const s = usable.find((x) => x.sex === sex) ?? usable[0]
  if (!s) return null
  return { sex: s.sex, models: s[models] ? models : s.hd ? 'hd' : 'sd' }
}

export type Faction = NonNullable<CharacterRace['faction']>

/** Races grouped Alliance, then Horde, then anything else, each in the server's order. */
export function racesByFaction(races: CharacterRace[]): { faction: Faction; races: CharacterRace[] }[] {
  const order: Faction[] = ['alliance', 'horde', 'neutral']
  return order
    .map((faction) => ({ faction, races: races.filter((r) => (r.faction ?? 'neutral') === faction) }))
    .filter((g) => g.races.length > 0)
}
