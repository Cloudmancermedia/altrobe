// Character texture compositor for the browser. No dependencies.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: CharMaterialRenderer (section-to-quad
// placement, per-blend-mode GL state) and the char fragment shader (multiply, overlay, screen).
//
// Changes from wow.export:
// - Layers draw in ChrModelTextureLayer.Layer order (wow.export sorts by texture target ID; the
//   two orders agree for the default looks).
// - The backdrop for multiply/overlay/screen is copied on the GPU (copyTexImage2D) and sampled by
//   gl_FragCoord, instead of reading pixels back to the CPU.
// - Alpha-blended modes use separate alpha factors (ONE, ONE_MINUS_SRC_ALPHA) so layer alpha does
//   not eat into the destination alpha.
//
// Output is raw RGBA (not premultiplied), rows top-down (row 0 = texture v = 0), which matches
// glTF UVs with texture.flipY = false.

import type { LookTexture, TextureLayer } from '../api/types'

const VERT = `
attribute vec2 a_position;
attribute vec2 a_texCoord;
varying vec2 v_texCoord;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
  v_texCoord = a_texCoord;
}`

const FRAG = `
precision mediump float;
varying vec2 v_texCoord;
uniform sampler2D u_texture;
uniform sampler2D u_baseTexture;
uniform vec2 u_size;
uniform float u_blendMode;
void main() {
  vec4 blend = texture2D(u_texture, v_texCoord);
  vec4 base = texture2D(u_baseTexture, gl_FragCoord.xy / u_size);
  if (u_blendMode == 4.0) {          // multiply
    gl_FragColor = base * blend;
  } else if (u_blendMode == 6.0) {   // overlay, keyed on the layer as in wow.export
    vec3 lo = 2.0 * base.rgb * blend.rgb;
    vec3 hi = 1.0 - 2.0 * (1.0 - base.rgb) * (1.0 - blend.rgb);
    gl_FragColor = vec4(mix(lo, hi, step(0.5, blend.rgb)), blend.a);
  } else if (u_blendMode == 7.0) {   // screen
    gl_FragColor = vec4(1.0 - (1.0 - base.rgb) * (1.0 - blend.rgb), blend.a);
  } else {                           // 0/1 blit, 9 alpha straight, 15 infer alpha, others
    gl_FragColor = blend;
  }
}`

const NEEDS_BACKDROP = new Set([4, 6, 7])


export interface SourceImage {
  bitmap: ImageBitmap
  bytes: number
}
export interface CompositedTexture {
  width: number
  height: number
  pixels: Uint8Array
}
export interface CompositeStats {
  fetchDecodeMs: number
  compositeMs: number
  downloadedBytes: number
  sourceImages: number
  missingImages: number[]
}

