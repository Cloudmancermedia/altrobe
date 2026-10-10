import { describe, expect, test } from 'vitest'
import { DOLL } from './doll-layout'

describe('the character screen layout', () => {
  test('mirrors the in-game character pane, with only slots the model shows: armor left, hands to feet right, weapons below', () => {
    expect(DOLL.left.map((s) => s.slot)).toEqual(['head', 'shoulder', 'back', 'chest', 'shirt', 'tabard', 'wrist'])
    expect(DOLL.right.map((s) => s.slot)).toEqual(['hands', 'waist', 'legs', 'feet'])
    expect(DOLL.bottom.map((s) => s.slot)).toEqual(['mainhand', 'offhand', 'ranged'])
  })

  test('no neck, rings or trinkets; every slot shown can be equipped', () => {
    const all = [...DOLL.left, ...DOLL.right, ...DOLL.bottom]
    expect(all.map((s) => s.slot)).not.toContain('neck')
    expect(all.every((s) => s.equippable)).toBe(true)
  })
})
