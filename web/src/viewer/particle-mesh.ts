// Draws one M2 particle emitter. A Points object with a small shader rather than instanced quads:
// glows are round sprites, so square, camera-facing points are enough, and one draw call per emitter
// with fixed-size buffers keeps the cost flat. The shader sizes each point in model units (scaled by
// the bone's world scale, like the game's InheritBoneScale) and picks its atlas cell.
// Particle spin is not drawn: points can't rotate.

import * as THREE from 'three'
import type { ParticleEmitterMeta } from '../api/types'
import { m2Blend } from './m2-material'
import { MAX_PARTICLES, advance, appearance, simulateTo, type EmitterState } from './particles'

const vertexShader = /* glsl */ `
attribute vec4 aColor;
attribute float aSize;
attribute float aCell;
uniform float uViewportHeight;
uniform float uScale;
varying vec4 vColor;
varying float vCell;
void main() {
  vec4 mv = modelViewMatrix * vec4(position, 1.0);
  gl_Position = projectionMatrix * mv;
  // aSize is the half-width in model units. At depth -mv.z one unit covers
  // projectionMatrix[1][1] * height / 2 pixels, so the full width 2 * aSize is aSize * that * 2.
  gl_PointSize = max(aSize * uScale * projectionMatrix[1][1] * uViewportHeight / max(-mv.z, 1e-4), 0.0);
  vColor = aColor;
  vCell = aCell;
}`

const fragmentShader = /* glsl */ `
uniform sampler2D uMap;
uniform bool uHasMap;
uniform float uRows;
uniform float uColumns;
varying vec4 vColor;
varying float vCell;
void main() {
  vec2 cell = vec2(mod(vCell, uColumns), floor(vCell / uColumns));
  vec2 uv = (cell + gl_PointCoord) / vec2(uColumns, uRows);
  vec4 c = (uHasMap ? texture2D(uMap, uv) : vec4(1.0)) * vColor;
  if (c.a <= 0.0) discard;
  gl_FragColor = c;
  #include <colorspace_fragment>
}`

export interface ParticleMesh {
  object: THREE.Points<THREE.BufferGeometry, THREE.ShaderMaterial>
  update(dt: number): void
  setTime(t: number): void
  dispose(): void
}

export function createParticleMesh(emitter: ParticleEmitterMeta, texture: THREE.Texture | null, seed: number): ParticleMesh {
  const position = new THREE.BufferAttribute(new Float32Array(MAX_PARTICLES * 3), 3)
  const color = new THREE.BufferAttribute(new Float32Array(MAX_PARTICLES * 4), 4)
  const size = new THREE.BufferAttribute(new Float32Array(MAX_PARTICLES), 1)
  const cell = new THREE.BufferAttribute(new Float32Array(MAX_PARTICLES), 1)
  for (const a of [position, color, size, cell]) a.setUsage(THREE.DynamicDrawUsage)
  const geometry = new THREE.BufferGeometry()
  geometry.setAttribute('position', position)
  geometry.setAttribute('aColor', color)
  geometry.setAttribute('aSize', size)
  geometry.setAttribute('aCell', cell)
  geometry.setDrawRange(0, 0)

  const material = new THREE.ShaderMaterial({
    vertexShader,
    fragmentShader,
    uniforms: {
      uMap: { value: texture },
      uHasMap: { value: texture !== null },
      uRows: { value: emitter.rows },
      uColumns: { value: emitter.columns },
      uViewportHeight: { value: 1 },
      uScale: { value: 1 },
    },
    transparent: true,
    depthWrite: false,
    ...m2Blend(emitter.blendMode),
  })
  // Alpha-key and opaque emitters still blend their faded edges; only depth writes stay off.
  if (!material.transparent) material.transparent = true

  const object = new THREE.Points(geometry, material)
  object.name = `particles_${emitter.index}`
  object.position.fromArray(emitter.offsetGltf)
  object.frustumCulled = false
  const viewport = new THREE.Vector4()
  object.onBeforeRender = (renderer) => {
    material.uniforms.uViewportHeight.value = renderer.getCurrentViewport(viewport).w
    material.uniforms.uScale.value = object.matrixWorld.getMaxScaleOnAxis()
  }

  const linear = new THREE.Color()
  let state: EmitterState = simulateTo(emitter, seed, 0)
  const write = () => {
    const n = state.particles.length
    for (let i = 0; i < n; i++) {
      const p = state.particles[i]
      const a = appearance(emitter, p)
      position.setXYZ(i, p.pos[0], p.pos[1], p.pos[2])
      // Curve colours are sRGB like the game's; the shader works in linear space.
      linear.setRGB(a.color[0], a.color[1], a.color[2], THREE.SRGBColorSpace)
      color.setXYZW(i, linear.r, linear.g, linear.b, a.alpha)
      size.setX(i, a.size)
      cell.setX(i, a.cell)
    }
    geometry.setDrawRange(0, n)
    for (const attr of [position, color, size, cell]) attr.needsUpdate = true
  }

  return {
    object,
    update(dt) { advance(state, dt); write() },
    setTime(t) { state = simulateTo(emitter, seed, t); write() },
    dispose() { geometry.dispose(); material.dispose() },
  }
}
