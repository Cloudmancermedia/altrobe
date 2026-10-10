// Spherical billboard bones (M2 bone flag 0x8). The game drops everything the bone inherits from its
// parents' rotation and gives it a fixed orientation in view space, with the bone's own animated
// rotation on top. The shoulder tassels of the Devout Mantle (143310) hang off such a bone; without
// this they take the Undead shoulder bone's tilt and point into the chest.
//
// Reference: WebWowViewerCpp, AnimationManager::calcBoneMatrix, case 0x8
// (https://github.com/Deamon87/WebWowViewerCpp, wowViewerLib/src/engine/managers/animationManager.cpp).
// It sets the bone's view-space axes to WoW X -> view -Z, WoW Y -> view +X, WoW Z -> view +Y. That
// matrix is a reflection (determinant -1), so a quaternion cannot copy it. We keep its up (WoW Z is
// screen up) and right (WoW Y, the model's left, is screen right) and choose X so the result is a
// rotation: WoW X, the way a model faces, points back at the camera. That is the pose of an
// unrotated model looking at the viewer.
//
// Axes: the converter maps WoW (x, y, z) to glTF (x, z, -y) (importer ModelConverter.ToGltf), so the
// bone's glTF axes go X -> camera +Z (toward the viewer), Y -> camera +Y, Z (WoW -Y) -> camera -X.
// That is camera rotation * a -90 degree turn about Y.

import { Quaternion, Vector3, type Camera, type Object3D } from 'three'

/** M2 bone flag 0x8, spherical_billboard (https://wowdev.wiki/M2#Bones). */
export const SPHERICAL_BILLBOARD = 0x8

const cameraToBone = new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), -Math.PI / 2)
const parentWorld = new Quaternion()
const cameraWorld = new Quaternion()

/**
 * Sets `bone.quaternion` so its world rotation is the billboard rotation for `camera` times `local`,
 * the bone's own animated rotation.
 */
export function faceCamera(bone: Object3D, local: Quaternion, camera: Camera) {
  if (bone.parent) bone.parent.getWorldQuaternion(parentWorld)
  else parentWorld.identity()
  camera.getWorldQuaternion(cameraWorld)
  bone.quaternion.copy(parentWorld.invert()).multiply(cameraWorld).multiply(cameraToBone).multiply(local)
}