/** Loads an image without premultiplying alpha, so RGB survives under alpha = 0. */
export async function loadImage(url: string): Promise<SourceImage> {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`)
  const blob = await res.blob()
  const bitmap = await createImageBitmap(blob, { premultiplyAlpha: 'none', colorSpaceConversion: 'none' })
  return { bitmap, bytes: blob.size }
}

/**
 * Composites every texture type in a look (base look layers plus item layers).
 * @param urlFor  URL of the PNG for a FileDataID
 * @param options.scale  < 1 composites at a fraction of the layout size
 */
export async function compositeLook(
  look: { textures: LookTexture[]; layers: TextureLayer[] },
  urlFor: (fileDataId: number) => string,
  options: { scale?: number } = {},
): Promise<{ textures: Map<number, CompositedTexture>; stats: CompositeStats }> {
  const scale = options.scale ?? 1
  const tFetch0 = performance.now()
  const ids = [...new Set(look.layers.map((l) => l.fileDataId))]
  // A missing source image drops that layer rather than the whole character.
  const loaded = await Promise.all(ids.map(async (id) => [id, await loadImage(urlFor(id)).catch(() => null)] as const))
  const images = new Map<number, SourceImage>()
  for (const [id, img] of loaded) if (img) images.set(id, img)
  const tFetch1 = performance.now()

  const textures = new Map<number, CompositedTexture>()
  for (const tex of look.textures) {
    const layers = look.layers.filter((l) => l.textureType === tex.textureType).sort((a, b) => a.layer - b.layer)
    if (layers.length === 0) continue
    textures.set(tex.textureType, compositeLayers(tex, layers, images, scale))
  }
  for (const img of images.values()) img.bitmap.close()
  return {
    textures,
    stats: {
      fetchDecodeMs: +(tFetch1 - tFetch0).toFixed(2),
      compositeMs: +(performance.now() - tFetch1).toFixed(2),
      downloadedBytes: [...images.values()].reduce((n, i) => n + i.bytes, 0),
      sourceImages: images.size,
      missingImages: ids.filter((id) => !images.has(id)),
    },
  }
}

/** Composites one texture type from its layers, in draw order. */
export function compositeLayers(tex: LookTexture, layers: TextureLayer[], images: Map<number, SourceImage>, scale = 1): CompositedTexture {
  const width = Math.round(tex.width * scale), height = Math.round(tex.height * scale)
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const gl = canvas.getContext('webgl', { premultipliedAlpha: false, preserveDrawingBuffer: true, antialias: false, alpha: true })
  if (!gl) throw new Error('WebGL unavailable for compositing')

  const prog = program(gl, VERT, FRAG)
  gl.useProgram(prog)
  const aPos = gl.getAttribLocation(prog, 'a_position')
  const aUv = gl.getAttribLocation(prog, 'a_texCoord')
  gl.uniform1i(gl.getUniformLocation(prog, 'u_texture'), 0)
  gl.uniform1i(gl.getUniformLocation(prog, 'u_baseTexture'), 1)
  gl.uniform2f(gl.getUniformLocation(prog, 'u_size'), width, height)
  const uBlend = gl.getUniformLocation(prog, 'u_blendMode')

  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false)
  gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false)
  gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE)

  const backdrop = gl.createTexture()
  gl.activeTexture(gl.TEXTURE1)
  gl.bindTexture(gl.TEXTURE_2D, backdrop)
  setSampling(gl, gl.NEAREST)

  const posBuf = gl.createBuffer(), uvBuf = gl.createBuffer()
  gl.bindBuffer(gl.ARRAY_BUFFER, uvBuf)
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 1]), gl.STATIC_DRAW)
  gl.vertexAttribPointer(aUv, 2, gl.FLOAT, false, 0, 0)
  gl.enableVertexAttribArray(aUv)

  gl.viewport(0, 0, width, height)
  gl.disable(gl.DEPTH_TEST)
  gl.clearColor(0.5, 0.5, 0.5, 1) // wow.export's neutral grey under the first layer
  gl.clear(gl.COLOR_BUFFER_BIT)

  const sourceTextures = new Map<number, WebGLTexture>()
  for (const layer of layers) {
    const img = images.get(layer.fileDataId)
    if (!img) continue
    if (!sourceTextures.has(layer.fileDataId)) {
      const t = gl.createTexture()
      gl.activeTexture(gl.TEXTURE0)
      gl.bindTexture(gl.TEXTURE_2D, t)
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img.bitmap)
      setSampling(gl, gl.LINEAR)
      sourceTextures.set(layer.fileDataId, t)
    }

    // Section rectangle in layout pixels (y down) -> clip space where image row 0 is at y = -1,
    // so readPixels returns rows top-down.
    const s = layer.section
    const x0 = (s.x * scale) / width * 2 - 1, x1 = ((s.x + s.width) * scale) / width * 2 - 1
    const y0 = (s.y * scale) / height * 2 - 1, y1 = ((s.y + s.height) * scale) / height * 2 - 1
    gl.bindBuffer(gl.ARRAY_BUFFER, posBuf)
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([x0, y0, x1, y0, x0, y1, x0, y1, x1, y0, x1, y1]), gl.STREAM_DRAW)
    gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0)
    gl.enableVertexAttribArray(aPos)

    const mode = layer.blendMode
    if (NEEDS_BACKDROP.has(mode)) {
      gl.activeTexture(gl.TEXTURE1)
      gl.bindTexture(gl.TEXTURE_2D, backdrop)
      gl.copyTexImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 0, 0, width, height, 0)
    }
    if (mode === 0 || mode === 1) gl.disable(gl.BLEND) // straight copy, keeps the layer's alpha
    else {
      gl.enable(gl.BLEND)
      gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA)
    }
    gl.uniform1f(uBlend, mode)
    gl.activeTexture(gl.TEXTURE0)
    gl.bindTexture(gl.TEXTURE_2D, sourceTextures.get(layer.fileDataId)!)
    gl.drawArrays(gl.TRIANGLES, 0, 6)
  }

  const pixels = new Uint8Array(width * height * 4)
  gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels)
  for (const t of sourceTextures.values()) gl.deleteTexture(t)
  gl.deleteTexture(backdrop)
  // Browsers cap live WebGL contexts, so release this one; the result lives on in `pixels`.
  gl.getExtension('WEBGL_lose_context')?.loseContext()
  return { width, height, pixels }
}

function setSampling(gl: WebGLRenderingContext, filter: number) {
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
}

function program(gl: WebGLRenderingContext, vs: string, fs: string) {
  const p = gl.createProgram()
  for (const [type, text] of [[gl.VERTEX_SHADER, vs], [gl.FRAGMENT_SHADER, fs]] as const) {
    const sh = gl.createShader(type)!
    gl.shaderSource(sh, text)
    gl.compileShader(sh)
    if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh) ?? 'shader compile failed')
    gl.attachShader(p, sh)
  }
  gl.linkProgram(p)
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) ?? 'program link failed')
  return p
}
