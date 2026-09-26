// Item dressing for the browser. No dependencies.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: slot tables, texture layer order and attachment
// IDs (src/js/wow/EquipmentSlots.js), item geoset groups and their priorities, helmet hide groups
// (src/js/db/caches/DBItemGeosets.js), item texture sections and the forced alpha blend
// (src/js/modules/tab_characters.js update_textures), shoulder left/right by PositionIndex
// (src/js/db/caches/DBItemModels.js).
//
// Input: a base look (output/looks/<name>.json, with sections and sectionLayers), and the resolved
// item JSON for each equipped item (output/resolved/<character>/<itemId>.json). Output: extra
// texture layers for the compositor, the geosets to show, replaceable textures for the body model
// (the cape), and the item models to attach to the body's attachment points.
//
// Changes from wow.export:
// - Cloaks paint their texture onto the body's own cape geoset (15xx, texture type 2) when the item
//   has no model. wow.export only handles cape models.
// - A look state is a plain object: { character, items: { <slotId>: itemId } }.

export const SHOULDER_SLOT_L = 3;
export const SHOULDER_SLOT_R = 30;

// Inventory type -> equipment slot (EquipmentSlots.js INVENTORY_TYPE_TO_SLOT_ID).
export const INVENTORY_TYPE_TO_SLOT = {
  1: 1, 2: 2, 3: SHOULDER_SLOT_L, 4: 4, 5: 5, 6: 6, 7: 7, 8: 8, 9: 9, 10: 10,
  13: 16, 14: 17, 15: 16, 16: 15, 17: 16, 19: 19, 20: 5, 21: 16, 22: 17, 23: 17, 26: 16,
};

// M2 attachment IDs (EquipmentSlots.js ATTACHMENT_ID) per slot, in ItemDisplayInfo model order.
export const ATTACHMENT = { HAND_RIGHT: 1, HAND_LEFT: 2, SHOULDER_RIGHT: 5, SHOULDER_LEFT: 6, HELMET: 11, BACK: 12, SHIELD: 0 };
const SLOT_TO_ATTACHMENT = {
  1: [ATTACHMENT.HELMET],
  [SHOULDER_SLOT_L]: [ATTACHMENT.SHOULDER_LEFT],
  [SHOULDER_SLOT_R]: [ATTACHMENT.SHOULDER_RIGHT],
  15: [ATTACHMENT.BACK],
  16: [ATTACHMENT.HAND_RIGHT],
  17: [ATTACHMENT.HAND_LEFT, ATTACHMENT.SHIELD],
};

// Texture layer priority per slot: lower draws first (EquipmentSlots.js SLOT_LAYER).
const SLOT_LAYER = { 4: 10, 7: 10, 1: 11, 8: 11, 3: 13, 30: 13, 5: 13, 19: 17, 6: 18, 9: 19, 10: 20, 16: 21, 17: 22, 15: 23 };

// Character geoset groups (DBItemGeosets.js CG), only the ones items touch.
const CG = { SLEEVES: 8, KNEEPADS: 9, CHEST: 10, PANTS: 11, TABARD: 12, TROUSERS: 13, CLOAK: 15, BELT: 18,
  FEET: 20, SKULL: 21, TORSO: 22, HAND_ATTACHMENT: 23, SHOULDERS: 26, HELM: 27, ARM_UPPER: 28, GLOVES: 4, BOOTS: 5 };

// Slot -> which ItemDisplayInfo.GeosetGroup index drives which character geoset group.
const SLOT_GEOSET_MAPPING = {
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
};

// When several slots set the same group, the first slot listed wins.
const GEOSET_PRIORITY = {
  [CG.SLEEVES]: [10, 5, 4], [CG.CHEST]: [5, 4], [CG.TROUSERS]: [5, 7], [CG.TABARD]: [19], [CG.CLOAK]: [15],
  [CG.BELT]: [6], [CG.FEET]: [8], [CG.TORSO]: [5], [CG.HAND_ATTACHMENT]: [10], [CG.HELM]: [1],
  [CG.ARM_UPPER]: [5], [CG.SKULL]: [1], [CG.SHOULDERS]: [3, 30], [CG.BOOTS]: [8], [CG.GLOVES]: [10],
  [CG.PANTS]: [7], [CG.KNEEPADS]: [7],
};

/**
 * Builds a look state from a character name and resolved items, placing each item in its slot.
 * Shoulders fill both the left and right shoulder slots, as in wow.export.
 * @param {string} character  e.g. "orc-male"
 * @param {object[]} resolvedItems  output/resolved/<character>/<itemId>.json contents
 */
export function lookStateFor(character, resolvedItems) {
  const items = {};
  for (const r of resolvedItems) {
    const slot = INVENTORY_TYPE_TO_SLOT[r.inventoryType];
    if (slot === undefined) continue;
    items[slot] = r.itemId;
    if (slot === SHOULDER_SLOT_L) items[SHOULDER_SLOT_R] = r.itemId;
  }
  return { character, items };
}

