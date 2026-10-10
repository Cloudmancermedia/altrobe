import { Bone, MathUtils, PerspectiveCamera, Quaternion, Vector3 } from 'three'
import { describe, expect, test } from 'vitest'
import { faceCamera } from './billboard'

// The Devout Mantle shoulder (143310): bone 1 carries the tassel, has flag 0x8 and hangs off bone 0,
// which the Undead shoulder attachment rotates hard. Here the parent gets 70 degrees about X.
function chain() {
  const parent = new Bone()
  parent.quaternion.setFromAxisAngle(new Vector3(1, 0, 0), MathUtils.degToRad(70))
  parent.position.set(0.3, 1.5, 0.1)
  const child = new Bone()
  child.position.set(0, 0.05, 0)
  parent.add(child)
  parent.updateMatrixWorld(true)
  return { parent, child }
}

function cameraAt(x: number, y: number, z: number) {
  const camera = new PerspectiveCamera()
  camera.position.set(x, y, z)
  camera.lookAt(0, 1.5, 0)
  camera.updateMatrixWorld(true)
  return camera
}

const angle = (a: Vector3, b: Vector3) => MathUtils.radToDeg(a.angleTo(b))

describe('faceCamera', () => {
  test('drops the parent rotation, so a level front camera leaves the bone unrotated and hanging world-down', () => {
    const { parent, child } = chain()
    faceCamera(child, new Quaternion(), cameraAt(5, 1.5, 0))
    parent.updateMatrixWorld(true)
    const down = new Vector3(0, -1, 0).applyQuaternion(child.getWorldQuaternion(new Quaternion()))
    expect(angle(down, new Vector3(0, -1, 0))).toBeLessThan(1)
    // Model +X (the way a WoW model faces) points back at the camera.
    const forward = new Vector3(1, 0, 0).applyQuaternion(child.getWorldQuaternion(new Quaternion()))
    expect(angle(forward, new Vector3(1, 0, 0))).toBeLessThan(1)
  })

  test('follows the camera: model up is screen up and model +X points at the camera', () => {
    const { parent, child } = chain()
    const camera = cameraAt(-2, 3, 4)
    faceCamera(child, new Quaternion(), camera)
    parent.updateMatrixWorld(true)
    const q = child.getWorldQuaternion(new Quaternion())
    const camUp = new Vector3(0, 1, 0).applyQuaternion(camera.quaternion)
    const camBack = new Vector3(0, 0, 1).applyQuaternion(camera.quaternion)
    expect(angle(new Vector3(0, 1, 0).applyQuaternion(q), camUp)).toBeLessThan(1)
    expect(angle(new Vector3(1, 0, 0).applyQuaternion(q), camBack)).toBeLessThan(1)
  })

  test("keeps the bone's own animated rotation on top of the billboard", () => {
    const { parent, child } = chain()
    const spin = new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), MathUtils.degToRad(30))
    faceCamera(child, spin, cameraAt(5, 1.5, 0))
    parent.updateMatrixWorld(true)
    expect(child.getWorldQuaternion(new Quaternion()).angleTo(spin)).toBeLessThan(MathUtils.degToRad(1))
  })
})
