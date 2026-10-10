import { describe, expect, test } from 'vitest'
import { generateName } from './names'

const human = { male: ['Antonidas', 'Arugal', 'Nielas'], female: ['Aegwynn', 'Calia'], last: ['Belgarden', 'Dawnstone', 'Cuthbridge'] }

describe('generateName', () => {
  test('the same seed always gives the same name, and seeds spread over the lists', () => {
    const a = generateName(human, 0, 42)
    expect(generateName(human, 0, 42)).toEqual(a)
    expect(human.male).toContain(a.firstName)
    expect(human.last).toContain(a.lastName)
    const seen = new Set(Array.from({ length: 50 }, (_, s) => generateName(human, 0, s).firstName))
    expect(seen.size).toBe(human.male.length)
  })

  test('first names follow the sex; last names are shared', () => {
    expect(human.female).toContain(generateName(human, 1, 7).firstName)
  })

  test('a locked part is kept while the other changes', () => {
    const names = Array.from({ length: 20 }, (_, s) => generateName(human, 0, s, { firstName: 'Asher' }))
    expect(new Set(names.map((n) => n.firstName))).toEqual(new Set(['Asher']))
    expect(new Set(names.map((n) => n.lastName)).size).toBeGreaterThan(1)
  })

  test('a race without a list gives nothing to pick, rather than a made-up name', () => {
    expect(generateName({ male: [], female: [], last: [] }, 0, 1)).toEqual({})
  })
})
