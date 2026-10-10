// Faint silhouettes for empty character-screen slots, in the spirit of the game's own (which are
// Blizzard's art, so these are drawn here from scratch). SVG paths in a 24 x 24 box, filled.
import type { DollSlot } from './doll-layout'

export const SLOT_ART: Partial<Record<DollSlot['slot'], string>> = {
  // helmet: dome with a nose guard and cheek plates
  head: 'M12 3C7.6 3 5 6.4 5 10.5V16l2.5 2.5V12h3v8h3v-8h3v6.5L19 16v-5.5C19 6.4 16.4 3 12 3z',
  // pauldron: a domed shoulder plate with a rim
  shoulder: 'M3 15c0-5 4-9 9-9s9 4 9 9v2H3v-2zm2 4h14v2H5v-2z',
  // cloak: a cape hanging from a clasp
  back: 'M9 3h6l1 2c2.5 1 4 3.5 4 6.5V21l-2.5-1.5L15 21l-3-1.5L9 21l-2.5-1.5L4 21v-9.5C4 8.5 5.5 6 8 5l1-2z',
  // breastplate: shoulders, a centre ridge and a flared waist
  chest: 'M7 3l5 2 5-2 4 3-2 5v6c0 2-2 4-7 4s-7-2-7-4v-6L3 6l4-3zm5 4v12',
  // shirt: a plain tunic with short sleeves
  shirt: 'M8 3l4 2 4-2 5 4-2.5 3L17 9v12H7V9l-1.5 1L3 7l5-4z',
  // tabard: a long panel with a hem point and an emblem
  tabard: 'M6 3h12v14l-6 4-6-4V3zm6 4l-2.5 3L12 13l2.5-3L12 7z',
  // bracer: a cuff with two straps
  wrist: 'M6 7h12l1 3-1 4H6l-1-4 1-4zm0 9h12v2H6v-2zm0-12h12v2H6V4z',
  // gauntlet: a hand with four fingers and a flared cuff
  hands: 'M8 4h1.6v7H11V3h1.6v8H14V4h1.6v8H17V7h1.6v8c0 3-2 5-5 5h-1.2c-2 0-3.4-1.2-4.2-2.8L5 13l1.5-1.2L8 13.5V4zm-1 17h9v1H7v-1z',
  // belt: a strap with a square buckle
  waist: 'M2 9h20v6H2V9zm8-1h4v8h-4V8zm1.4 2v4h1.2v-4h-1.2z',
  // legs: trousers to the ankle
  legs: 'M6 3h12l1 18h-5l-2-11-2 11H5L6 3z',
  // boot: a tall boot with a toe and a heel
  feet: 'M8 2h7v10l5 4v4H6l-1-2 2-2V2zm-2 19h15v1H6v-1z',
  // sword: blade, crossguard and grip
  mainhand: 'M17.5 2.5l4 0 0 4-9 9 1.5 1.5-1.5 1.5-1.5-1.5-3 3 1 1-1.5 1.5-4-4 1.5-1.5 1 1 3-3-1.5-1.5 1.5-1.5 1.5 1.5z',
  // shield: a kite shield with a boss
  offhand: 'M12 2l8 3v6c0 5.5-3.5 9.5-8 11-4.5-1.5-8-5.5-8-11V5l8-3zm0 6a2.5 2.5 0 100 5 2.5 2.5 0 000-5z',
  // bow: a curved limb with its string
  ranged: 'M6 2c7 3 11 7 11 10s-4 7-11 10l-.6-1C12 18 15.5 14.8 15.5 12S12 6 5.4 3L6 2zm-.6 1.4v17.2h1V3.4h-1z',
}
