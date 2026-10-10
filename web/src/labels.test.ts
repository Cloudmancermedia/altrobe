import { describe, expect, test } from 'vitest'
import type { CharacterRace } from './api/types'
import { racesByFaction } from './labels'

const race = (id: number, name: string, faction?: CharacterRace['faction']): CharacterRace =>
  ({ race: id, name, classes: [], sexes: [], faction })

describe('racesByFaction', () => {
  test('puts the Alliance first, then the Horde, keeping each side in order', () => {
    const groups = racesByFaction([race(1, 'Human', 'alliance'), race(2, 'Orc', 'horde'), race(3, 'Dwarf', 'alliance'), race(95, 'High Order Skyborne', 'alliance'), race(96, 'Windshaper Skyborne', 'horde')])
    expect(groups.map((g) => [g.faction, g.races.map((r) => r.name)])).toEqual([
      ['alliance', ['Human', 'Dwarf', 'High Order Skyborne']],
      ['horde', ['Orc', 'Windshaper Skyborne']],
    ])
  })

  test('races without a faction (an older server) come last in one group', () => {
    expect(racesByFaction([race(1, 'Human'), race(2, 'Orc', 'horde')]).map((g) => g.faction)).toEqual(['horde', 'neutral'])
  })
})
