import { expect, test } from 'vitest'
import { DOLL } from './doll-layout'
import { SLOT_ART } from './slot-art'

test('every slot on the character screen has a silhouette for when it is empty', () => {
  for (const s of [...DOLL.left, ...DOLL.right, ...DOLL.bottom]) expect(SLOT_ART[s.slot], s.slot).toMatch(/^M/)
})
