// Builds one dressed character as a three.js object: body model, composited skin, attached items,
// and their animation mixers.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: item models placed at bone * T(attachment
// position) (src/js/3D/renderers/M2RendererGL.js getAttachmentTransform).

import * as THREE from 'three'
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js'
import type { BaseLook, ModelMeta } from '../api/types'
import { compositeLook } from './compositor'
import { faceCamera, SPHERICAL_BILLBOARD } from './billboard'
import type { Attachment, Dressed } from './dress'
import { m2Material } from './m2-material'
import { createParticleMesh, type ParticleMesh } from './particle-mesh'
import { ITEM_NAME_SEPARATOR, itemRootName, prefixItemNames } from './prefix'
import { clipSeconds } from './timing'

export interface AssetUrls {
  model: (fileDataId: number, ext: 'glb' | 'json') => string
  texture: (fileDataId: number) => string
  /** One animation for a body model's bones (converted by the server on first request). */
  anim: (modelFileDataId: number, animId: number) => string
}

export interface CharacterInput {
  baseLook: BaseLook
  dressed: Dressed
  assets: AssetUrls
  /**
   * Item animation speed. Our timing matches the file and wow.export's playback, but Thunderfury
   * looked 3-4x faster than in game; 0.5 was the closest match by eye. This is
   * a stand-in, not a verified cause.
   */
  itemSpeed?: number
  /** Animation ID to play instead of Stand (0). */
  animId?: number
}

export interface AttachedItem extends Attachment {
  rootName: string
  submeshes: number
}

export interface Character {
  root: THREE.Object3D
  /** Bind-pose box of the shown geosets, for framing. */
  box: THREE.Box3
  attached: AttachedItem[]
  /** What was drawn: the geosets shown and the texture files composited, in layer order. */
  drawn: { geosets: number[]; layerFiles: number[] }
  notices: string[]
  mixer: THREE.AnimationMixer | null
  itemMixers: THREE.AnimationMixer[]
  update(dt: number): void
  /** Poses the body and every item at time t (seconds), for reproducible checks. */
  setTime(t: number): void
  /** Turns the items' billboard bones to `camera`. Call once per frame, after update and camera moves. */
  faceCamera(camera: THREE.Camera): void
  dispose(): void
}

// Geometry and materials are per character; downloads and plain PNG textures are shared, because
// every character renders through the one WebGLRenderer.
const bufferCache = new Map<string, Promise<ArrayBuffer>>()
const jsonCache = new Map<string, Promise<unknown>>()
const pngCache = new Map<string, Promise<THREE.Texture | null>>()
const loader = new GLTFLoader()

