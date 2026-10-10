// M2 particle simulation, as plain math over the emitter metadata the converter writes. The three.js
// side is particle-mesh.ts.
//
// Semantics from https://wowdev.wiki/M2 (Particle emitters) and the generators in WebWowViewerCpp
// (https://github.com/Deamon87/WebWowViewerCpp, CParticleGenerator): a plane generator (type 1)
// starts particles in an areaX by areaY rectangle on the emitter's z = 0 plane and sends them along a
// direction tipped from +z by up to verticalRange and turned by up to horizontalRange; a sphere
// generator (type 2) starts them between two radii, at up to verticalRange elevation, moving outward.
// With zSource > 0 the direction is away from (0, 0, zSource) instead. Spline and bone generators
// (3, 4) are not supported and start at the emitter, moving along +z.
//
// Particles live in the emitter's space (a child of its bone), so they follow the bone; WoW keeps
// world-space emitters' particles where they were emitted, which only differs while the bone moves.
// Not simulated: spin, twinkle, tails, squirt bursts, wind and model particles.

import type { ParticleCurve, ParticleEmitterMeta, ParticleTrack } from '../api/types'

export const MAX_PARTICLES = 64
/** Fixed step for simulateTo, in seconds. */
const STEP = 1 / 30

export interface Particle { pos: number[]; vel: number[]; age: number; life: number; sizeFactor: number }

export interface EmitterState {
  emitter: ParticleEmitterMeta
  particles: Particle[]
  /** Fractional particles owed by the emission rate. */
  owed: number
  timeMs: number
  random: () => number
}

