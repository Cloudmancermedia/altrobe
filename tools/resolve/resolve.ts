// Spike Step 2: resolve an item + race + sex to the models, textures and geosets it needs.
// Reads the JSON tables the probe exported into output/tables/. Run with Node 24+:
//   node tools/resolve/resolve.ts
// Writes output/resolved/<race>-<sex>/<itemId>.json and prints a summary.

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

const itemSparse = byId(table("ItemSparse"));
const imaByItem = groupBy(table("ItemModifiedAppearance"), "ItemID");
const itemAppearance = byId(table("ItemAppearance"));
const itemDisplayInfo = byId(table("ItemDisplayInfo"));
const matResByDisplay = groupBy(table("ItemDisplayInfoMaterialRes"), "ItemDisplayInfoID");
const modelFilesByRes = groupBy(table("ModelFileData"), "ModelResourcesID");
const textureFilesByRes = groupBy(table("TextureFileData"), "MaterialResourcesID");
const componentModel = byId(table("ComponentModelFileData"));
const componentTexture = byId(table("ComponentTextureFileData"));
const helmetGeosetsByVis = groupBy(table("HelmetGeosetData"), "HelmetGeosetVisDataID");
const chrRaces = byId(table("ChrRaces"));
const chrModel = byId(table("ChrModel"));
const raceXModel = table("ChrRaceXChrModel");
const creatureDisplay = byId(table("CreatureDisplayInfo"));
const creatureModel = byId(table("CreatureModelData"));

// Component files carry race/sex/class filters. 0 for race or class means "any".
// GenderIndex 0 = male, 1 = female; other values (seen: 2, 3) are treated as "any". Inferred, not verified.
type Character = { raceId: number; sex: 0 | 1 };
type Pick = { fileDataId: number; match: string; position?: number };

function fallbackRace(c: Character, kind: "Model" | "Texture"): number {
  const r = chrRaces.get(c.raceId)!;
  return c.sex === 0 ? r[`Male${kind}FallbackRaceID`] : r[`Female${kind}FallbackRaceID`];
}

function pickComponentFiles(fileIds: number[], meta: Map<number, Row>, c: Character, kind: "Model" | "Texture"): Pick[] {
  const withMeta = fileIds.map((id) => ({ id, m: meta.get(id) }));
  // Files with no component row are race-neutral.
  const neutral = withMeta.filter((f) => !f.m);
  if (neutral.length === withMeta.length) return neutral.map((f) => ({ fileDataId: f.id, match: "neutral" }));

  const sexOk = (m: Row) => m.GenderIndex === c.sex || (m.GenderIndex !== 0 && m.GenderIndex !== 1);
  const tiers: [string, (m: Row) => boolean][] = [
    ["race+sex", (m) => m.RaceID === c.raceId && sexOk(m)],
    ["fallback-race", (m) => m.RaceID === fallbackRace(c, kind) && fallbackRace(c, kind) !== 0 && sexOk(m)],
    ["any-race", (m) => m.RaceID === 0 && sexOk(m)],
  ];
  for (const [label, test] of tiers) {
    const hits = withMeta.filter((f) => f.m && test(f.m));
    if (hits.length) return hits.map((f) => ({ fileDataId: f.id, match: label, position: f.m!.PositionIndex }));
  }
  return neutral.map((f) => ({ fileDataId: f.id, match: "neutral" }));
}

function baseCharacter(c: Character) {
  const link = raceXModel.find((x) => x.ChrRacesID === c.raceId && x.Sex === c.sex);
  if (!link) return { error: "no ChrRaceXChrModel row" };
  const model = chrModel.get(link.ChrModelID)!;
  const display = creatureDisplay.get(model.DisplayID);
  const modelData = display ? creatureModel.get(display.ModelID) : undefined;
  return {
    chrModelId: model.ID,
    displayId: model.DisplayID,
    modelFileDataId: modelData?.FileDataID ?? null,
    skeletonFileDataId: model.SkeletonFileDataID,
    textureLayoutId: model.CharComponentTextureLayoutID,
  };
}

