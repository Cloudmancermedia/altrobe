// The name generator: picks a first and a last name from the game's own random-name lists (NameGen),
// as the in-game random-name button does. Deterministic: the same lists, sex and seed always give
// the same name, so "next" and "back" walk through names predictably. Pure.
import type { RaceNames } from '../api/types'

// mulberry32, a small seeded generator (public domain); one draw per name part.
function draw(seed: number): number {
  let t = (seed + 0x6d2b79f5) | 0
  t = Math.imul(t ^ (t >>> 15), t | 1)
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296
}
const pick = (list: string[], seed: number) => (list.length ? list[Math.floor(draw(seed) * list.length)] : undefined)

/**
 * A first name for the sex (0 male, 1 female) and a shared last name for `seed`. A part in `keep`
 * stays as it is. Parts the race has no list for are left out, never made up.
 */
export function generateName(names: RaceNames, sex: number, seed: number, keep: { firstName?: string; lastName?: string } = {}) {
  const firstName = keep.firstName ?? pick(sex === 1 ? names.female : names.male, seed)
  const lastName = keep.lastName ?? pick(names.last, seed ^ 0x5bd1e995)
  return { ...(firstName ? { firstName } : {}), ...(lastName ? { lastName } : {}) }
}
