// The character screen's slots, laid out as a character pane: armor down the left side,
// hands to feet down the right, weapons below. Only slots that show on the model are offered: no neck,
// rings or trinkets. A character holds hand weapons or a ranged one, not both (commands.ts).
import { SLOT_LABELS } from '../labels'
import type { SlotName } from '../viewer/dress'

export interface DollSlot {
  slot: SlotName
  label: string
  /** A look can hold an item here. */
  equippable: boolean
  /** The item shows on the 3D model. */
  drawn: boolean
}

const worn = (slot: SlotName, drawn = true): DollSlot => ({ slot, label: SLOT_LABELS[slot], equippable: true, drawn })

export const DOLL: { left: DollSlot[]; right: DollSlot[]; bottom: DollSlot[] } = {
  left: [worn('head'), worn('shoulder'), worn('back'), worn('chest'), worn('shirt'), worn('tabard'), worn('wrist')],
  right: [worn('hands'), worn('waist'), worn('legs'), worn('feet')],
  bottom: [worn('mainhand'), worn('offhand'), worn('ranged')],
}
