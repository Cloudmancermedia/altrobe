// One WebGLRenderer for every character on screen. The canvas sits behind the stage and each view is
// a plain element laid out on top of it; every frame draws each view's scene into that element's
// rectangle with a scissor (the three.js "multiple elements" pattern). Each view has its own scene,
// camera and orbit controls, which listen on the view's element.

import * as THREE from 'three'
import { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import type { View } from '../look/look'
import type { Character } from './character'
import { plateLayout, screenPoint } from './nameplate'

// The page background (--bg in index.css), a touch lighter so the character stands out.
const BACKGROUND = 0x0b2636

export class StageView {
  readonly scene = new THREE.Scene()
  readonly camera = new THREE.PerspectiveCamera(35, 1, 0.01, 100)
  readonly controls: OrbitControls
  readonly element: HTMLElement
  character: Character | null = null
  /** The nameplate element to keep over the character's head, if it has one. */
  nameplate: HTMLElement | null = null
  /** The character's race, for the nameplate's height (plateLayout). */
  nameplateRace: number | undefined
  /** The top of the head in the character's first animated frame, frozen so the nameplate never bobs. */
  private plateHead: THREE.Vector3 | null = null

  constructor(element: HTMLElement) {
    this.element = element
    this.scene.background = new THREE.Color(BACKGROUND)
    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x444450, 1.6))
    const sun = new THREE.DirectionalLight(0xffffff, 1.8)
    sun.position.set(2, 3, 4)
    this.scene.add(sun)
    this.controls = new OrbitControls(this.camera, element)
    this.controls.listenToKeyEvents(element) // arrow keys pan when the view has focus
  }

  /** Swaps in a new character, disposing the old one. `frame` re-aims the camera. */
  setCharacter(character: Character, frame: View | null) {
    if (this.character) {
      this.scene.remove(this.character.root)
      this.character.dispose()
    }
    this.character = character
    this.plateHead = null
    this.scene.add(character.root)
    if (frame) this.frame(frame)
  }

  frame(view: View) {
    const c = this.character
    if (!c) return
    const size = c.box.getSize(new THREE.Vector3()), center = c.box.getCenter(new THREE.Vector3())
    // Far enough to fit the height, and in a narrow cell (five or six side by side) the width too.
    // Models face +X, so the side-to-side width is the Z extent; the box is the bare body, so allow a
    // little more for shoulder pads.
    const r = this.element.getBoundingClientRect()
    const aspect = r.width > 0 && r.height > 0 ? r.width / r.height : this.camera.aspect
    const tanH = Math.tan(THREE.MathUtils.degToRad(this.camera.fov) / 2) * aspect
    const d = Math.max(size.y * 2.2, (size.z * 1.25 / 2) / tanH)
    this.controls.target.copy(center)
    // WoW models face +X, which stays +X after the Y-up conversion, so look from +X.
    if (view === 'side') this.camera.position.set(center.x, center.y, center.z + d)
    else if (view === 'back') this.camera.position.set(center.x - d, center.y, center.z)
    else this.camera.position.set(center.x + d, center.y, center.z)
    if (view === 'head') {
      // Attachment 11 (helm) rides on the head bone, so it tracks the pose; without it, fall back to
      // the top of the bind-pose box.
      const helm = c.root.getObjectByName('attachment_11')
      const head = helm ? helm.getWorldPosition(new THREE.Vector3()) : new THREE.Vector3(center.x, c.box.max.y - size.y * 0.12, center.z)
      if (helm) head.y -= size.y * 0.06
      this.controls.target.copy(head)
      this.camera.position.set(head.x + size.y * 0.75, head.y, head.z)
    }
    this.controls.update()
  }

  /**
   * Keeps the nameplate above the head: anchored where the head is in the first animated
   * frame and kept there, so it doesn't bob with the idle breathing. It moves only when the camera does,
   * and its size follows the character's height on screen.
   */
  placeNameplate(width: number, height: number) {
    const el = this.nameplate, c = this.character
    if (!el) return
    let at: ReturnType<typeof plateLayout> | null = null
    if (c) {
      const size = c.box.getSize(new THREE.Vector3()), center = c.box.getCenter(new THREE.Vector3())
      // The render loop has animated the character once before this runs, so the helm attachment (on
      // the head bone) is where the head really is in its pose, hunched or not. Taken once, then kept.
      if (!this.plateHead) {
        const helm = c.root.getObjectByName('attachment_11')
        this.plateHead = helm
          ? helm.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(0, size.y * 0.06, 0))
          : new THREE.Vector3(center.x, c.box.max.y, center.z)
      }
      const head = screenPoint(this.plateHead, this.camera, width, height)
      const feet = screenPoint(new THREE.Vector3(this.plateHead.x, c.box.min.y, this.plateHead.z), this.camera, width, height)
      if (head && feet) at = plateLayout(head, feet, this.nameplateRace)
    }
    el.style.visibility = at ? 'visible' : 'hidden'
    if (at) {
      el.style.fontSize = `${at.fontSize}px`
      el.style.transform = `translate(${at.x}px, ${at.bottom}px) translate(-50%, -100%)`
    }
  }

  dispose() {
    this.controls.dispose()
    if (this.character) {
      this.scene.remove(this.character.root)
      this.character.dispose()
      this.character = null
    }
  }
}

export class Stage {
  readonly renderer: THREE.WebGLRenderer
  readonly views: StageView[] = []
  private readonly canvas: HTMLCanvasElement
  private readonly clock = new THREE.Clock()
  paused = false

  constructor(canvas: HTMLCanvasElement) {
    this.canvas = canvas
    // preserveDrawingBuffer keeps the last frame readable for screenshots and saved images.
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true })
    this.renderer.setPixelRatio(devicePixelRatio)
    this.renderer.setScissorTest(true)
    this.renderer.setAnimationLoop(() => this.render())
  }

  addView(element: HTMLElement): StageView {
    const v = new StageView(element)
    this.views.push(v)
    return v
  }

  removeView(v: StageView) {
    const i = this.views.indexOf(v)
    if (i >= 0) this.views.splice(i, 1)
    v.dispose()
  }

  private render() {
    const dt = this.clock.getDelta()
    const { canvas, renderer } = this
    const w = canvas.clientWidth, h = canvas.clientHeight
    if (w === 0 || h === 0) return
    const size = renderer.getSize(new THREE.Vector2())
    if (size.x !== w || size.y !== h) renderer.setSize(w, h, false)
    renderer.setScissor(0, 0, w, h)
    renderer.setClearColor(BACKGROUND)
    renderer.clear()
    const box = canvas.getBoundingClientRect()
    for (const v of this.views) {
      const r = v.element.getBoundingClientRect()
      const left = r.left - box.left, bottom = box.bottom - r.bottom
      if (r.width <= 0 || r.height <= 0 || r.right < box.left || r.left > box.right || r.bottom < box.top || r.top > box.bottom) continue
      if (!this.paused) v.character?.update(dt)
      v.camera.aspect = r.width / r.height
      v.camera.updateProjectionMatrix()
      v.controls.update()
      // After the camera moves, and while paused too, so billboards follow orbiting.
      v.character?.faceCamera(v.camera)
      renderer.setViewport(left, bottom, r.width, r.height)
      renderer.setScissor(left, bottom, r.width, r.height)
      renderer.render(v.scene, v.camera)
      v.placeNameplate(r.width, r.height)
    }
  }

  dispose() {
    this.renderer.setAnimationLoop(null)
    for (const v of [...this.views]) this.removeView(v)
    this.renderer.dispose()
  }
}
