import { AnimationClip, AnimationMixer, Bone, Group, PropertyBinding, VectorKeyframeTrack } from 'three'
import { describe, expect, test } from 'vitest'
import { ITEM_NAME_SEPARATOR, itemRootName, prefixItemNames } from './prefix'

function rig(name: string) {
  const root = new Group()
  root.name = name
  const bone = new Bone()
  bone.name = 'bone_0'
  root.add(bone)
  return { root, bone }
}
const moveClip = (path: string, x: number) =>
  new AnimationClip('Stand_0', 1, [new VectorKeyframeTrack(`${path}.position`, [0, 1], [0, 0, 0, x, 0, 0])])

describe('prefixItemNames', () => {
  test('renames the item root, every node under it and every track of its clips', () => {
    const { root, bone } = rig('scene')
    const clip = moveClip('bone_0', 1)
    prefixItemNames(root, [clip], itemRootName(19019, 148234))
    expect(root.name).toBe('item_19019_148234')
    expect(bone.name).toBe('item_19019_148234__bone_0')
    expect(clip.tracks[0].name).toBe('item_19019_148234__bone_0.position')
  })

  test('the prefixed track path binds to the whole node name, not a directory', () => {
    expect(ITEM_NAME_SEPARATOR).not.toContain('/')
    const parsed = PropertyBinding.parseTrackName('item_1_2__bone_0.quaternion')
    expect(parsed.nodeName).toBe('item_1_2__bone_0')
    // The trap the separator avoids: with "/" three.js keeps only the last path part.
    expect(PropertyBinding.parseTrackName('item_1_2/bone_0.quaternion').nodeName).toBe('bone_0')
  })

  test('an attached item no longer captures the body track, and plays its own', () => {
    const body = rig('body')
    const item = rig('item')
    const itemClip = moveClip('bone_0', 5)
    prefixItemNames(item.root, [itemClip], 'item_1_2')
    // Attach the item under the body bone, as items ride on attachment nodes.
    body.bone.add(item.root)

    const bodyMixer = new AnimationMixer(body.root)
    bodyMixer.clipAction(moveClip('bone_0', 1)).play()
    const itemMixer = new AnimationMixer(item.root)
    itemMixer.clipAction(itemClip).play()
    bodyMixer.update(0.5)
    itemMixer.update(0.5)

    expect(body.bone.position.x).toBeCloseTo(0.5)
    expect(item.bone.position.x).toBeCloseTo(2.5)
  })
})
