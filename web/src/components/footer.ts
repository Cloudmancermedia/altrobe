import type { Features } from '../api/backend'

const REPO = 'https://github.com/Cloudmancermedia/altrobe'

/**
 * The footer's links. Problems and rights concerns go to a public GitHub issue, never an email address.
 * The hosted site (no install picker) also points to the local app's releases.
 */
export function footerLinks(features: Features): { label: string; href: string }[] {
  return [
    ...(features.installs ? [] : [{ label: 'Download the local app', href: `${REPO}/releases/latest` }]),
    { label: 'Report a problem or rights concern', href: `${REPO}/issues/new` },
    { label: 'Source code', href: REPO },
    // Credit for the Classic item names the game files leave out (ClassicItemNames, GPL-3.0).
    { label: 'Item names: cmangos', href: 'https://github.com/cmangos/classic-db' },
  ]
}
