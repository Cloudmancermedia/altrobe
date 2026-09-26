// Spike Step 4a: pick the default customization for a bare character and resolve it to the
// geosets to show and the texture layers to composite. Reads the JSON tables the probe exported
// into output/tables/. Run with Node 24+:
//   node tools/looks/looks.ts
// Writes output/looks/<name>.json and prints a summary.
//
// Portions ported from wow.export (https://github.com/Kruithne/wow.export), MIT License,
// Copyright (c) Kruithne and Marlamin. Ported logic: default geoset reset rule and per-option
// geoset toggling (src/js/ui/character-appearance.js), related-choice gating and texture layer
// section lookup (same file), and race/gender texture selection
// (src/js/db/caches/DBComponentTextureFileData.js).
//
// Where this deliberately differs from wow.export (see output/looks/*.json "notes"):
// - Options with flag 0x20 still get a default choice. wow.export leaves them unset, which drops
//   the Undead jaw geoset (202) and leaves a hole under the face.
// - Choices are filtered by ChrCustomizationReq for a fresh non-Death-Knight character.
// - Every element of a choice counts, not only the last one read.
// - Ears default to 702, not 701 (701 renders the Orc without ears).

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";

type Row = Record<string, any>;
const root = join(import.meta.dirname, "../..");
const tablesDir = join(root, "output/tables");

const cache = new Map<string, Row[]>();
function table(name: string): Row[] {
  if (!cache.has(name)) cache.set(name, JSON.parse(readFileSync(join(tablesDir, `${name}.json`), "utf8")).rows);
  return cache.get(name)!;
}
function groupBy(rows: Row[], key: string): Map<number, Row[]> {
  const m = new Map<number, Row[]>();
  for (const r of rows) {
    const k = r[key];
    if (!m.has(k)) m.set(k, []);
    m.get(k)!.push(r);
  }
  return m;
}
function byId(rows: Row[], key = "ID"): Map<number, Row> {
  return new Map(rows.map((r) => [r[key], r]));
}

// The two spike characters. Class 1 (Warrior) stands in for "any class that is not a Death Knight":
// several choices are split by class mask into Death Knight and everyone else.
const CHARACTERS = [
  { name: "orc-male", race: 2, sex: 0, classId: 1 },
  { name: "undead-female", race: 5, sex: 1, classId: 1 },
];
// overrideArchive client setting. -1 on a requirement means "any"; the Undead "Fresh" skin type
// and its skin colors need 1, the "Bony" ones need 0. We assume the default client value 0.
const OVERRIDE_ARCHIVE = 0;

const raceXModel = table("ChrRaceXChrModel");
const chrModel = byId(table("ChrModel"));
const optionsByModel = groupBy(table("ChrCustomizationOption"), "ChrModelID");
const choicesByOption = groupBy(table("ChrCustomizationChoice"), "ChrCustomizationOptionID");
const elementsByChoice = groupBy(table("ChrCustomizationElement"), "ChrCustomizationChoiceID");
const custGeoset = byId(table("ChrCustomizationGeoset"));
const custMaterial = byId(table("ChrCustomizationMaterial"));
const req = byId(table("ChrCustomizationReq"));
const texturesByRes = groupBy(table("TextureFileData"), "MaterialResourcesID");
const componentTexture = byId(table("ComponentTextureFileData"));
const layersByLayout = groupBy(table("ChrModelTextureLayer"), "CharComponentTextureLayoutsID");
const materialsByLayout = groupBy(table("ChrModelMaterial"), "CharComponentTextureLayoutsID");
const sectionsByLayout = groupBy(table("CharComponentTextureSections"), "CharComponentTextureLayoutID");
const layouts = byId(table("CharComponentTextureLayouts"));
const displayInfo = byId(table("CreatureDisplayInfo"));
const modelData = byId(table("CreatureModelData"));

