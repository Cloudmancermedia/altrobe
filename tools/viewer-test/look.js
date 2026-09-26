// Saved looks and share links. No dependencies; runs in the browser and in Node 24.
//
// A look is a small JSON object that names a character and what it wears by game IDs only:
//   { v: 1, game: "forever", build: "1.60.1.70009", models: "hd" | "sd", race: 2, sex: 0,
//     custom: { "<optionId>": choiceId },   only choices that differ from the data-driven defaults
//     items: { "<slot>": itemId },          slot names from dress.js SLOT_NAMES
//     hide: ["head", "back"],               optional hidden slots
//     cam: { view: "front" | "side" | "back" | "head" } }
// A share link carries it in the URL fragment, which browsers never send to a server:
//   index.html#look=<base64url(canonical JSON)>
//
// Loading is forgiving: unknown fields are ignored, bad entries are dropped, and every change is
// reported as a notice so the viewer can show it. Only a look it cannot render at all (no race,
// sex or game) fails.

import { SLOT_NAMES, INVENTORY_TYPE_TO_SLOT, SHOULDER_SLOT_L, SHOULDER_SLOT_R } from './dress.js';

export const LOOK_VERSION = 1;
export const GAME = 'forever';
export const VIEWS = ['front', 'side', 'back', 'head'];
export const MODEL_SETS = ['hd', 'sd'];

// Characters this spike has data for (output/looks/<name>.json). race is a ChrRaces ID; sex 0 male, 1 female.
export const CHARACTERS = [
  { name: 'orc-male', race: 2, sex: 0 },
  { name: 'undead-female', race: 5, sex: 1 },
];

const SLOT_BY_ID = Object.fromEntries(Object.entries(SLOT_NAMES).map(([n, id]) => [id, n]));
const isId = (x) => Number.isInteger(x) && x > 0;
const isIdKey = (k) => /^[1-9]\d*$/.test(k);

/** The character name (orc-male) for a look's race and sex, or null when the spike has no data for it. */
export function characterFor(look) {
  return CHARACTERS.find((c) => c.race === look.race && c.sex === look.sex)?.name ?? null;
}

/**
 * Reads any parsed JSON value as a v1 look. Unknown fields are dropped; malformed entries are dropped
 * with a notice. Returns { look: null } only when the look cannot be rendered at all.
 * @returns {{look: object|null, notices: string[]}}
 */
export function normalizeLook(input) {
  const notices = [];
  if (!input || typeof input !== 'object' || Array.isArray(input)) return { look: null, notices: ['not a look: expected a JSON object'] };

  // Version. There is no older format, so a missing "v" is read as v1 and said so; a newer one is read
  // as v1 too (its extra fields are ignored), which is the forward-compatibility promise.
  if (input.v === undefined) notices.push(`look has no schema version; read as v${LOOK_VERSION}`);
  else if (!Number.isInteger(input.v) || input.v < 1) return { look: null, notices: [`unsupported look version ${JSON.stringify(input.v)}`] };
  else if (input.v > LOOK_VERSION) notices.push(`look is v${input.v}, this viewer reads v${LOOK_VERSION}; fields it does not know were ignored`);

  const game = input.game ?? GAME;
  if (game !== GAME) return { look: null, notices: [`look is for game "${game}", this viewer shows "${GAME}"`] };
  if (!Number.isInteger(input.race) || !(input.sex === 0 || input.sex === 1)) return { look: null, notices: ['look needs an integer race and a sex of 0 or 1'] };

  const look = { v: LOOK_VERSION, game, build: typeof input.build === 'string' ? input.build : '', models: 'hd', race: input.race, sex: input.sex, custom: {}, items: {}, hide: [], cam: { view: 'front' } };
  if (typeof input.build !== 'string') notices.push('look has no game build');

  if (MODEL_SETS.includes(input.models)) look.models = input.models;
  else if (input.models !== undefined) notices.push(`unknown models "${input.models}"; using hd`);

  for (const [k, v] of Object.entries(isPlainObject(input.custom) ? input.custom : {})) {
    if (isIdKey(k) && isId(v)) look.custom[k] = v;
    else notices.push(`dropped customization ${k}: ${JSON.stringify(v)} (not an option ID and choice ID)`);
  }
  for (const [slot, id] of Object.entries(isPlainObject(input.items) ? input.items : {})) {
    if (!(slot in SLOT_NAMES)) notices.push(`dropped item in unknown slot "${slot}"`);
    else if (!isId(id)) notices.push(`dropped ${slot}: ${JSON.stringify(id)} is not an item ID`);
    else look.items[slot] = id;
  }
  for (const slot of Array.isArray(input.hide) ? input.hide : []) {
    if (!(slot in SLOT_NAMES)) notices.push(`dropped hidden slot "${slot}"`);
    else if (!look.hide.includes(slot)) look.hide.push(slot);
  }
  const view = input.cam?.view;
  if (VIEWS.includes(view)) look.cam.view = view;
  else if (view !== undefined) notices.push(`unknown camera view "${view}"; using front`);

  if (!characterFor(look)) notices.push(`no character data for race ${look.race} sex ${look.sex}`);
  return { look, notices };
}

/**
 * Canonical form: sorted keys at every level, empty or default optional fields left out, and
 * customizations equal to the defaults left out. The same look always gives the same JSON.
 * @param {object} look  a normalized look
 * @param {{defaults?: Object<string,number>}} [opts]  default choice per option ID, from output/looks/<name>.json
 */
