// three.js material for an M2 texture unit, from its M2Material blend mode and render flags.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: M2 blend mode -> GL blend state
// (src/js/3D/renderers/M2RendererGL.js M2BLEND_TO_EGX, src/js/3D/gl/GLContext.js apply_blend_mode),
// render flags -> culling and depth state (M2RendererGL.js render), alpha-key threshold 0.501960814.
//
// Simplifications: only the unit's first texture is used (no multi-texture combiners, texture
// transforms, colour or transparency tracks), and alpha key always discards on the first texture's
// alpha, whereas wow.export only discards for combiners that can.
//
// Blend modes (https://wowdev.wiki/M2#Render_flags_and_blending_modes): 0 opaque, 1 alpha key,
// 2 alpha, 3 no-alpha add, 4 add, 5 mod, 6 mod2x, 7 blend add.
// Render flags: 0x1 unlit, 0x4 two-sided, 0x8 no depth test, 0x10 no depth write.

import * as THREE from 'three'

export function m2Material(map: THREE.Texture, blendMode: number, flags: number): THREE.Material {
  const unlit = (flags & 0x1) !== 0
  const opts: THREE.MeshStandardMaterialParameters = {
    map,
    side: flags & 0x4 ? THREE.DoubleSide : THREE.FrontSide,
    depthTest: (flags & 0x8) === 0,
    depthWrite: (flags & 0x10) === 0,
  }
  Object.assign(opts, m2Blend(blendMode))
  // Blended modes never write depth (GLContext.apply_blend_mode), whatever the flags say.
  if (blendMode >= 2) opts.depthWrite = false
  return unlit ? new THREE.MeshBasicMaterial(opts as THREE.MeshBasicMaterialParameters) : new THREE.MeshStandardMaterial({ ...opts, roughness: 0.85 })
}

/** Blend state for an M2 blend mode, shared by meshes and particles. Blended modes never write depth (GLContext.apply_blend_mode). */
export function m2Blend(blendMode: number): THREE.MaterialParameters {
  const custom = (src: THREE.BlendingSrcFactor, dst: THREE.BlendingDstFactor, srcA: THREE.BlendingSrcFactor = src, dstA: THREE.BlendingDstFactor = dst) => ({
    transparent: true, blending: THREE.CustomBlending, blendEquation: THREE.AddEquation,
    blendSrc: src, blendDst: dst, blendSrcAlpha: srcA, blendDstAlpha: dstA, depthWrite: false,
  })
  switch (blendMode) {
    case 0: return {}                                                // opaque
    case 1: return { alphaTest: 0.501960814 }                        // alpha key
    case 2: return custom(THREE.SrcAlphaFactor, THREE.OneMinusSrcAlphaFactor, THREE.OneFactor, THREE.OneMinusSrcAlphaFactor)
    case 3: return custom(THREE.OneFactor, THREE.OneFactor, THREE.ZeroFactor, THREE.OneFactor)
    case 4: return custom(THREE.SrcAlphaFactor, THREE.OneFactor, THREE.ZeroFactor, THREE.OneFactor)
    case 5: return custom(THREE.DstColorFactor, THREE.ZeroFactor)
    case 6: return custom(THREE.DstColorFactor, THREE.SrcColorFactor)
    case 7: return custom(THREE.OneFactor, THREE.OneMinusSrcAlphaFactor)
    default: return { alphaTest: 0.501960814 }
  }
}