// Returns null if the requirement passes, else the reason it fails.
function reqFailure(reqId: number, classId: number): string | null {
  if (!reqId) return null;
  const r = req.get(reqId);
  if (!r) return `req ${reqId} missing`;
  // WoWDBDefs documents only ReqType &1 = class required. Bit 2 is on the plain "everyone" requirement
  // (141, ReqType 3), so it is not a lock. Bit 4 without bit 2 (req 10) is only on extra skin and hair
  // colors, which look like unlockables, so a fresh character is assumed not to have them.
  if (r.ReqType & 4 && !(r.ReqType & 2)) return `req ${reqId} ReqType ${r.ReqType} (assumed locked)`;
  if (r.ReqType & 1 && !(r.ClassMask & (1 << (classId - 1)))) return `req ${reqId} class mask ${r.ClassMask}`;
  if (r.OverrideArchive !== -1 && r.OverrideArchive !== OVERRIDE_ARCHIVE) return `req ${reqId} overrideArchive ${r.OverrideArchive}`;
  if (r.ReqAchievementID || r.ReqQuestID || r.ReqItemModifiedAppearanceID) return `req ${reqId} needs unlock`;
  return null;
}

// Ported from wow.export DBComponentTextureFileData.getTextureForRaceGender.
const GENDER_ANY = 3;
function textureForRaceGender(fdids: number[], raceId: number, gender: number, classId: number): number | null {
  if (fdids.length === 0) return null;
  if (fdids.length === 1) return fdids[0];
  const candidates = fdids.map((fdid) => ({ fdid, info: componentTexture.get(fdid) }));
  if (!candidates.some((c) => c.info)) return fdids[0];
  const tagged = candidates.filter((c) => c.info && (c.info.GenderIndex === gender || c.info.GenderIndex === GENDER_ANY) && (c.info.ClassID === 0 || c.info.ClassID === classId));
  if (tagged.length === 0) return candidates.find((c) => !c.info)?.fdid ?? null;
  const rank = (i: Row) => [i.GenderIndex === gender ? 0 : 1, i.ClassID === classId ? 0 : 1, i.RaceID === raceId ? 0 : 1];
  tagged.sort((a, b) => {
    const ra = rank(a.info!), rb = rank(b.info!);
    return ra[0] - rb[0] || ra[1] - rb[1] || ra[2] - rb[2];
  });
  return tagged[0].fdid;
}

function geosetOf(id: number): number {
  const g = custGeoset.get(id)!;
  return g.GeosetType * 100 + g.GeosetID;
}

mkdirSync(join(root, "output/looks"), { recursive: true });

