import { describe, expect, it } from 'vitest'
import { clipSeconds } from './timing'

describe('clipSeconds', () => {
  it('converts milliseconds to seconds', () => {
    expect(clipSeconds(2467)).toBe(2.467)
  })

  it('never returns 0, which makes three.js actions compute NaN times', () => {
    expect(clipSeconds(0)).toBeGreaterThan(0)
  })
})
