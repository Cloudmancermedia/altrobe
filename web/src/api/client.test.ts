import { describe, expect, it } from 'vitest'
import { modelUrl, rememberStatus, textureUrl } from './client'
import type { Status } from './types'

const status = (assetVersion?: number): Status => ({
  version: '0.1.0-dev',
  installs: [],
  active: { path: '/wow', product: 'wow_classic_beta', build: '1.60.1.70124', ...(assetVersion === undefined ? {} : { assetVersion }) },
  cacheFolder: '/cache',
})

describe('asset URLs', () => {
  it('carry the converter version, so a new converter gets past the browser cache', () => {
    rememberStatus(status(3))
    expect(modelUrl('1.60.1.70124', 42, 'glb')).toBe('/assets/1.60.1.70124/models/42.glb?v=3')
    expect(textureUrl('1.60.1.70124', 7)).toBe('/assets/1.60.1.70124/textures/7.png?v=3')
  })

  it('leave the version off when the server does not report one', () => {
    rememberStatus(status())
    expect(modelUrl('1.60.1.70124', 42, 'json')).toBe('/assets/1.60.1.70124/models/42.json')
  })
})
