// Types for the local server's API (/api/v1) and its converted assets (/assets/{build}/...).
//
// Base looks and resolved items keep the shape of the tools/looks and tools/resolve output.

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
  active: {
    path: string
    product: string
    build: string
    /** Converter version; asset URLs carry it as ?v= (see client.ts). */
    assetVersion?: number
  } | null
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
  /** From ChrRaces.Alliance. */
  faction?: 'alliance' | 'horde' | 'neutral'
}
export interface CharactersResponse {
  build: string
  races: CharacterRace[]
}

// GET /api/v1/characters/{race}/{sex}?models=hd|sd
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
  /** On a choice's layers in `options`: the layer applies only while this other choice is active. */
  relatedChoiceId?: number
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
export interface AnimationInfo {
  id: number
  name: string
}
export interface CustomizationChoice {
  choiceId: number
  name: string
  swatch?: string
  /** Geosets this choice shows, after the option's own geosets are turned off. */
  geosets?: number[]
  /** Texture layers this choice paints. */
  layers?: TextureLayer[]
}
/** Every choice a fresh character can pick for one option. */
export interface CustomizationOption {
  optionId: number
  name: string
  /** ChrCustomizationOption.Flags. */
  flags?: number
  defaultChoiceId: number | null
  /** Every geoset any choice of this option names. */
  geosets?: number[]
  choices: CustomizationChoice[]
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
  /** Every geoset in the body mesh. */
  meshGeosets?: number[]
  /** Animations the body model has, by animation ID. */
  animations?: AnimationInfo[]
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
  /** ItemSparse.InventoryType (13 one-hand, 14 shield, 17 two-hand, ...). */
  inventoryType?: number
  /** ItemSparse.OverallQualityID: 0 poor ... 5 legendary, 6 artifact, 7 heirloom. */
  quality: number
  iconFileDataId: number
  /** A developer or NPC item, going by its name. Listed after the others. */
  internal?: boolean
  /** The build has a model but no name, quality or level for it; `name` is made up from its set and slot. */
  unnamed?: boolean
}
export interface SetPiece {
  slot: string
  itemId: number
  name: string
  quality: number
  iconFileDataId: number
  unnamed?: boolean
}
/** One heading of notable sets (/sets/notable): "PvP (rare)", "PvP (epic)", "Tier 0" ... "Tier 3". */
export interface SetGroup { group: string; sets: ItemSetResult[] }

export interface ItemSetResult {
  setId: number
  name: string
  internal: boolean
  /** Every piece is unnamed in this build (see ItemSearchResult.unnamed). */
  unnamed?: boolean
  /** Best piece quality, or the tier's for an unnamed tier set; null when unknown. */
  quality?: number | null
  /** Pieces that can be shown, each with the look slot it goes in. */
  pieces: SetPiece[]
  /** Pieces left out: not worn, no visual, or their slot is taken. */
  skipped: { itemId: number; reason: string }[]
}
export interface ItemSearchQuery {
  q?: string
  slot?: string
  quality?: number
  limit?: number
  offset?: number
  /** 1 also lists items with no name; without it an exact ID still finds them. */
  unnamed?: 1
}

// GET /api/v1/items/{itemId}/resolved
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
  /** The same groups, each with the option flags that keep it (HelmHide in ItemResolver). */
  helmHides?: { group: number; keepWithOptionFlags: number }[]
  models: ItemModelSlot[]
  bodyTextures: ItemBodyTexture[]
  /** Effect models (weapon glows) for the item's own model, at its attachment points 0-4 (ItemVisuals). */
  effects?: ItemEffect[]
  /** Item.SheatheType: where the weapon goes when sheathed (dress.ts SHEATH). */
  sheatheType?: number
  /** Item.SubclassID: bow, gun, ... */
  itemSubclass?: number
  error?: string
}
export interface ItemEffect {
  attachmentId: number
  modelFileDataId: number
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
export interface ModelBone {
  index: number
  /** glTF node name, bone_<index>. */
  node: string
  parent: number
  /** M2 bone flags (https://wowdev.wiki/M2#Bones). */
  flags: number
}
export interface ModelMeta {
  fileDataId: number
  textures: { index: number; type: number; flags: number; fileDataId: number | null }[]
  geosets: ModelGeoset[]
  animations?: ModelAnimation[]
  bones?: ModelBone[]
  /** Converter version 2 and later. */
  particles?: ParticleEmitterMeta[]
}

/** An M2Track sampled for Stand or a global sequence: keys are [timeMs, ...value]. */
export interface ParticleTrack { durationMs: number; interpolation: number; globalSequence?: number | null; keys: number[][] }
/** A value over a particle's life: times are fractions of the life (0 to 1). */
export interface ParticleCurve { times: number[]; values: (number | number[])[] }

/** One M2 particle emitter, in glTF axes except the generator angles (see viewer/particles.ts). */
export interface ParticleEmitterMeta {
  index: number
  flags: number
  bone: number
  boneNode: string | null
  offsetGltf: number[]
  textureIndex: number
  textureFileDataId: number | null
  blendMode: number
  emitterType: number
  rows: number
  columns: number
  tracks: Partial<Record<'emissionSpeed' | 'speedVariation' | 'verticalRange' | 'horizontalRange' | 'gravity' | 'lifespan'
    | 'emissionRate' | 'areaX' | 'areaY' | 'zSource', ParticleTrack>>
  lifespanVariation: number
  emissionRateVariation: number
  color?: ParticleCurve | null
  alpha?: ParticleCurve | null
  scale?: ParticleCurve | null
  scaleVariation: number[]
  headCell?: ParticleCurve | null
  drag: number
}

// Prompt box (/api/v1/assistant). The key itself never comes back, only a masked form.
export type AssistantProvider = 'anthropic' | 'openai-compatible' | 'claude-code'
export interface AssistantSettingsView {
  provider: AssistantProvider | null
  baseUrl: string | null
  model: string | null
  hasKey: boolean
  maskedKey: string | null
  keyFromEnvironment: boolean
}
export interface AssistantSettingsInput { provider: AssistantProvider; baseUrl?: string; model: string; apiKey?: string }
export interface AssistantTestResult { ok: boolean; model: string | null; reply?: string; message?: string }
export interface AssistantTurn { role: 'user' | 'assistant'; text: string }
export type AssistantEvent =
  | { type: 'step'; tool: string; arguments: Record<string, unknown> | null }
  | { type: 'reply'; text: string }
  /** plan: the tokens count against the player's Claude plan, not a key. */
  | { type: 'usage'; inputTokens: number; outputTokens: number; plan?: boolean }
  | { type: 'done' }
  | { type: 'error'; message: string }

// GET /titles: CharTitles, "%s" where the name goes; female is the female wording (often the same).
export interface TitleInfo {
  titleId: number
  male: string
  female: string
  /** The faction that can hold it (PvP ranks); absent when either can. */
  faction?: 'alliance' | 'horde' | null
}
// GET /names: NameGen per race ID, the game's random-name lists. Last names are shared by both sexes.
export interface RaceNames {
  male: string[]
  female: string[]
  last: string[]
}
