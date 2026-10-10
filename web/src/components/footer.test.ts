import { describe, expect, test } from 'vitest'
import { footerLinks } from './footer'

describe('footerLinks', () => {
  test('every page links to a GitHub issue for problems and rights concerns, and to the source', () => {
    const labels = footerLinks({ installs: true, session: true, assistant: true }).map((l) => l.label)
    expect(labels).toEqual(['Report a problem or rights concern', 'Source code', 'Item names: cmangos'])
    expect(footerLinks({ installs: true, session: true, assistant: true })[0].href).toBe('https://github.com/Cloudmancermedia/altrobe/issues/new')
  })

  test('the hosted site, which has no install picker, also offers the local app', () => {
    const links = footerLinks({ installs: false, session: false, assistant: false })
    expect(links.map((l) => l.label)).toEqual(['Download the local app', 'Report a problem or rights concern', 'Source code', 'Item names: cmangos'])
    expect(links[0].href).toBe('https://github.com/Cloudmancermedia/altrobe/releases/latest')
  })
})