for (const ch of CHARACTERS) {
  const link = raceXModel.find((r) => r.ChrRacesID === ch.race && r.Sex === ch.sex)!;
  const model = chrModel.get(link.ChrModelID)!;
  const layoutId = model.CharComponentTextureLayoutID;
  // Body model: ChrModel.DisplayID -> CreatureDisplayInfo.ModelID -> CreatureModelData.FileDataID.
  const bodyFdid: number = modelData.get(displayInfo.get(model.DisplayID)!.ModelID)!.FileDataID;
  const modelMeta = JSON.parse(readFileSync(join(root, `output/models/${bodyFdid}.json`), "utf8"));
  const meshGeosets: number[] = [...new Set<number>(modelMeta.geosets.map((g: Row) => g.geosetId))].sort((a, b) => a - b);
  const notes: string[] = [];

  // 1. Default choice per option: first eligible choice by OrderIndex, then ID.
  const options = [...(optionsByModel.get(model.ID) ?? [])].sort((a, b) => a.OrderIndex - b.OrderIndex);
  const chosen: Row[] = [];
  for (const opt of options) {
    const optFail = reqFailure(opt.Requirement, ch.classId);
    const all = [...(choicesByOption.get(opt.ID) ?? [])].sort((a, b) => a.OrderIndex - b.OrderIndex || a.ID - b.ID);
    const eligible = all.filter((c) => !reqFailure(c.ChrCustomizationReqID, ch.classId));
    const pick = optFail ? undefined : eligible[0];
    const wowExportPick = opt.Flags & 0x20 ? null : [...all].sort((a, b) => a.ID - b.ID)[0]?.ID ?? null;
    chosen.push({
      optionId: opt.ID, option: opt.Name_lang, optionFlags: opt.Flags,
      choiceId: pick?.ID ?? null, choice: pick?.Name_lang ?? "", orderIndex: pick?.OrderIndex ?? null,
      skipped: optFail ?? (pick ? undefined : "no eligible choice"),
      eligibleChoices: eligible.length, totalChoices: all.length, wowExportChoiceId: wowExportPick,
    });
  }
  const active = chosen.filter((c) => c.choiceId != null);
  const activeIds = new Set(active.map((c) => c.choiceId));

  // 2. Geosets. Reset rule from wow.export: 0, every xx01, every 32xx, minus groups 17 and 35.
  // With nothing equipped, the xx01 rule is also the "bare" state for equipment groups
  // (4xx hands, 5xx boots, 13xx legs, 20xx feet, 22xx torso...).
  const show = new Set<number>();
  for (const id of meshGeosets) {
    const s = String(id);
    // Ears (7xx) are the exception: 701 is the "no ears" state helmets switch to (wowdev's
    // Character_Customization geoset table marks it DNE), and the Orc renders earless with it.
    const isDefault = id === 0 || (s.endsWith("01") && !s.startsWith("7")) || s.startsWith("32") || id === 702;
    const hidden = s.startsWith("17") || s.startsWith("35");
    if (isDefault && !hidden) show.add(id);
  }
  // Then per active option: every geoset any choice of the option names is turned off, and the
  // chosen choice's geosets are turned on.
  const fromChoices: Row[] = [];
  for (const c of active) {
    const optionGeosets = new Set<number>();
    for (const other of choicesByOption.get(c.optionId) ?? [])
      for (const e of elementsByChoice.get(other.ID) ?? []) if (e.ChrCustomizationGeosetID) optionGeosets.add(geosetOf(e.ChrCustomizationGeosetID));
    const mine = (elementsByChoice.get(c.choiceId) ?? []).filter((e) => e.ChrCustomizationGeosetID).map((e) => geosetOf(e.ChrCustomizationGeosetID));
    for (const g of optionGeosets) if (g !== 0) show.delete(g);
    for (const g of mine) show.add(g);
    if (mine.length) fromChoices.push({ optionId: c.optionId, choiceId: c.choiceId, geosets: mine });
  }
  const missing = [...show].filter((g) => !meshGeosets.includes(g));
  if (missing.length) notes.push(`Choices name geosets the body mesh does not have (ignored): ${missing.join(",")}`);
  const geosets = [...show].filter((g) => meshGeosets.includes(g)).sort((a, b) => a - b);

  // 3. Texture layers: choice -> element -> ChrCustomizationMaterial -> target -> layer + section.
  const layers: Row[] = [];
  const unsupported: Row[] = [];
  const sections = sectionsByLayout.get(layoutId) ?? [];
  for (const c of active) {
    for (const e of elementsByChoice.get(c.choiceId) ?? []) {
      for (const k of ["ChrCustomizationSkinnedModelID", "ChrCustomizationCondModelID", "ChrCustomizationDisplayInfoID", "ChrCustItemGeoModifyID"])
        if (e[k]) unsupported.push({ choiceId: c.choiceId, [k]: e[k] });
      if (!e.ChrCustomizationMaterialID) continue;
      if (e.RelatedChrCustomizationChoiceID && !activeIds.has(e.RelatedChrCustomizationChoiceID)) continue;
      const mat = custMaterial.get(e.ChrCustomizationMaterialID)!;
      const target = mat.ChrModelTextureTargetID;
      const layer = (layersByLayout.get(layoutId) ?? []).find((l) => l.ChrModelTextureTargetID[0] === target);
      if (!layer) {
        notes.push(`Choice ${c.choiceId} material ${mat.ID} targets ${target}, which layout ${layoutId} has no layer for (skipped)`);
        continue;
      }
      const texMat = (materialsByLayout.get(layoutId) ?? []).find((m) => m.TextureType === layer.TextureType)!;
      let section;
      if (layer.TextureSectionTypeBitMask === -1) section = { sectionType: -1, x: 0, y: 0, width: texMat.Width, height: texMat.Height };
      else {
        const s = sections.find((s) => (1 << s.SectionType) & layer.TextureSectionTypeBitMask);
        if (!s) { notes.push(`No section for layer ${layer.ID} mask ${layer.TextureSectionTypeBitMask}`); continue; }
        section = { sectionType: s.SectionType, x: s.X, y: s.Y, width: s.Width, height: s.Height };
      }
      const candidates = (texturesByRes.get(mat.MaterialResourcesID) ?? []).filter((t) => t.UsageType === 0).map((t) => t.FileDataID);
      const fileDataId = textureForRaceGender(candidates, ch.race, ch.sex, ch.classId);
      if (fileDataId == null) { notes.push(`Material ${mat.ID} (resources ${mat.MaterialResourcesID}) has no texture file`); continue; }
      layers.push({
        textureType: layer.TextureType, layer: layer.Layer, blendMode: layer.BlendMode, target,
        section, fileDataId, choiceId: c.choiceId, optionId: c.optionId, materialResourcesId: mat.MaterialResourcesID,
        ...(candidates.length > 1 ? { candidates } : {}),
      });
    }
  }
  layers.sort((a, b) => a.textureType - b.textureType || a.layer - b.layer);
  const textures = (materialsByLayout.get(layoutId) ?? []).map((m) => ({ textureType: m.TextureType, width: m.Width, height: m.Height }));

  // 4. Where item textures go (used in the browser by tools/viewer-test/dress.js). Every section rectangle
  // of the layout, and for item component sections 0-8 the texture layer that paints them. Ported from
  // wow.export tab_characters.js update_textures: the first layer (in table order) whose section mask
  // includes the section, else the full-size skin layer (mask -1, texture type 1). Items blend over the
  // skin, so blit modes 0/1 become 15 (alpha), as in wow.export.
  const layoutLayers = layersByLayout.get(layoutId) ?? [];
  const baseLayer = layoutLayers.find((l) => l.TextureSectionTypeBitMask === -1 && l.TextureType === 1);
  const sectionLayers: Row[] = [];
  for (let sectionType = 0; sectionType < 9; sectionType++) {
    const l = layoutLayers.find((l) => l.TextureSectionTypeBitMask !== -1 && (1 << sectionType) & l.TextureSectionTypeBitMask) ?? baseLayer;
    if (!l) continue;
    sectionLayers.push({ sectionType, textureType: l.TextureType, blendMode: l.BlendMode === 0 || l.BlendMode === 1 ? 15 : l.BlendMode, fromLayer: l.ID });
  }
  const allSections = sections.map((s) => ({ sectionType: s.SectionType, x: s.X, y: s.Y, width: s.Width, height: s.Height }));

  const look = {
    name: ch.name, race: ch.race, sex: ch.sex, classId: ch.classId, overrideArchive: OVERRIDE_ARCHIVE,
    chrModelId: model.ID, modelFileDataId: bodyFdid, textureLayoutId: layoutId, layout: layouts.get(layoutId),
    choices: chosen, geosets, geosetsFromChoices: fromChoices, textures, layers,
    sections: allSections, sectionLayers, unsupportedElements: unsupported, notes,
  };
  writeFileSync(join(root, `output/looks/${ch.name}.json`), JSON.stringify(look, null, 2));
  console.log(`${ch.name}: ChrModel ${model.ID}, layout ${layoutId}`);
  for (const c of chosen)
    console.log(`  option ${c.optionId} ${c.option} (flags 0x${c.optionFlags.toString(16)}): ${c.choiceId ?? "-"} ${c.choice} ${c.skipped ? `[${c.skipped}]` : ""}${c.wowExportChoiceId !== c.choiceId ? ` (wow.export: ${c.wowExportChoiceId})` : ""}`);
  console.log(`  geosets: ${geosets.join(",")}`);
  for (const l of layers) console.log(`  layer type ${l.textureType} #${l.layer} target ${l.target} blend ${l.blendMode} section ${l.section.sectionType} -> ${l.fileDataId}`);
  for (const n of notes) console.log(`  note: ${n}`);
  if (unsupported.length) console.log(`  unsupported elements: ${JSON.stringify(unsupported)}`);
}

