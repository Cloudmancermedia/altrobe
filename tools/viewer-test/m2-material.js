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

/**
 * @param {typeof import('three')} THREE
 * @param {import('three').Texture} map
 * @param {number} blendMode  M2Material blending mode
 * @param {number} flags  M2Material render flags
 */
export function m2Material(THREE, map, blendMode, flags) {
  const unlit = (flags & 0x1) !== 0;
  const opts = {
    map,
    side: flags & 0x4 ? THREE.DoubleSide : THREE.FrontSide,
    depthTest: (flags & 0x8) === 0,
    depthWrite: (flags & 0x10) === 0,
  };
  const custom = (src, dst, srcA = src, dstA = dst) => ({
    transparent: true, blending: THREE.CustomBlending, blendEquation: THREE.AddEquation,
    blendSrc: src, blendDst: dst, blendSrcAlpha: srcA, blendDstAlpha: dstA, depthWrite: false,
  });
  switch (blendMode) {
    case 0: break;                                                   // opaque
    case 1: opts.alphaTest = 0.501960814; break;                     // alpha key
    case 2: Object.assign(opts, custom(THREE.SrcAlphaFactor, THREE.OneMinusSrcAlphaFactor, THREE.OneFactor, THREE.OneMinusSrcAlphaFactor)); break;
    case 3: Object.assign(opts, custom(THREE.OneFactor, THREE.OneFactor, THREE.ZeroFactor, THREE.OneFactor)); break;
    case 4: Object.assign(opts, custom(THREE.SrcAlphaFactor, THREE.OneFactor, THREE.ZeroFactor, THREE.OneFactor)); break;
    case 5: Object.assign(opts, custom(THREE.DstColorFactor, THREE.ZeroFactor)); break;
    case 6: Object.assign(opts, custom(THREE.DstColorFactor, THREE.SrcColorFactor)); break;
    case 7: Object.assign(opts, custom(THREE.OneFactor, THREE.OneMinusSrcAlphaFactor)); break;
    default: opts.alphaTest = 0.501960814;
  }
  // Blended modes never write depth (GLContext.apply_blend_mode), whatever the flags say.
  if (blendMode >= 2) opts.depthWrite = false;
  return unlit ? new THREE.MeshBasicMaterial(opts) : new THREE.MeshStandardMaterial({ ...opts, roughness: 0.85 });
}