/**
 * Works out everything the viewer needs to dress a character.
 * @param {object} look  base look (output/looks/<name>.json)
 * @param {{character:string, items:Object<string,number>}} state  look state
 * @param {Map<number,object>} resolvedById  resolved item JSON by item ID
 * @returns {{layers:object[], geosets:number[], replaceableTextures:Object<number,number>, attachments:object[], notes:string[]}}
 */
export function dress(look, state, resolvedById) {
  const notes = [];
  const slots = Object.entries(state.items).map(([s, id]) => ({ slot: Number(s), item: resolvedById.get(id) }))
    .filter(({ slot, item }) => {
      if (!item) notes.push(`slot ${slot}: no resolved data`);
      return !!item && !item.error;
    });

  // 1. Texture layers for body sections.
  const sectionRect = new Map(look.sections.map((s) => [s.sectionType, s]));
  const sectionLayer = new Map(look.sectionLayers.map((s) => [s.sectionType, s]));
  const layers = [];
  for (const { slot, item } of slots) {
    if (slot === SHOULDER_SLOT_R) continue; // same item as the left slot; paint once
    for (const bt of item.bodyTextures ?? []) {
      const rect = sectionRect.get(bt.section);
      const target = sectionLayer.get(bt.section);
      const fileDataId = bt.textures[0]?.fileDataId;
      if (!rect || !target || !fileDataId) {
        notes.push(`item ${item.itemId} section ${bt.section}: ${!rect ? 'no section rectangle' : !target ? 'no texture layer' : 'no texture'}`);
        continue;
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
      });
    }
  }
  layers.sort((a, b) => a.layer - b.layer);

  // 2. Geosets. Every group an item touches is cleared (xx01-xx99), then its chosen value shown.
  const show = new Set(look.geosets);
  const perGroup = new Map();
  for (const { slot, item } of slots) {
    for (const m of SLOT_GEOSET_MAPPING[slot] ?? []) {
      const g = item.geosetGroup?.[m.index] ?? 0;
      // wow.export: value is 1 + GeosetGroup[n]; feet use GeosetGroup[1] as is, and 2 when it is 0.
      const value = m.feet ? (g === 0 ? 2 : g) : 1 + g;
      if (!perGroup.has(m.group)) perGroup.set(m.group, []);
      perGroup.get(m.group).push({ slot, value });
    }
  }
  const geosetChanges = [];
  for (const [group, entries] of perGroup) {
    const order = GEOSET_PRIORITY[group];
    const pick = order ? order.map((s) => entries.find((e) => e.slot === s)).find(Boolean) : entries[0];
    clearGroup(show, group);
    if (pick) {
      show.add(group * 100 + pick.value);
      geosetChanges.push({ group, geoset: group * 100 + pick.value, fromSlot: pick.slot });
    }
  }
  // Helmet hides (hair, ears, facial features). HideGeosetGroup values are group numbers, already
  // filtered by race and sex in the resolver.
  const head = slots.find((s) => s.slot === 1)?.item;
  for (const group of head?.helmHideGeosetGroups ?? []) clearGroup(show, group);

  // 3. Replaceable textures on the body: a cloak without a model paints the cape geoset (type 2).
  const replaceableTextures = {};
  const back = slots.find((s) => s.slot === 15)?.item;
  if (back) {
    const noModel = back.models.every((m) => m.models.length === 0);
    const tex = back.models.find((m) => m.textures.length)?.textures[0]?.fileDataId;
    if (noModel && tex) replaceableTextures[2] = tex;
  }

  // 4. Attached item models.
  const attachments = [];
  for (const { slot, item } of slots) {
    const ids = SLOT_TO_ATTACHMENT[slot];
    if (!ids) continue;
    let picks;
    if (slot === SHOULDER_SLOT_L || slot === SHOULDER_SLOT_R) {
      // Both display slots list the same pair; PositionIndex 0 is the left model, 1 the right.
      const displaySlot = slot === SHOULDER_SLOT_L ? 0 : 1;
      const want = slot === SHOULDER_SLOT_L ? 0 : 1;
      const src = item.models[displaySlot] ?? item.models[0];
      const model = src?.models.find((m) => m.position === want) ?? src?.models[displaySlot];
      picks = model ? [{ model, textures: src.textures }] : [];
    } else {
      picks = item.models.filter((m) => m.models.length).map((m) => ({ model: m.models[0], textures: m.textures }));
    }
    picks.slice(0, ids.length).forEach((p, i) => attachments.push({
      slot, itemId: item.itemId, attachmentId: ids[i],
      modelFileDataId: p.model.fileDataId,
      // Item models use texture type 2 for the item's own skin.
      replaceableTextures: p.textures[0] ? { 2: p.textures[0].fileDataId } : {},
    }));
  }

  return { layers, geosets: [...show].sort((a, b) => a - b), geosetChanges, replaceableTextures, attachments, notes };
}

function clearGroup(show, group) {
  for (const id of [...show]) if (id >= group * 100 + 1 && id <= group * 100 + 99) show.delete(id);
}
