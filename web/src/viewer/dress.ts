// Item dressing. Pure; no three.js or DOM.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: slot tables, texture layer order and attachment
// IDs (src/js/wow/EquipmentSlots.js), item geoset groups and their priorities, helmet hide groups
// (src/js/db/caches/DBItemGeosets.js), item texture sections and the forced alpha blend
// (src/js/modules/tab_characters.js update_textures), shoulder left/right by PositionIndex
// (src/js/db/caches/DBItemModels.js).
//
// Input: a base look and the resolved item data for each equipped item. Output: extra texture layers
// for the compositor, the geosets to show, replaceable textures for the body model (the cape), and
// the item models to attach to the body's attachment points.
//
// Changes from wow.export:
// - Cloaks paint their texture onto the body's own cape geoset (15xx, texture type 2) when the item
//   has no model. wow.export only handles cape models.
// - A dress state is a plain object: { character, items: { <slotId>: itemId } }.

import type { BaseLook, ItemEffect, ResolvedItem, TextureLayer } from '../api/types'

export const SHOULDER_SLOT_L = 3
export const SHOULDER_SLOT_R = 30

// Inventory type -> equipment slot (EquipmentSlots.js INVENTORY_TYPE_TO_SLOT_ID).
export const INVENTORY_TYPE_TO_SLOT: Record<number, number> = {
  1: 1, 2: 2, 3: SHOULDER_SLOT_L, 4: 4, 5: 5, 6: 6, 7: 7, 8: 8, 9: 9, 10: 10,
  13: 16, 14: 17, 15: 18, 16: 15, 17: 16, 19: 19, 20: 5, 21: 16, 22: 17, 23: 17, 25: 18, 26: 18,
}

// Slot names used by saved looks (look.ts), mapped to the equipment slot IDs above. The right
// shoulder (30) has no name: it always holds the same item as "shoulder".
export const SLOT_NAMES = {
  head: 1, neck: 2, shoulder: SHOULDER_SLOT_L, shirt: 4, chest: 5, waist: 6, legs: 7, feet: 8,
  wrist: 9, hands: 10, back: 15, mainhand: 16, offhand: 17, ranged: 18, tabard: 19,
} as const
export type SlotName = keyof typeof SLOT_NAMES
export const SLOT_ORDER = Object.keys(SLOT_NAMES) as SlotName[]
export const isSlotName = (s: string): s is SlotName => Object.hasOwn(SLOT_NAMES, s)
const SLOT_BY_ID = Object.fromEntries(Object.entries(SLOT_NAMES).map(([n, id]) => [id, n])) as Record<number, SlotName>

/** The look slot name for an inventory type, or null when items of that type are not worn. */
export function slotNameForInventoryType(inventoryType: number): SlotName | null {
  return SLOT_BY_ID[INVENTORY_TYPE_TO_SLOT[inventoryType]] ?? null
}

/** Slots an item of this inventory type may go in. One-handers can also be held in the off hand. */
export function slotsForInventoryType(inventoryType: number): SlotName[] {
  const s = slotNameForInventoryType(inventoryType)
  if (!s) return []
  return inventoryType === 13 ? [s, 'offhand'] : [s]
}

/** Slots an item fits besides the one it equips to by default, such as the off hand for a one-hander. */
export function otherSlots(item: { slot: string; inventoryType?: number }): SlotName[] {
  return slotsForInventoryType(item.inventoryType ?? 0).filter((s) => s !== item.slot)
}

const MAIN_HAND_SLOT = 16
const RANGED_SLOT = 18
const BOW_SUBCLASS = 2 // a bow is held in the left hand; guns, crossbows, thrown and wands in the right
const WEAPON_SLOTS = new Set([MAIN_HAND_SLOT, 17, RANGED_SLOT])
const HELD_IN_OFF_HAND = 23
const FIST_WEAPON = 13 // Item.SubclassID of fist weapons

// Where a sheathed weapon goes, by Item.SheatheType: the attachment for the off hand, and for the main
// hand the mirror on the other side, so a pair crosses (WoW Model Viewer, database.cpp and WoWItem.cpp:
// 1 one-hand on the back 27/26, 2 large weapon 30/31, 3 hip 32/33, 4 shield on the back 28).
const SHEATH: Record<number, { main: number; off: number }> = {
  1: { main: 26, off: 27 }, 2: { main: 31, off: 30 }, 3: { main: 33, off: 32 }, 4: { main: 28, off: 28 },
}