function resolveItem(itemId: number, c: Character) {
  const name = itemSparse.get(itemId)?.Display_lang ?? "(no ItemSparse row)";
  const ima = (imaByItem.get(itemId) ?? []).sort((a, b) => a.ItemAppearanceModifierID - b.ItemAppearanceModifierID || a.OrderIndex - b.OrderIndex)[0];
  if (!ima) return { itemId, name, error: "no ItemModifiedAppearance" };
  const appearance = itemAppearance.get(ima.ItemAppearanceID);
  if (!appearance) return { itemId, name, error: `no ItemAppearance ${ima.ItemAppearanceID}` };
  const display = itemDisplayInfo.get(appearance.ItemDisplayInfoID);
  if (!display) return { itemId, name, error: `no ItemDisplayInfo ${appearance.ItemDisplayInfoID}` };

  // A slot can have a texture with no model: cloaks paint onto the body's own cape geoset.
  const models = [0, 1].flatMap((i) => {
    const resId = display.ModelResourcesID[i];
    const matId = display.ModelMaterialResourcesID[i];
    if (!resId && !matId) return [];
    const files = resId ? (modelFilesByRes.get(resId) ?? []).map((r) => r.FileDataID) : [];
    const textures = matId ? pickComponentFiles((textureFilesByRes.get(matId) ?? []).map((r) => r.FileDataID), componentTexture, c, "Texture") : [];
    return [{ slot: i, modelResourcesId: resId, models: resId ? pickComponentFiles(files, componentModel, c, "Model") : [], textures }];
  });

  const bodyTextures = (matResByDisplay.get(display.ID) ?? []).map((m) => ({
    section: m.ComponentSection,
    textures: pickComponentFiles((textureFilesByRes.get(m.MaterialResourcesID) ?? []).map((r) => r.FileDataID), componentTexture, c, "Texture"),
  }));

  // HelmetGeosetVis is indexed by sex (0 male, 1 female), as in wow.export DBItemGeosets.get_helmet_hide_geosets.
  const helmVis = display.HelmetGeosetVis?.[c.sex];
  const helmHides = helmVis
    ? (helmetGeosetsByVis.get(helmVis) ?? []).filter((h) => h.RaceID === c.raceId || h.RaceID === 0).map((h) => h.HideGeosetGroup)
    : [];

  return {
    itemId,
    name,
    inventoryType: itemSparse.get(itemId)?.InventoryType,
    itemAppearanceId: ima.ItemAppearanceID,
    itemDisplayInfoId: display.ID,
    geosetGroup: display.GeosetGroup,
    attachmentGeosetGroup: display.AttachmentGeosetGroup,
    helmHideGeosetGroups: [...new Set(helmHides)],
    models,
    bodyTextures,
  };
}

const characters: Record<string, Character> = { "orc-male": { raceId: 2, sex: 0 }, "undead-female": { raceId: 5, sex: 1 } };
const testItems = [14152, 220794, 231534, 12640, 15138, 220806, 16734, 19019, 226906];

const outDir = join(root, "output/resolved");
for (const [label, c] of Object.entries(characters)) {
  const dir = join(outDir, label);
  mkdirSync(dir, { recursive: true });
  const base = baseCharacter(c);
  writeFileSync(join(dir, "base.json"), JSON.stringify(base, null, 2));
  console.log(`\n== ${label}: body model ${"modelFileDataId" in base ? base.modelFileDataId : base.error}, skeleton ${"skeletonFileDataId" in base ? base.skeletonFileDataId : "-"}, texture layout ${"textureLayoutId" in base ? base.textureLayoutId : "-"}`);
  for (const id of testItems) {
    const r = resolveItem(id, c);
    writeFileSync(join(dir, `${id}.json`), JSON.stringify(r, null, 2));
    if ("error" in r) {
      console.log(`  ${id} ${r.name}: ERROR ${r.error}`);
      continue;
    }
    const modelSummary = r.models.map((m) => `${m.models.map((p) => p.fileDataId + (p.position !== undefined && p.position >= 0 ? `@${p.position}` : "")).join("/") || "(no model)"} [${m.models[0]?.match ?? "texture-only"}] tex ${m.textures.map((t) => t.fileDataId).join("/") || "-"}`).join("; ");
    const bodySummary = r.bodyTextures.map((b) => `s${b.section}:${b.textures.map((t) => t.fileDataId).join("/") || "NONE"}[${b.textures[0]?.match ?? "-"}]`).join(" ");
    console.log(`  ${id} ${r.name}`);
    if (modelSummary) console.log(`      models: ${modelSummary}`);
    if (bodySummary) console.log(`      body:   ${bodySummary}`);
    console.log(`      geosets: ${JSON.stringify(r.geosetGroup)} helm hides: ${JSON.stringify(r.helmHideGeosetGroups)}`);
  }
}
