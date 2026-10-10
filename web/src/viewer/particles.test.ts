import { describe, expect, test } from 'vitest'
import type { ParticleEmitterMeta } from '../api/types'
import { MAX_PARTICLES, advance, appearance, createState, rng, sampleCurve, sampleTrack, simulateTo, spawn } from './particles'

const constant = (...v: number[]) => ({ durationMs: 1000, interpolation: 1, keys: [[0, ...v]] })

// A plane emitter like the Warlord's shoulder glow (model 143212): 8 particles a second that drift
// straight up and live 1.25 s.
const glow = (over: Partial<ParticleEmitterMeta> = {}): ParticleEmitterMeta => ({
  index: 0, flags: 0, bone: 1, boneNode: 'bone_1', offsetGltf: [0, 0, 0], textureIndex: 1, textureFileDataId: 203839,
  blendMode: 4, emitterType: 1, rows: 1, columns: 1,
  tracks: {
    emissionSpeed: constant(0.2), speedVariation: constant(0), verticalRange: constant(0), horizontalRange: constant(Math.PI * 2),
    lifespan: constant(1.25), emissionRate: constant(8), areaX: constant(0.2), areaY: constant(0.1),
  },
  lifespanVariation: 0, emissionRateVariation: 0,
  color: { times: [0, 0.5, 1], values: [[1, 0, 0], [0, 1, 0], [0, 0, 1]] },
  alpha: { times: [0, 0.5, 1], values: [0, 1, 0] },
  scale: { times: [0, 1], values: [[0.1, 0.1], [0.3, 0.3]] },
  scaleVariation: [0, 0], drag: 0,
  ...over,
})

describe('tracks and curves', () => {
  test('a track interpolates between keys and loops over its duration', () => {
    const t = { durationMs: 1000, interpolation: 1, keys: [[0, 10], [500, 20]] }
    expect(sampleTrack(t, 250, [0])).toEqual([15])
    expect(sampleTrack(t, 1250, [0])).toEqual([15])
    expect(sampleTrack(t, 750, [0])).toEqual([20]) // holds the last key to the end of the loop
  })

  test('a step track holds each key, and a missing track gives the fallback', () => {
    expect(sampleTrack({ durationMs: 1000, interpolation: 0, keys: [[0, 10], [500, 20]] }, 400, [0])).toEqual([10])
    expect(sampleTrack(undefined, 400, [7])).toEqual([7])
  })

  test('a lifetime curve interpolates by the fraction of life', () => {
    expect(sampleCurve(glow().color, 0.25, [1, 1, 1])).toEqual([0.5, 0.5, 0])
    expect(sampleCurve(glow().alpha, 0.75, [1])).toEqual([0.5])
    expect(sampleCurve(null, 0.5, [1])).toEqual([1])
  })
})

describe('spawning', () => {
  test('a plane emitter with no vertical range shoots straight up (glTF +Y) from inside its area', () => {
    const random = rng(1)
    for (let i = 0; i < 50; i++) {
      const p = spawn(glow(), 0, random)
      expect(p.vel[0]).toBeCloseTo(0, 6)
      expect(p.vel[1]).toBeCloseTo(0.2, 6)
      expect(p.vel[2]).toBeCloseTo(0, 6)
      expect(Math.abs(p.pos[0])).toBeLessThanOrEqual(0.1)   // WoW x, half of areaX
      expect(p.pos[1]).toBe(0)                              // the plane is WoW z = 0
      expect(Math.abs(p.pos[2])).toBeLessThanOrEqual(0.05)  // WoW y -> glTF -z, half of areaY
      expect(p.life).toBe(1.25)
    }
  })

  test('a sphere emitter starts particles between its radius bounds', () => {
    const e = glow({ emitterType: 2, tracks: { ...glow().tracks, verticalRange: constant(Math.PI / 2), areaX: constant(0.5), areaY: constant(1) } })
    const random = rng(2)
    for (let i = 0; i < 50; i++) {
      const r = Math.hypot(...spawn(e, 0, random).pos)
      expect(r).toBeGreaterThanOrEqual(0.5 - 1e-9)
      expect(r).toBeLessThanOrEqual(1 + 1e-9)
    }
  })
})

describe('simulation', () => {
  test('emits at the track rate and drops particles at the end of their life', () => {
    const s = createState(glow(), 3)
    for (let i = 0; i < 30; i++) advance(s, 1 / 30)
    expect(s.particles.length).toBe(8)
    for (let i = 0; i < 60; i++) advance(s, 1 / 30) // 3 s in: only about the last 1.25 s (10) live
    expect(s.particles.length).toBeGreaterThanOrEqual(9)
    expect(s.particles.length).toBeLessThanOrEqual(11)
    expect(s.particles.every((p) => p.age < p.life)).toBe(true)
  })

  test('particles move with their velocity and fall with gravity', () => {
    const s = createState(glow({ tracks: { ...glow().tracks, gravity: constant(0, -1, 0) } }), 4)
    advance(s, 0.2)
    const [p] = s.particles
    const before = [...p.pos]
    advance(s, 0.1)
    expect(p.pos[1]).toBeLessThan(before[1] + 0.2 * 0.1) // gravity slowed its rise
  })

  test('caps the particles of one emitter', () => {
    const s = createState(glow({ tracks: { ...glow().tracks, emissionRate: constant(10000), lifespan: constant(10) } }), 5)
    advance(s, 1)
    expect(s.particles.length).toBe(MAX_PARTICLES)
  })

  test('simulating to a time is repeatable', () => {
    const a = simulateTo(glow(), 9, 2.5).particles.map((p) => p.pos)
    const b = simulateTo(glow(), 9, 2.5).particles.map((p) => p.pos)
    expect(a.length).toBeGreaterThan(0)
    expect(a).toEqual(b)
  })

  test('a far-off time replays only the last few seconds', () => {
    const late = simulateTo(glow(), 9, 600)
    expect(late.timeMs).toBeCloseTo(600_000, 3)
    expect(late.particles.length).toBeGreaterThan(0)
  })

  test('appearance follows the life curves', () => {
    const p = { pos: [0, 0, 0], vel: [0, 0, 0], age: 0.625, life: 1.25, sizeFactor: 1 }
    const a = appearance(glow(), p)
    expect(a.color).toEqual([0, 1, 0])
    expect(a.alpha).toBe(1)
    expect(a.size).toBeCloseTo(0.2, 6)
    expect(a.cell).toBe(0)
  })
})
