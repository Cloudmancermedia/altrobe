// Dev-only hooks for the Playwright checks: pause the animation, pose every character at a fixed
// time, and read node positions. Nothing here runs in a production build.
import * as THREE from 'three'
import { commands, store } from './app-state'
import type { Stage } from './viewer/stage'

declare global {
  interface Window { __altrobe?: unknown }
}

export function installTestHooks(stage: () => Stage | null) {
  if (!import.meta.env.DEV) return
  const view = (i: number) => {
    const v = stage()?.views[i]
    if (!v?.character) throw new Error(`no character in view ${i}`)
    return v.character
  }
  window.__altrobe = {
    store, commands,
    /** Stops the clock and poses every character at t seconds. */
    setTime(t: number) {
      const s = stage()
      if (!s) return
      s.paused = true
      for (const v of s.views) v.character?.setTime(t)
    },
    resume() { const s = stage(); if (s) s.paused = false },
    nodeWorld(i: number, name: string) {
      const node = view(i).root.getObjectByName(name)
      return node ? node.getWorldPosition(new THREE.Vector3()).toArray() : null
    },
    drawn: (i: number) => view(i).drawn,
    attached: (i: number) => view(i).attached.map((a) => ({ itemId: a.itemId, attachmentId: a.attachmentId, rootName: a.rootName })),
    /** Each attached item node's matrix in the item root's own space, so body motion cancels out. */
    itemNodesLocal(i: number, rootName: string) {
      const c = view(i)
      const root = c.root.getObjectByName(rootName)
      if (!root) return null
      c.root.updateMatrixWorld(true)
      const inv = root.matrixWorld.clone().invert()
      const out: Record<string, number[]> = {}
      root.traverse((o) => {
        if (o === root || (o as THREE.Mesh).isMesh) return
        out[o.name] = inv.clone().multiply(o.matrixWorld).toArray()
      })
      return out
    },
  }
  return () => { delete window.__altrobe }
}
