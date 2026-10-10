import { describe, expect, test } from 'vitest'
import { classChoices, displayName, fullName, nameplate, titlesFor, tooltipLines } from './identity'

const titles = [
  { titleId: 1, male: 'High Warlord %s', female: 'High Warlord %s' },
  { titleId: 2, male: '%s the Alchemist', female: '%s the Alchemist' },
  { titleId: 3, male: 'Lord %s', female: 'Lady %s' },
]

describe('identity text', () => {
  test('a full name is the first and last name, either alone, or empty', () => {
    expect(fullName({ firstName: 'Asher', lastName: 'Meier' })).toBe('Asher Meier')
    expect(fullName({ lastName: 'Meier' })).toBe('Meier')
    expect(fullName(undefined)).toBe('')
  })

  test('a title goes around the name, in the wording for the character sex', () => {
    expect(displayName({ firstName: 'Raaziel', titleId: 1 }, titles, 0)).toBe('High Warlord Raaziel')
    expect(displayName({ firstName: 'Asher', lastName: 'Meier', titleId: 2 }, titles, 0)).toBe('Asher Meier the Alchemist')
    expect(displayName({ firstName: 'Calia', titleId: 3 }, titles, 1)).toBe('Lady Calia')
  })

  test('without a name the title is not shown; an unknown title leaves the name alone', () => {
    expect(displayName({ titleId: 1 }, titles, 0)).toBe('')
    expect(displayName({ firstName: 'Asher', titleId: 99 }, titles, 0)).toBe('Asher')
  })
})

describe('nameplate and tooltip', () => {
  const undead = { race: 5, name: 'Undead', faction: 'horde' as const, classes: [{ classId: 9, name: 'Warlock' }], sexes: [] }
  const identity = { firstName: 'Asher', lastName: 'Meier', titleId: 1, guild: 'Slain', level: 29, classId: 9, pvp: true }

  test('the nameplate shows the titled name with the guild in angle brackets below, nothing without a name', () => {
    expect(nameplate(identity, titles, 0)).toEqual({ name: 'High Warlord Asher Meier', guild: '<Slain>', pvp: true })
    expect(nameplate({ firstName: 'Raaziel' }, titles, 0)).toEqual({ name: 'Raaziel', pvp: false })
    expect(nameplate({ guild: 'Slain' }, titles, 0)).toBeNull()
  })

  test('the tooltip has the in-game lines: name, guild, level and race, class, faction, PvP', () => {
    expect(tooltipLines(identity, titles, 0, undead)).toEqual([
      'High Warlord Asher Meier', 'Slain', 'Level 29 Undead (Player)', 'Warlock', 'Horde', 'PvP',
    ])
  })

  test('missing parts are left out rather than guessed', () => {
    expect(tooltipLines({ firstName: 'Asher' }, titles, 0, { ...undead, faction: undefined })).toEqual(['Asher', 'Undead (Player)'])
  })
})

describe('titlesFor', () => {
  const all = [
    { titleId: 1270, male: 'Grand Marshal %s', female: 'Grand Marshal %s', faction: 'alliance' as const },
    { titleId: 1284, male: 'High Warlord %s', female: 'High Warlord %s', faction: 'horde' as const },
    { titleId: 1313, male: 'Master Angler %s', female: 'Master Angler %s' },
  ]
  test("a race sees its own faction's PvP ranks and the titles anyone can hold", () => {
    expect(titlesFor(all, 'horde').map((t) => t.titleId)).toEqual([1284, 1313])
    expect(titlesFor(all, 'alliance').map((t) => t.titleId)).toEqual([1270, 1313])
  })
  test('a race of no known faction gets only the shared titles', () => {
    expect(titlesFor(all, undefined).map((t) => t.titleId)).toEqual([1313])
  })
})

describe('classChoices', () => {
  const characters = { build: 'b', races: [
    { race: 6, name: 'Tauren', classes: [{ classId: 1, name: 'Warrior' }, { classId: 11, name: 'Druid' }], sexes: [] },
    { race: 5, name: 'Undead', classes: [{ classId: 1, name: 'Warrior' }, { classId: 5, name: 'Priest' }], sexes: [] },
  ] }
  test("a race offers its own classes; with any class on, every class any race can be, once each, by ID", () => {
    expect(classChoices(characters, 6, false).map((c) => c.classId)).toEqual([1, 11])
    expect(classChoices(characters, 6, true).map((c) => c.classId)).toEqual([1, 5, 11])
  })
})
