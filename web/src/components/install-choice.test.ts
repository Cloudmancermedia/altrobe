import { describe, expect, it } from 'vitest'
import type { Status } from '../api/types'
import { foreverChoice, installChoices, openFailedNotice } from './install-choice'

const status = (products: { product: string; build: string; isForever?: boolean }[][]): Status => ({
  version: '0.1.0', active: null, cacheFolder: '/tmp',
  installs: products.map((p, i) => ({ path: `/wow${i}`, products: p })),
})

describe('foreverChoice', () => {
  it('picks the only Forever product', () => {
    const s = status([[{ product: 'wow', build: '11.2' }, { product: 'wow_classic_beta', build: '1.60.1.70009', isForever: true }]])
    expect(foreverChoice(s)).toEqual({ path: '/wow0', product: 'wow_classic_beta' })
  })

  it('asks the user when there is no Forever product', () => {
    expect(foreverChoice(status([[{ product: 'wow', build: '11.2' }]]))).toBeNull()
  })

  it('asks the user when there are several Forever products', () => {
    const f = { product: 'wow_classic_beta', build: '1.60.1.70009', isForever: true }
    expect(foreverChoice(status([[f], [f]]))).toBeNull()
  })
})

describe('installChoices', () => {
  it('labels Forever products and lists them first', () => {
    const s = status([[{ product: 'wow', build: '11.2' }, { product: 'wow_classic_beta', build: '1.60.1.70009', isForever: true }]])
    expect(installChoices(s).map((c) => c.label)).toEqual(['Forever · wow_classic_beta 1.60.1.70009 · /wow0', 'wow 11.2 · /wow0'])
  })
})

describe('openFailedNotice', () => {
  it('shows the server message once, without doubled punctuation', () => {
    expect(openFailedNotice({ path: '/wow', product: 'wow_classic_beta' }, 'Battle.net is still updating this install. Let the update finish, then try again.'))
      .toBe('Could not open wow_classic_beta at /wow: Battle.net is still updating this install. Let the update finish, then try again. Choose an install.')
    expect(openFailedNotice({ path: '/wow', product: 'wow' }, 'network down'))
      .toBe('Could not open wow at /wow: network down. Choose an install.')
  })
})