/** mulberry32: small seeded generator, so a given time always looks the same. */
export function rng(seed: number): () => number {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const lerp = (a: number[], b: number[], f: number) => a.map((x, i) => x + (b[i] - x) * f)
const asArray = (v: number | number[]) => (typeof v === 'number' ? [v] : v)

/** A track's value at `tMs` on its loop: linear between keys (held for step tracks), the last key held to the end. */
export function sampleTrack(track: ParticleTrack | undefined, tMs: number, fallback: number[]): number[] {
  if (!track || track.keys.length === 0) return fallback
  const keys = track.keys
  const t = track.durationMs > 0 ? ((tMs % track.durationMs) + track.durationMs) % track.durationMs : tMs
  if (t <= keys[0][0]) return keys[0].slice(1)
  for (let i = 1; i < keys.length; i++) {
    if (t < keys[i][0]) {
      const [a, b] = [keys[i - 1], keys[i]]
      if (track.interpolation === 0) return a.slice(1)
      return lerp(a.slice(1), b.slice(1), (t - a[0]) / (b[0] - a[0]))
    }
  }
  return keys[keys.length - 1].slice(1)
}

/** A lifetime curve at fraction `f` of a particle's life. */
export function sampleCurve(curve: ParticleCurve | null | undefined, f: number, fallback: number[]): number[] {
  if (!curve || curve.times.length === 0) return fallback
  const { times, values } = curve
  if (f <= times[0]) return asArray(values[0])
  for (let i = 1; i < times.length; i++) {
    if (f <= times[i]) {
      const span = times[i] - times[i - 1]
      return span > 0 ? lerp(asArray(values[i - 1]), asArray(values[i]), (f - times[i - 1]) / span) : asArray(values[i])
    }
  }
  return asArray(values[values.length - 1])
}

const one = (e: ParticleEmitterMeta, name: keyof ParticleEmitterMeta['tracks'], tMs: number, fallback = 0) =>
  sampleTrack(e.tracks[name], tMs, [fallback])[0]
const signed = (random: () => number) => random() * 2 - 1
/** WoW (x, y, z) to glTF (x, z, -y), as the converter does. */
const toGltf = (v: number[]) => [v[0], v[2], -v[1]]

export function spawn(e: ParticleEmitterMeta, tMs: number, random: () => number): Particle {
  const [ax, ay] = [one(e, 'areaX', tMs), one(e, 'areaY', tMs)]
  const vertical = one(e, 'verticalRange', tMs)
  const horizontal = one(e, 'horizontalRange', tMs)
  const zSource = one(e, 'zSource', tMs)
  let pos = [0, 0, 0]
  let dir = [0, 0, 1]
  if (e.emitterType === 1) {
    pos = [signed(random) * ax / 2, signed(random) * ay / 2, 0]
    const polar = signed(random) * vertical
    const azimuth = signed(random) * horizontal
    dir = [Math.sin(polar) * Math.cos(azimuth), Math.sin(polar) * Math.sin(azimuth), Math.cos(polar)]
  } else if (e.emitterType === 2) {
    const r = Math.min(ax, ay) + random() * Math.abs(ay - ax)
    const elevation = signed(random) * vertical
    const azimuth = signed(random) * horizontal
    pos = [r * Math.cos(elevation) * Math.cos(azimuth), r * Math.cos(elevation) * Math.sin(azimuth), r * Math.sin(elevation)]
    const len = Math.hypot(...pos)
    if (len > 0) dir = pos.map((x) => x / len)
  }
  if (zSource > 0) {
    const away = [pos[0], pos[1], pos[2] - zSource]
    const len = Math.hypot(...away)
    if (len > 0) dir = away.map((x) => x / len)
  }
  const speed = one(e, 'emissionSpeed', tMs) * (1 + one(e, 'speedVariation', tMs) * signed(random))
  const life = Math.max(one(e, 'lifespan', tMs, 1) + e.lifespanVariation * signed(random), 0.01)
  return {
    pos: toGltf(pos),
    vel: toGltf(dir.map((x) => x * speed)),
    age: 0,
    life,
    sizeFactor: Math.max(1 + (e.scaleVariation[0] ?? 0) * signed(random), 0),
  }
}

export function createState(emitter: ParticleEmitterMeta, seed: number): EmitterState {
  return { emitter, particles: [], owed: 0, timeMs: 0, random: rng(seed) }
}

/** Ages and moves the particles by `dt` seconds, drops the dead ones, then emits new ones. */
export function advance(s: EmitterState, dt: number) {
  const e = s.emitter
  const gravity = sampleTrack(e.tracks.gravity, s.timeMs, [0, 0, 0])
  const drag = e.drag > 0 ? Math.exp(-e.drag * dt) : 1
  for (const p of s.particles) {
    p.age += dt
    for (let i = 0; i < 3; i++) {
      p.vel[i] = (p.vel[i] + gravity[i] * dt) * drag
      p.pos[i] += p.vel[i] * dt
    }
  }
  s.particles = s.particles.filter((p) => p.age < p.life)
  s.timeMs += dt * 1000
  const rate = Math.max(one(e, 'emissionRate', s.timeMs) + e.emissionRateVariation * signed(s.random), 0)
  s.owed += rate * dt
  // The epsilon keeps floating-point sums such as 30 * (8 / 30) from coming out one short.
  while (s.owed >= 1 - 1e-9) {
    s.owed -= 1
    if (s.particles.length < MAX_PARTICLES) s.particles.push(spawn(e, s.timeMs, s.random))
  }
}

/** How much of the past simulateTo replays: longer than any particle lives. */
const REPLAY = 10

/** The emitter as it looks `t` seconds in, always the same for the same seed and t. */
export function simulateTo(emitter: ParticleEmitterMeta, seed: number, t: number): EmitterState {
  const s = createState(emitter, seed)
  const start = Math.max(t - REPLAY, 0)
  s.timeMs = start * 1000
  for (let left = Math.max(t, 0) - start; left > 1e-9; left -= STEP) advance(s, Math.min(STEP, left))
  return s
}

/** Colour (0-1), alpha, half-size in model units, and atlas cell for one particle. */
export function appearance(e: ParticleEmitterMeta, p: Particle) {
  const f = Math.min(p.age / p.life, 1)
  return {
    color: sampleCurve(e.color, f, [1, 1, 1]),
    alpha: sampleCurve(e.alpha, f, [1])[0],
    size: sampleCurve(e.scale, f, [1, 1])[0] * p.sizeFactor,
    cell: Math.round(sampleCurve(e.headCell, f, [0])[0]),
  }
}
