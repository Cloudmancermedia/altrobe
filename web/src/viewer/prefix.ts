import type { AnimationClip, Object3D } from 'three'

// "__", not "/": three.js track paths treat "/" as a directory separator, so "item/bone_0.quaternion"
// would bind to the first node named "bone_0" anywhere, which is the body's bone.
export const ITEM_NAME_SEPARATOR = '__'

export const itemRootName = (itemId: number, modelFileDataId: number) => `item_${itemId}_${modelFileDataId}`

/**
 * Item models with a skeleton (weapons) name their bones bone_<n>, like the body. The body's mixer
 * binds tracks by node name across the whole scene, so an unprefixed item bone can capture a body
 * track and freeze that body bone. This renames every node under the item root, and every track of
 * the item's own clips to match. Mutates both.
 */
export function prefixItemNames(root: Object3D, clips: AnimationClip[], rootName: string) {
  root.name = rootName
  const prefix = rootName + ITEM_NAME_SEPARATOR
  root.traverse((o) => { if (o !== root) o.name = prefix + o.name })
  for (const c of clips) for (const t of c.tracks) t.name = prefix + t.name
}