export function canonicalLook(look, { defaults = {} } = {}) {
  const out = { build: look.build, game: look.game, models: look.models, race: look.race, sex: look.sex, v: look.v };
  const custom = sortKeys(Object.fromEntries(Object.entries(look.custom ?? {}).filter(([k, v]) => defaults[k] !== v)));
  const items = sortKeys(look.items ?? {});
  const hide = [...new Set(look.hide ?? [])].sort();
  if (look.cam?.view && look.cam.view !== 'front') out.cam = { view: look.cam.view };
  if (Object.keys(custom).length) out.custom = custom;
  if (hide.length) out.hide = hide;
  if (Object.keys(items).length) out.items = items;
  return sortKeys(out);
}

export function canonicalJSON(look, opts) {
  return JSON.stringify(canonicalLook(look, opts));
}

/** base64url(canonical JSON), for the #look= fragment. */
export function encodeLook(look, opts) {
  const bytes = new TextEncoder().encode(canonicalJSON(look, opts));
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Inverse of encodeLook, then normalizeLook. Never throws. Accepts a percent-encoded value. */
export function decodeLook(text) {
  let parsed;
  try {
    const b64 = decodeURIComponent(text).replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64 + '='.repeat((4 - (b64.length % 4)) % 4));
    parsed = JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0))));
  } catch (e) {
    return { look: null, notices: [`could not read the look link: ${e.message}`] };
  }
  return normalizeLook(parsed);
}

/** The look in a URL fragment ("#look=..."), or null when there is none. */
export function lookFromFragment(hash) {
  const m = /(?:^#|&)look=([^&]*)/.exec(hash ?? '');
  return m ? decodeLook(m[1]) : null;
}

/** Viewer URL (no query, no fragment) + #look=... */
export function shareUrl(viewerUrl, look, opts) {
  return `${viewerUrl.split(/[?#]/)[0]}#look=${encodeLook(look, opts)}`;
}

/** Default choice per option ID from a base look file (output/looks/<name>.json). */
export function defaultsFrom(baseLook) {
  return Object.fromEntries((baseLook?.choices ?? []).filter((c) => isId(c.choiceId)).map((c) => [String(c.optionId), c.choiceId]));
}

/**
 * Builds a look from what the query-param viewer consumes: ?look=<character>&models=&items=&view=.
 * Items go to the slot their inventory type implies (dress.js lookStateFor rules), later items win.
 * @param {{character:string, models?:string, build:string, resolvedItems:object[], view?:string}} p
 */
export function lookFromViewer({ character, models = 'hd', build, resolvedItems = [], view = 'front' }) {
  const ch = CHARACTERS.find((c) => c.name === character);
  if (!ch) throw new Error(`unknown character ${character}`);
  const items = {};
  for (const r of resolvedItems) {
    const name = SLOT_BY_ID[INVENTORY_TYPE_TO_SLOT[r?.inventoryType]];
    if (name && !r.error) items[name] = r.itemId;
  }
  return normalizeLook({ v: LOOK_VERSION, game: GAME, build, models, race: ch.race, sex: ch.sex, items, cam: { view } }).look;
}

/** Slots an item of this inventory type may go in. One-handers can also be held in the off hand. */
function slotsForInventoryType(t) {
  const s = INVENTORY_TYPE_TO_SLOT[t];
  if (s === undefined) return [];
  return t === 13 ? [s, SLOT_NAMES.offhand] : [s];
}

/**
 * Checks a normalized look against the loaded game data and drops what cannot be shown.
 * @param {object} look
 * @param {{build?:string, defaults?:Object<string,number>, resolvedById?:Map<number,object>}} data
 *   build: the build of the loaded data; defaults: from defaultsFrom(); resolvedById: resolved item JSON
 *   (missing entries mean the item has no resolved data for this character).
 * @returns {{look: object, notices: string[]}}  a new look plus notices
 */
export function checkAgainstData(look, { build, defaults, resolvedById } = {}) {
  const notices = [];
  const out = { ...look, custom: { ...look.custom }, items: { ...look.items }, hide: [...look.hide], cam: { ...look.cam } };
  if (build && look.build !== build) notices.push(`look was made on build ${look.build || '(unknown)'}; showing it with data from build ${build}`);
  if (defaults) for (const [opt, choice] of Object.entries(out.custom)) {
    if (!(opt in defaults)) { notices.push(`dropped customization option ${opt}: not an option for this character (${look.models})`); delete out.custom[opt]; }
    else if (defaults[opt] === choice) delete out.custom[opt];
    else notices.push(`customization ${opt}=${choice} kept but not drawn: this viewer only draws the default choices`);
  }
  if (resolvedById) for (const [slot, id] of Object.entries(out.items)) {
    const r = resolvedById.get(id);
    if (!r || r.error) { notices.push(`dropped ${slot} item ${id}: no item data${r?.error ? ` (${r.error})` : ''}`); delete out.items[slot]; }
    else if (!slotsForInventoryType(r.inventoryType).includes(SLOT_NAMES[slot])) {
      notices.push(`dropped ${slot} item ${id}: inventory type ${r.inventoryType} does not go in that slot`);
      delete out.items[slot];
    }
  }
  return { look: out, notices };
}

/** The dress.js look state for a look: { character, items: { slotId: itemId } }, hidden slots left out. */
export function dressStateFor(look) {
  const items = {};
  for (const [slot, id] of Object.entries(look.items)) {
    if (look.hide.includes(slot)) continue;
    items[SLOT_NAMES[slot]] = id;
    if (SLOT_NAMES[slot] === SHOULDER_SLOT_L) items[SHOULDER_SLOT_R] = id;
  }
  return { character: characterFor(look), items };
}

function isPlainObject(x) {
  return !!x && typeof x === 'object' && !Array.isArray(x);
}
function sortKeys(o) {
  return Object.fromEntries(Object.keys(o).sort().map((k) => [k, o[k]]));
}