function fetchOnce<T>(cache: Map<string, Promise<T>>, url: string, read: (r: Response) => Promise<T>): Promise<T> {
  if (!cache.has(url)) {
    cache.set(url, fetch(url).then((r) => {
      if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`)
      return read(r)
    }).catch((e) => { cache.delete(url); throw e }))
  }
  return cache.get(url)!
}

async function loadModel(assets: AssetUrls, fdid: number) {
  const [meta, buffer] = await Promise.all([
    fetchOnce(jsonCache, assets.model(fdid, 'json'), (r) => r.json()) as Promise<ModelMeta>,
    fetchOnce(bufferCache, assets.model(fdid, 'glb'), (r) => r.arrayBuffer()),
  ])
  const gltf = await loader.parseAsync(buffer, '')
  return { meta, gltf }
}

function pngTexture(assets: AssetUrls, fileDataId: number, m2TextureFlags = 0): Promise<THREE.Texture | null> {
  const url = assets.texture(fileDataId)
  const key = `${url}:${m2TextureFlags & 3}`
  if (!pngCache.has(key)) {
    pngCache.set(key, new THREE.TextureLoader().loadAsync(url).then((tex) => {
      tex.flipY = false // glTF/WoW UV origin is top-left
      tex.colorSpace = THREE.SRGBColorSpace
      // M2 texture flags: 0x1 wrap U, 0x2 wrap V (https://wowdev.wiki/M2#Textures).
      tex.wrapS = m2TextureFlags & 1 ? THREE.RepeatWrapping : THREE.ClampToEdgeWrapping
      tex.wrapT = m2TextureFlags & 2 ? THREE.RepeatWrapping : THREE.ClampToEdgeWrapping
      return tex
    }, () => null))
  }
  return pngCache.get(key)!
}

const grey = new THREE.MeshStandardMaterial({ color: 0x8a9a6a, roughness: 0.8 })

const meshesOf = (root: THREE.Object3D) => {
  const list: THREE.Mesh[] = []
  root.traverse((o) => { if ((o as THREE.Mesh).isMesh) list.push(o as THREE.Mesh) })
  return list
}
const userData = (o: THREE.Object3D) => ({ ...o.parent?.userData, ...o.userData }) as { geosetId?: number; submeshIndex?: number }

export async function buildCharacter({ baseLook, dressed, assets, itemSpeed = 0.5, animId = 0 }: CharacterInput): Promise<Character> {
  const notices: string[] = []
  const look = { ...baseLook, layers: [...baseLook.layers, ...dressed.layers], geosets: dressed.geosets }
  const show = new Set(look.geosets)

  // Composite every character texture type (1 skin, 6 hair, 19 eyes) in the browser, item layers included.
  const [{ textures }, body] = await Promise.all([
    compositeLook(look, assets.texture),
    loadModel(assets, baseLook.modelFileDataId),
  ])
  const composited = new Map<number, THREE.DataTexture>()
  for (const [type, t] of textures) {
    const tex = new THREE.DataTexture(t.pixels, t.width, t.height, THREE.RGBAFormat)
    tex.flipY = false
    tex.colorSpace = THREE.SRGBColorSpace
    tex.magFilter = THREE.LinearFilter
    tex.minFilter = THREE.LinearMipmapLinearFilter
    tex.generateMipmaps = true
    tex.needsUpdate = true
    composited.set(type, tex)
  }

  // Material for one submesh: its first texture unit's first texture, drawn with the unit's M2
  // material. Texture type 0 loads its hardcoded FileDataID, `replaceable` maps other types to a
  // FileDataID (the cape, item skins), and the rest use the composited character textures.
  const materials = new Map<string, THREE.Material>()
  async function submeshMaterial(meta: ModelMeta, submeshIndex: number | undefined, replaceable: Record<number, number>) {
    const unit = meta.geosets.find((g) => g.submeshIndex === submeshIndex)?.textureUnits?.[0]
    const first = unit?.textures?.[0]
    if (!unit || !first) return { material: grey }
    const flags = meta.textures[first.textureIndex]?.flags ?? 0
    let tex: THREE.Texture | null = null
    let key: string
    if (first.type === 0 && first.fileDataId) { tex = await pngTexture(assets, first.fileDataId, flags); key = `png${first.fileDataId}` }
    else if (replaceable[first.type]) { tex = await pngTexture(assets, replaceable[first.type], flags); key = `png${replaceable[first.type]}` }
    else { tex = composited.get(first.type) ?? null; key = `chr${first.type}` }
    if (!tex) return { material: grey, missingType: first.type }
    key += `:${unit.blendMode}:${unit.materialFlags}:${flags & 3}`
    if (!materials.has(key)) materials.set(key, m2Material(tex, unit.blendMode ?? 0, unit.materialFlags ?? 0))
    return { material: materials.get(key)!, blendMode: unit.blendMode }
  }

  const root = body.gltf.scene
  const box = new THREE.Box3()
  for (const o of meshesOf(root)) {
    const data = userData(o)
    o.material = (await submeshMaterial(body.meta, data.submeshIndex, dressed.replaceableTextures)).material
    o.visible = data.geosetId !== undefined && show.has(data.geosetId)
    // Every primitive shares one vertex buffer, so this is the bind-pose box of the whole model; it
    // avoids Box3.expandByObject, which skins every vertex on the CPU.
    if (o.visible) {
      if (!o.geometry.boundingBox) o.geometry.computeBoundingBox()
      box.union(o.geometry.boundingBox!)
    }
  }

  // Item models ride on the body's attachment nodes (children of the animated bones), so they follow
  // the Stand animation. Each attachment node's transform is bone * T(attachment position).
  const attached: AttachedItem[] = []
  const itemMixers: THREE.AnimationMixer[] = []
  const itemRoots: THREE.Object3D[] = []
  // Spherical billboard bones on items (M2 bone flag 0x8). `rest` is the glTF pose, restored before
  // each mixer step since a bone without tracks keeps whatever rotation faceCamera last gave it;
  // `local` is the bone's animated rotation after that step.
  const billboards: { bone: THREE.Object3D; rest: THREE.Quaternion; local: THREE.Quaternion }[] = []
  // Particle emitters (glows on shoulders and weapons) run on real time: their rates and lifespans are
  // in seconds, unlike the item animation speed fudge above.
  const emitters: ParticleMesh[] = []
  // Sets up one item or effect model under `rootName`: materials, billboard bones, its own animation
  // clock and particle emitters. Returns its meshes.
  const mount = async (item: Awaited<ReturnType<typeof loadModel>>, rootName: string, replaceableTextures: Record<number, number>, seed: number) => {
    const list = meshesOf(item.gltf.scene)
    for (const o of list) {
      o.material = (await submeshMaterial(item.meta, userData(o).submeshIndex, replaceableTextures)).material
      o.frustumCulled = false // bounds are in the item's bind pose, not where the bone carries it
    }
    prefixItemNames(item.gltf.scene, item.gltf.animations, rootName)
    for (const b of item.meta.bones ?? []) {
      if (!(b.flags & SPHERICAL_BILLBOARD)) continue
      const bone = item.gltf.scene.getObjectByName(rootName + ITEM_NAME_SEPARATOR + b.node)
      if (bone) billboards.push({ bone, rest: bone.quaternion.clone(), local: bone.quaternion.clone() })
    }
    // Items animate on their own clock. Weapons like Thunderfury drive their glow planes with global
    // sequences only, so play Stand (if it has channels) plus every global-sequence clip.
    const itemClips = (item.meta.animations ?? [])
      .filter((m) => m.name && (m.globalSequence !== undefined || (m.id === 0 && m.variation === 0)))
      .map((m) => ({ m, c: item.gltf.animations.find((c) => c.name === m.name) }))
      .filter((x): x is { m: typeof x.m; c: THREE.AnimationClip } => !!x.c)
    if (itemClips.length) {
      const itemMixer = new THREE.AnimationMixer(item.gltf.scene)
      itemMixer.timeScale = itemSpeed
      for (const { m, c } of itemClips) {
        c.duration = clipSeconds(m.durationMs)
        itemMixer.clipAction(c).play()
      }
      itemMixers.push(itemMixer)
    }
    for (const e of item.meta.particles ?? []) {
      const bone = e.boneNode ? item.gltf.scene.getObjectByName(rootName + ITEM_NAME_SEPARATOR + e.boneNode) : item.gltf.scene
      if (!bone) continue
      const map = e.textureFileDataId ? await pngTexture(assets, e.textureFileDataId, item.meta.textures[e.textureIndex]?.flags) : null
      const pm = createParticleMesh(e, map, seed + e.index)
      bone.add(pm.object)
      emitters.push(pm)
    }
    return list
  }

  await Promise.all(dressed.attachments.map(async (a) => {
    const node = root.getObjectByName(`attachment_${a.attachmentId}`)
    if (!node) { notices.push(`no attachment ${a.attachmentId} on the body model for item ${a.itemId}`); return }
    let item
    try { item = await loadModel(assets, a.modelFileDataId) } catch { notices.push(`item ${a.itemId}: model ${a.modelFileDataId} is missing`); return }
    const rootName = itemRootName(a.itemId, a.modelFileDataId)
    const list = await mount(item, rootName, a.replaceableTextures, a.itemId * 31 + a.attachmentId * 7)
    // Weapon glows that aren't part of the weapon's own model (ItemVisuals) hang on its attachment points.
    await Promise.all((a.effects ?? []).map(async (fx) => {
      const point = item.gltf.scene.getObjectByName(rootName + ITEM_NAME_SEPARATOR + `attachment_${fx.attachmentId}`)
      // Weapons usually have all five points but not always (The Unstoppable Force has 2-4); a column
      // without its point has nowhere to go, so it is skipped quietly.
      if (!point) return
      let effect
      try { effect = await loadModel(assets, fx.modelFileDataId) } catch { notices.push(`item ${a.itemId}: effect model ${fx.modelFileDataId} is missing`); return }
      await mount(effect, `${rootName}_fx${fx.attachmentId}`, {}, a.itemId * 31 + 1000 + fx.attachmentId * 7)
      point.add(effect.gltf.scene)
    }))
    node.add(item.gltf.scene)
    itemRoots.push(item.gltf.scene)
    attached.push({ ...a, rootName, submeshes: list.length })
  }))

  // The chosen animation on loop: Stand (ID 0, first variation) comes with the model; any other is its
  // own file of the same bones, whose tracks bind to the body's bone nodes by name. The converter
  // records each sequence's real duration; glTF clips otherwise end at their last keyframe.
  let mixer: THREE.AnimationMixer | null = null
  let main: THREE.AnimationClip | undefined
  if (animId !== 0) {
    try {
      const gltf = await loader.parseAsync(await fetchOnce(bufferCache, assets.anim(baseLook.modelFileDataId, animId), (r) => r.arrayBuffer()), '')
      main = gltf.animations[0]
      const ms = (gltf.parser.json.animations?.[0]?.extras as { durationMs?: number } | undefined)?.durationMs
      if (main && ms !== undefined) main.duration = clipSeconds(ms)
    } catch (e) {
      notices.push(`animation ${animId} could not load (${(e as Error).message}); standing`)
    }
  }
  if (!main) {
    const standMeta = body.meta.animations?.find((a) => a.id === 0 && a.variation === 0 && a.name)
    main = standMeta ? body.gltf.animations.find((c) => c.name === standMeta.name) : undefined
    if (standMeta && main) main.duration = clipSeconds(standMeta.durationMs)
  }
  if (main) {
    mixer = new THREE.AnimationMixer(root)
    mixer.clipAction(main).play()
  }
  // Global sequences run on their own loop whatever else plays. Character models use them for fixed
  // per-race tweaks, e.g. the scale of the shoulder attachment bones (1.7 on the Orc male, 0.65 on the
  // Undead female), so without them shoulder items come out the same size on every race.
  for (const a of body.meta.animations ?? []) {
    const c = a.globalSequence !== undefined && a.name ? body.gltf.animations.find((x) => x.name === a.name) : undefined
    if (!c) continue
    c.duration = clipSeconds(a.durationMs)
    mixer ??= new THREE.AnimationMixer(root)
    mixer.clipAction(c).play()
  }
  mixer?.update(0)
  root.updateMatrixWorld(true)

  return {
    root, box, attached, notices, mixer, itemMixers,
    drawn: { geosets: look.geosets, layerFiles: look.layers.map((l) => l.fileDataId) },
    update(dt) {
      for (const b of billboards) b.bone.quaternion.copy(b.rest)
      mixer?.update(dt)
      for (const m of itemMixers) m.update(dt)
      for (const b of billboards) b.local.copy(b.bone.quaternion)
      for (const e of emitters) e.update(dt)
    },
    setTime(t) {
      for (const b of billboards) b.bone.quaternion.copy(b.rest)
      mixer?.setTime(t)
      for (const m of itemMixers) m.setTime(t)
      for (const b of billboards) b.local.copy(b.bone.quaternion)
      for (const e of emitters) e.setTime(t)
      root.updateMatrixWorld(true)
    },
    faceCamera(camera) {
      for (const b of billboards) faceCamera(b.bone, b.local, camera)
    },
    dispose() {
      mixer?.stopAllAction()
      for (const m of itemMixers) m.stopAllAction()
      for (const e of emitters) e.dispose()
      for (const r of [root, ...itemRoots]) for (const o of meshesOf(r)) {
        o.geometry.dispose()
        ;(o as THREE.SkinnedMesh).skeleton?.dispose()
      }
      for (const m of materials.values()) m.dispose()
      for (const t of composited.values()) t.dispose()
    },
  }
}
