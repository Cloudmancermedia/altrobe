// Types for the local server's API (/api/v1) and its converted assets (/assets/{build}/...).
//
// Base looks and resolved items mirror the Phase 1 spike's output/looks/*.json and
// output/resolved/<character>/<itemId>.json. Fields marked "proposed" are not in the spike's files;
// the dev adapter fills them, and the real server needs to agree on them.

export type ModelSet = 'hd' | 'sd'
export type Sex = 0 | 1

export interface ApiErrorBody {
  error: { code: string; message: string }
}

// GET /api/v1/status
export interface InstallProduct {
  product: string
  build: string
  buildName?: string
  /** The server's guess, from the build name, that this product is Forever. */
  isForever?: boolean
}
export interface Install {
  path: string
  products: InstallProduct[]
}
export interface Status {
  version: string
  installs: Install[]
  /** null until an install is selected; data endpoints answer 409 "no_install" until then. */
  active: { path: string; product: string; build: string } | null
  cacheFolder: string
}

// POST /api/v1/install
export interface InstallRequest {
  path?: string
  product: string
}

// GET /api/v1/characters
export interface CharacterClass {
  classId: number
  name: string
}
export interface CharacterSex {
  sex: Sex
  hd: boolean
  sd: boolean
}
export interface CharacterRace {
  race: number
  name: string
  classes: CharacterClass[]
  sexes: CharacterSex[]
}
export interface CharactersResponse {
  build: string
  races: CharacterRace[]
}

// GET /api/v1/characters/{race}/{sex}?models=hd|sd  (spike: output/looks/<name>.json)
export interface Rect {
  x: number
  y: number
  width: number
  height: number
}
export interface Section extends Rect {
  sectionType: number
}
export interface TextureLayer {
  textureType: number
  layer: number
  blendMode: number
  target?: number
  section: Section
  fileDataId: number
  choiceId?: number
  optionId?: number
  materialResourcesId?: number
  // Set on item layers added by dress().
  itemId?: number
  slot?: number
}
export interface SectionLayer {
  sectionType: number
  textureType: number
  blendMode: number
  fromLayer?: number
}
export interface LookTexture {
  textureType: number
  width: number
  height: number
}
/** The default choice for one customization option. */
export interface LookChoice {
  optionId: number
  option: string
  optionFlags?: number
  choiceId: number | null
  choice: string
  orderIndex?: number
  eligibleChoices?: number
  totalChoices?: number
}
/** Proposed: every selectable choice of an option. The spike's looks only carry the default. */
export interface CustomizationOption {
  optionId: number
  name: string
  defaultChoiceId: number | null
  choices: { choiceId: number; name: string; swatch?: string }[]
}
export interface BaseLook {
  name?: string
  character?: string
  build: string
  models: ModelSet
  race: number
  sex: number
  classId?: number
  chrModelId: number
  modelFileDataId: number
  textureLayoutId: number
  layout?: { ID: number; Width: number; Height: number }
  choices: LookChoice[]
  geosets: number[]
  geosetsFromChoices?: { optionId: number; choiceId: number; geosets: number[] }[]
  textures: LookTexture[]
  layers: TextureLayer[]
  sections: Section[]
  sectionLayers: SectionLayer[]
  notes?: string[]
  options?: CustomizationOption[]
}

// GET /api/v1/items/search
export interface ItemSearchResult {
  itemId: number
  name: string
  /** Look slot name (look.ts SLOT_NAMES), from the item's inventory type. */
  slot: string
  /** ItemSparse.OverallQualityID: 0 poor ... 5 legendary, 6 artifact, 7 heirloom. */
  quality: number
  iconFileDataId: number
}
export interface ItemSearchQuery {
  q?: string
  slot?: string
  quality?: number
  limit?: number
  offset?: number
}

// GET /api/v1/items/{itemId}/resolved  (spike: output/resolved/<character>/<itemId>.json)
export interface ItemFile {
  fileDataId: number
  match?: string
  /** Shoulders: 0 left, 1 right. -1 or absent otherwise. */
  position?: number
}
export interface ItemModelSlot {
  slot: number
  modelResourcesId?: number
  models: ItemFile[]
  textures: ItemFile[]
}
export interface ItemBodyTexture {
  section: number
  textures: ItemFile[]
}
export interface ResolvedItem {
  itemId: number
  name: string
  inventoryType: number
  itemAppearanceId?: number
  itemDisplayInfoId?: number
  geosetGroup: number[]
  attachmentGeosetGroup?: number[]
  helmHideGeosetGroups: number[]
  models: ItemModelSlot[]
  bodyTextures: ItemBodyTexture[]
  error?: string
}

// GET /assets/{build}/models/{fdid}.json  (converter metadata next to the .glb)
export interface ModelTextureRef {
  textureIndex: number
  type: number
  fileDataId: number | null
}
export interface ModelTextureUnit {
  blendMode?: number
  materialFlags?: number
  textures: ModelTextureRef[]
}
export interface ModelGeoset {
  submeshIndex: number
  geosetId: number
  textureUnits?: ModelTextureUnit[]
}
export interface ModelAnimation {
  name?: string
  id?: number
  variation?: number
  globalSequence?: number
  durationMs: number
}
export interface ModelMeta {
  fileDataId: number
  textures: { index: number; type: number; flags: number; fileDataId: number | null }[]
  geosets: ModelGeoset[]
  animations?: ModelAnimation[]
}
