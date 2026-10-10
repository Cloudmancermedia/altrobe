import * as THREE from 'three'
import { describe, expect, test } from 'vitest'
import { plateLayout, screenPoint } from './nameplate'

describe('screenPoint', () => {
  const camera = new THREE.PerspectiveCamera(35, 2, 0.01, 100)
  camera.position.set(5, 1, 0)
  camera.lookAt(0, 1, 0)
  camera.updateMatrixWorld()

  test('a point the camera looks at lands in the middle of the cell', () => {
    const p = screenPoint(new THREE.Vector3(0, 1, 0), camera, 400, 200)!
    expect(p.x).toBeCloseTo(200)
    expect(p.y).toBeCloseTo(100)
  })

  test('a point above the target lands higher on screen', () => {
    expect(screenPoint(new THREE.Vector3(0, 1.5, 0), camera, 400, 200)!.y).toBeLessThan(100)
  })

  test('a point behind the camera has no place on screen', () => {
    expect(screenPoint(new THREE.Vector3(10, 1, 0), camera, 400, 200)).toBeNull()
  })
})

describe('plateLayout', () => {
  // A character whose head is at y 200 and feet at y 1000 on screen: 800 px tall.
  test('sits above the head by the game\'s share of the character height, with the game\'s text size', () => {
    const p = plateLayout({ x: 400, y: 200 }, { x: 400, y: 1000 })
    expect(p.x).toBe(400)
    expect(p.bottom).toBeCloseTo(200 - 800 * 0.26)
    expect(p.fontSize).toBeCloseTo(800 * 0.075)
  })

  test('big races (Orc, Tauren) get extra room above the head; others do not', () => {
    expect(plateLayout({ x: 0, y: 200 }, { x: 0, y: 1000 }, 2).bottom).toBeLessThan(plateLayout({ x: 0, y: 200 }, { x: 0, y: 1000 }, 1).bottom)
    expect(plateLayout({ x: 0, y: 200 }, { x: 0, y: 1000 }, 6).bottom).toBeLessThan(plateLayout({ x: 0, y: 200 }, { x: 0, y: 1000 }, 5).bottom)
    expect(plateLayout({ x: 0, y: 200 }, { x: 0, y: 1000 }, 5).bottom).toBeCloseTo(200 - 800 * 0.26)
  })

  test('the text stays readable on a small character and does not swamp a close-up', () => {
    expect(plateLayout({ x: 0, y: 100 }, { x: 0, y: 140 }).fontSize).toBe(12)
    expect(plateLayout({ x: 0, y: 0 }, { x: 0, y: 3000 }).fontSize).toBe(64)
  })
})
