import { resolve } from 'node:path'
import { describe, expect, test } from 'vitest'
import { bundleFile, contentType } from './bundle-server.ts'

describe('bundle server', () => {
  const root = '/srv/bundle'

  test('maps a /bundle URL to a file under the bundle folder', () => {
    expect(bundleFile(root, '/bundle/wow_classic_beta/current.json')).toBe(resolve(root, 'wow_classic_beta/current.json'))
    expect(bundleFile(root, '/bundle/wow_classic_beta/1.60.1.70245/v2/models/42.glb?v=2')).toBe(resolve(root, 'wow_classic_beta/1.60.1.70245/v2/models/42.glb'))
  })

  test('refuses anything outside it', () => {
    expect(bundleFile(root, '/bundle/../secret.txt')).toBeNull()
    expect(bundleFile(root, '/bundle/%2e%2e/secret.txt')).toBeNull()
    expect(bundleFile(root, '/elsewhere/current.json')).toBeNull()
  })

  test('types the files a bundle has', () => {
    expect(contentType('a.json')).toBe('application/json')
    expect(contentType('a.glb')).toBe('model/gltf-binary')
    expect(contentType('a.png')).toBe('image/png')
  })
})
