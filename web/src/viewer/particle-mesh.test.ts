import * as THREE from 'three'
import { describe, expect, test } from 'vitest'
import type { ParticleEmitterMeta } from '../api/types'
import { createParticleMesh } from './particle-mesh'
import { simulateTo } from './particles'

const constant = (...v: number[]) => ({ durationMs: 1000, interpolation: 1, keys: [[0, ...v]] })
const emitter = (blendMode = 4): ParticleEmitterMeta => ({
  index: 0, flags: 0, bone: 1, boneNode: 'bone_1', offsetGltf: [0, 0.1, 0], textureIndex: 1, textureFileDataId: null,
  blendMode, emitterType: 1, rows: 2, columns: 2,
  tracks: { emissionSpeed: constant(0.2), lifespan: constant(1.25), emissionRate: constant(8), areaX: constant(0.1), areaY: constant(0.1) },
  lifespanVariation: 0, emissionRateVariation: 0, scaleVariation: [0, 0], drag: 0,
  alpha: { times: [0, 1], values: [1, 0] },
})

describe('particle mesh', () => {
  test('draws the simulated particles at their positions, from the emitter offset', () => {
    const m = createParticleMesh(emitter(), null, 7)
    m.setTime(2)
    const want = simulateTo(emitter(), 7, 2).particles
    const geo = m.object.geometry
    expect(geo.drawRange.count).toBe(want.length)
    expect(Array.from(geo.getAttribute('position').array.slice(0, 3))).toEqual(want[0].pos.map(Math.fround))
    expect(m.object.position.toArray()).toEqual([0, 0.1, 0])
    m.dispose()
  })

  test('advances on update and keeps drawing', () => {
    const m = createParticleMesh(emitter(), null, 7)
    m.update(0.5)
    expect(m.object.geometry.drawRange.count).toBe(4)
    m.dispose()
  })

  test('additive emitters add their colour without writing depth', () => {
    const add = createParticleMesh(emitter(4), null, 1).object.material as THREE.ShaderMaterial
    expect(add.blending).toBe(THREE.CustomBlending)
    expect(add.blendDst).toBe(THREE.OneFactor)
    expect(add.depthWrite).toBe(false)
    const alpha = createParticleMesh(emitter(2), null, 1).object.material as THREE.ShaderMaterial
    expect(alpha.blendDst).toBe(THREE.OneMinusSrcAlphaFactor)
  })
})