// M2 attachment IDs (EquipmentSlots.js ATTACHMENT_ID) per slot, in ItemDisplayInfo model order.
export const ATTACHMENT = { HAND_RIGHT: 1, HAND_LEFT: 2, SHOULDER_RIGHT: 5, SHOULDER_LEFT: 6, HELMET: 11, BACK: 12, SHIELD: 0 }
const SLOT_TO_ATTACHMENT: Record<number, number[]> = {
  1: [ATTACHMENT.HELMET],
  [SHOULDER_SLOT_L]: [ATTACHMENT.SHOULDER_LEFT],
  [SHOULDER_SLOT_R]: [ATTACHMENT.SHOULDER_RIGHT],
  15: [ATTACHMENT.BACK],
  18: [ATTACHMENT.HAND_RIGHT], // ranged; a bow goes in the left hand (see below)
  16: [ATTACHMENT.HAND_RIGHT],
  17: [ATTACHMENT.HAND_LEFT, ATTACHMENT.SHIELD],
}
const SHIELD_INVENTORY_TYPE = 14

// Texture layer priority per slot: lower draws first (EquipmentSlots.js SLOT_LAYER).
const SLOT_LAYER: Record<number, number> = { 4: 10, 7: 10, 1: 11, 8: 11, 3: 13, 30: 13, 5: 13, 19: 17, 6: 18, 9: 19, 10: 20, 16: 21, 17: 22, 15: 23 }

// Character geoset groups (DBItemGeosets.js CG), only the ones items touch.
const CG = { SLEEVES: 8, KNEEPADS: 9, CHEST: 10, PANTS: 11, TABARD: 12, TROUSERS: 13, CLOAK: 15, BELT: 18,
  FEET: 20, SKULL: 21, TORSO: 22, HAND_ATTACHMENT: 23, SHOULDERS: 26, HELM: 27, ARM_UPPER: 28, GLOVES: 4, BOOTS: 5 }

// Slot -> which ItemDisplayInfo.GeosetGroup index drives which character geoset group.
const SLOT_GEOSET_MAPPING: Record<number, { index: number; group: number; feet?: boolean }[]> = {
  1: [{ index: 0, group: CG.HELM }, { index: 1, group: CG.SKULL }],
  3: [{ index: 0, group: CG.SHOULDERS }],
  30: [{ index: 0, group: CG.SHOULDERS }],
  4: [{ index: 0, group: CG.SLEEVES }, { index: 1, group: CG.CHEST }],
  5: [{ index: 0, group: CG.SLEEVES }, { index: 1, group: CG.CHEST }, { index: 2, group: CG.TROUSERS }, { index: 3, group: CG.TORSO }, { index: 4, group: CG.ARM_UPPER }],
  6: [{ index: 0, group: CG.BELT }],
  7: [{ index: 0, group: CG.PANTS }, { index: 1, group: CG.KNEEPADS }, { index: 2, group: CG.TROUSERS }],
  8: [{ index: 0, group: CG.BOOTS }, { index: 1, group: CG.FEET, feet: true }],
  9: [],
  10: [{ index: 0, group: CG.GLOVES }, { index: 1, group: CG.HAND_ATTACHMENT }],
  15: [{ index: 0, group: CG.CLOAK }],
  19: [{ index: 0, group: CG.TABARD }],
}

// When several slots set the same group, the first slot listed wins, skipping slots whose GeosetGroup
// value is 0: that item has no opinion on the group (a chest that is not a robe leaves a kilt alone).
const GEOSET_PRIORITY: Record<number, number[]> = {
  [CG.SLEEVES]: [10, 5, 4], [CG.CHEST]: [5, 4], [CG.TROUSERS]: [5, 7], [CG.TABARD]: [19], [CG.CLOAK]: [15],
  [CG.BELT]: [6], [CG.FEET]: [8], [CG.TORSO]: [5], [CG.HAND_ATTACHMENT]: [10], [CG.HELM]: [1],
  [CG.ARM_UPPER]: [5], [CG.SKULL]: [1], [CG.SHOULDERS]: [3, 30], [CG.BOOTS]: [8], [CG.GLOVES]: [10],
  [CG.PANTS]: [7], [CG.KNEEPADS]: [7],
}

