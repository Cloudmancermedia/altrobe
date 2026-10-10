// The text of a character's identity, as the nameplate and tooltip show it. Pure.
import type { CharacterClass, CharacterRace, CharactersResponse, TitleInfo } from '../api/types'
import type { Identity } from '../look/look'

/** First and last name with a space, or whichever one is set. */
export function fullName(identity: Identity | undefined): string {
  return [identity?.firstName, identity?.lastName].filter(Boolean).join(' ')
}

/**
 * The name with its title around it ("High Warlord Raaziel", "Asher the Alchemist"), in the title's
 * wording for the character's sex. No name means nothing to show, title or not.
 */
export function displayName(identity: Identity | undefined, titles: TitleInfo[], sex: number): string {
  const name = fullName(identity)
  if (!name) return ''
  const title = identity?.titleId ? titles.find((t) => t.titleId === identity.titleId) : undefined
  if (!title) return name
  return (sex === 1 ? title.female : title.male).replace('%s', name)
}

/** What floats over the character's head: the titled name, then `<Guild>`. Null without a name. */
export function nameplate(identity: Identity | undefined, titles: TitleInfo[], sex: number): { name: string; guild?: string; pvp: boolean } | null {
  const name = displayName(identity, titles, sex)
  if (!name) return null
  return { name, ...(identity?.guild ? { guild: `<${identity.guild}>` } : {}), pvp: identity?.pvp ?? false }
}

const FACTIONS: Record<string, string> = { alliance: 'Alliance', horde: 'Horde' }

/**
 * The player tooltip, line by line: titled name, guild, "Level 29 Undead (Player)", class,
 * faction, PvP. The first line is the name line; parts the look does not set are left out.
 */
export function tooltipLines(identity: Identity | undefined, titles: TitleInfo[], sex: number, race: CharacterRace | undefined): string[] {
  const lines = [displayName(identity, titles, sex) || fullName(identity)]
  if (identity?.guild) lines.push(identity.guild)
  if (race) lines.push(`${identity?.level ? `Level ${identity.level} ` : ''}${race.name} (Player)`)
  const cls = identity?.classId ? race?.classes.find((c) => c.classId === identity.classId)?.name : undefined
  if (cls) lines.push(cls)
  if (race?.faction && FACTIONS[race.faction]) lines.push(FACTIONS[race.faction])
  if (identity?.pvp) lines.push('PvP')
  return lines.filter(Boolean)
}

/** The titles a race can hold: its own faction's PvP ranks and the titles either faction can. */
export function titlesFor(titles: TitleInfo[], faction: CharacterRace['faction']): TitleInfo[] {
  return titles.filter((t) => !t.faction || t.faction === faction)
}

/**
 * The classes to offer: the race's own, or with `anyClass` every class any race can be (to dress a
 * Tauren in priest sets, say), once each in ID order.
 */
export function classChoices(characters: CharactersResponse | null | undefined, race: number, anyClass: boolean): CharacterClass[] {
  const races = characters?.races ?? []
  if (!anyClass) return races.find((r) => r.race === race)?.classes ?? []
  const byId = new Map<number, CharacterClass>()
  for (const r of races) for (const c of r.classes) if (!byId.has(c.classId)) byId.set(c.classId, c)
  return [...byId.values()].sort((a, b) => a.classId - b.classId)
}
