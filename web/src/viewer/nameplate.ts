// Places the nameplate: an HTML label that follows a point above the character's head on screen.
import * as THREE from 'three'

/** Where `world` appears in a `width` x `height` view, in pixels from its top left; null when behind the camera. */
export function screenPoint(world: THREE.Vector3, camera: THREE.Camera, width: number, height: number): { x: number; y: number } | null {
  const v = world.clone().project(camera)
  if (v.z < -1 || v.z > 1) return null
  return { x: (v.x + 1) / 2 * width, y: (1 - v.y) / 2 * height }
}

// Measured from an in-game screenshot (a High Warlord nameplate): the name's text is about 7.5% of the
// character's height on screen; the bottom of the guild line sits about a quarter of that height above
// the head.
const TEXT_SHARE = 0.075
const GAP_SHARE = 0.26
// Orcs (2) and Tauren (6) are so broad and stooped that the same gap looks cramped on them.
const BIG_RACES = new Set([2, 6])
const BIG_RACE_EXTRA = 0.08

/**
 * Where the nameplate goes, from the top of the head and the feet on screen: centred on x, its bottom
 * edge at `bottom`, text at `fontSize` px (12 to 64, so it stays readable and never swamps a close-up).
 */
export function plateLayout(head: { x: number; y: number }, feet: { x: number; y: number }, race?: number): { x: number; bottom: number; fontSize: number } {
  const height = Math.max(0, feet.y - head.y)
  const gap = GAP_SHARE + (race !== undefined && BIG_RACES.has(race) ? BIG_RACE_EXTRA : 0)
  return { x: head.x, bottom: head.y - height * gap, fontSize: Math.min(64, Math.max(12, height * TEXT_SHARE)) }
}