export interface DressState {
  character: string
  /** Equipment slot ID -> item ID. */
  items: Record<number, number>
  /** Weapons put away: on the back or hip by their sheath type; a ranged weapon isn't shown. */
  sheathed?: boolean
}

export interface Attachment {
  slot: number
  itemId: number
  attachmentId: number
  modelFileDataId: number
  replaceableTextures: Record<number, number>
  /** Effect models to hang on this model's own attachment points (the item's first model only). */
  effects?: ItemEffect[]
}

export interface Dressed {
  layers: TextureLayer[]
  geosets: number[]
  geosetChanges: { group: number; geoset: number; fromSlot: number }[]
  replaceableTextures: Record<number, number>
  attachments: Attachment[]
  notes: string[]
}

type DressLook = Pick<BaseLook, 'sections' | 'sectionLayers' | 'geosets' | 'options'>

/** Works out everything the viewer needs to dress a character. */
export function dress(look: DressLook, state: DressState, resolvedById: Map<number, ResolvedItem>): Dressed {
  const notes: string[] = []
  const slots = Object.entries(state.items).map(([s, id]) => ({ slot: Number(s), item: resolvedById.get(id) }))
    .filter((e): e is { slot: number; item: ResolvedItem } => {
      if (!e.item) notes.push(`slot ${e.slot}: no resolved data`)
      return !!e.item && !e.item.error
    })

  // 1. Texture layers for body sections.
  const sectionRect = new Map(look.sections.map((s) => [s.sectionType, s]))
  const sectionLayer = new Map(look.sectionLayers.map((s) => [s.sectionType, s]))
  const layers: TextureLayer[] = []
  for (const { slot, item } of slots) {
    if (slot === SHOULDER_SLOT_R) continue // same item as the left slot; paint once
    for (const bt of item.bodyTextures ?? []) {
      const rect = sectionRect.get(bt.section)
      const target = sectionLayer.get(bt.section)
      const fileDataId = bt.textures[0]?.fileDataId
      if (!rect || !target || !fileDataId) {
        notes.push(`item ${item.itemId} section ${bt.section}: ${!rect ? 'no section rectangle' : !target ? 'no texture layer' : 'no texture'}`)
        continue
      }
      layers.push({
        textureType: target.textureType,
        // Same ordering key as wow.export's texture target ID (slot layer * 100 + section), which puts
        // every item layer above the customization layers (layer < 100).
        layer: (SLOT_LAYER[slot] ?? 10) * 100 + bt.section,
        blendMode: target.blendMode,
        section: { sectionType: rect.sectionType, x: rect.x, y: rect.y, width: rect.width, height: rect.height },
        fileDataId,
        itemId: item.itemId,
        slot,
      })
    }
  }
  layers.sort((a, b) => a.layer - b.layer)

  // 2. Geosets. Every group an item touches is cleared (xx01-xx99), then its chosen value shown.
  const show = new Set(look.geosets)
  const perGroup = new Map<number, { slot: number; value: number; set: boolean }[]>()
  for (const { slot, item } of slots) {
    for (const m of SLOT_GEOSET_MAPPING[slot] ?? []) {
      const g = item.geosetGroup?.[m.index] ?? 0
      // wow.export: value is 1 + GeosetGroup[n]; feet use GeosetGroup[1] as is, and 2 when it is 0.
      const value = m.feet ? (g === 0 ? 2 : g) : 1 + g
      if (!perGroup.has(m.group)) perGroup.set(m.group, [])
      perGroup.get(m.group)!.push({ slot, value, set: g !== 0 })
    }
  }
  const geosetChanges: Dressed['geosetChanges'] = []
  for (const [group, entries] of perGroup) {
    const order = GEOSET_PRIORITY[group]
    const ranked = order ? order.map((s) => entries.find((e) => e.slot === s)).filter((e) => e !== undefined) : entries
    const pick = ranked.find((e) => e.set) ?? ranked[0]
    clearGroup(show, group)
    if (pick) {
      show.add(group * 100 + pick.value)
      geosetChanges.push({ group, geoset: group * 100 + pick.value, fromSlot: pick.slot })
    }
  }
  // Helmet hides (hair, ears, facial features). HideGeosetGroup values are group numbers, already
  // filtered by race and sex in the resolver. A hide with option flags spares its group when the
  // customization option filling that group has one of them: Helm of Might keeps the Undead jaw.
  const head = slots.find((s) => s.slot === 1)?.item
  const hides = head?.helmHides ?? (head?.helmHideGeosetGroups ?? []).map((group) => ({ group, keepWithOptionFlags: 0 }))
  for (const { group, keepWithOptionFlags } of hides) {
    const kept = keepWithOptionFlags > 0 && (look.options ?? []).some((o) =>
      ((o.flags ?? 0) & keepWithOptionFlags) !== 0 && (o.geosets ?? []).some((g) => Math.floor(g / 100) === group))
    if (!kept) clearGroup(show, group)
  }

  // 3. Replaceable textures on the body: a cloak without a model paints the cape geoset (type 2).
  const replaceableTextures: Record<number, number> = {}
  const back = slots.find((s) => s.slot === 15)?.item
  if (back) {
    const noModel = back.models.every((m) => m.models.length === 0)
    const tex = back.models.find((m) => m.textures.length)?.textures[0]?.fileDataId
    if (noModel && tex) replaceableTextures[2] = tex
  }

  // 4. Attached item models.
  const attachments: Attachment[] = []
  for (const { slot, item } of slots) {
    // A shield hangs on the shield point (0) on the forearm, not in the left hand (2) that wow.export's
    // table lists first for the off hand; ItemResolver does the same.
    let ids = item.inventoryType === SHIELD_INVENTORY_TYPE ? [ATTACHMENT.SHIELD]
      : slot === RANGED_SLOT && item.itemSubclass === BOW_SUBCLASS ? [ATTACHMENT.HAND_LEFT]
      : SLOT_TO_ATTACHMENT[slot]
    if (!ids) continue
    if (state.sheathed && WEAPON_SLOTS.has(slot)) {
      // Classic shows no stowed ranged weapon. Others go to their sheath; a sheath type of 0 stays in hand.
      if (slot === RANGED_SLOT) continue
      // Tomes, orbs and other held-in-off-hand items (inventory type 23) and fist weapons stay in hand.
      const sheath = item.inventoryType === HELD_IN_OFF_HAND || item.itemSubclass === FIST_WEAPON ? undefined : SHEATH[item.sheatheType ?? 0]
      if (sheath) ids = [slot === MAIN_HAND_SLOT ? sheath.main : sheath.off]
    }
    let picks: { model: { fileDataId: number }; textures: { fileDataId: number }[] }[]
    if (slot === SHOULDER_SLOT_L || slot === SHOULDER_SLOT_R) {
      // Both display slots list the same pair; PositionIndex 0 is the left model, 1 the right.
      const displaySlot = slot === SHOULDER_SLOT_L ? 0 : 1
      const want = slot === SHOULDER_SLOT_L ? 0 : 1
      const src = item.models[displaySlot] ?? item.models[0]
      const model = src?.models.find((m) => m.position === want) ?? src?.models[displaySlot]
      picks = model ? [{ model, textures: src.textures }] : []
    } else {
      picks = item.models.filter((m) => m.models.length).map((m) => ({ model: m.models[0], textures: m.textures }))
    }
    picks.slice(0, ids.length).forEach((p, i) => attachments.push({
      slot, itemId: item.itemId, attachmentId: ids[i],
      modelFileDataId: p.model.fileDataId,
      // Item models use texture type 2 for the item's own skin.
      replaceableTextures: p.textures[0] ? { 2: p.textures[0].fileDataId } : {},
      ...(i === 0 && item.effects?.length ? { effects: item.effects } : {}),
    }))
  }

  return { layers, geosets: [...show].sort((a, b) => a - b), geosetChanges, replaceableTextures, attachments, notes }
}

function clearGroup(show: Set<number>, group: number) {
  for (const id of [...show]) if (id >= group * 100 + 1 && id <= group * 100 + 99) show.delete(id)
}
