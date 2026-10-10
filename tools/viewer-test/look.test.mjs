// Tests for look.js. Run with: node --test tools/viewer-test/look.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  normalizeLook, canonicalJSON, encodeLook, decodeLook, lookFromFragment, shareUrl, lookFromViewer,
  checkAgainstData, dressStateFor, defaultsFrom, characterFor,
} from './look.js';

const outfitA = {
  v: 1, game: 'forever', build: '1.60.1.70009', models: 'hd', race: 2, sex: 0,
  items: { head: 12640, shoulder: 231534, chest: 220794, hands: 220806, feet: 16734, mainhand: 19019, back: 15138 },
};

test('round-trip: decode(encode(look)) gives the same look', () => {
  const { look } = normalizeLook({ ...outfitA, hide: ['back'], cam: { view: 'side' }, custom: { 20: 385 } });
  const back = decodeLook(encodeLook(look));
  assert.deepEqual(back.notices, []);
  assert.deepEqual(back.look, look);
  assert.equal(characterFor(back.look), 'orc-male');
});

test('encoding is URL-safe and survives a share URL', () => {
  const { look } = normalizeLook(outfitA);
  const url = shareUrl('http://localhost:8766/tools/viewer-test/index.html?look=orc-male&items=1#x', look);
  assert.match(url, /^http:\/\/localhost:8766\/tools\/viewer-test\/index\.html#look=[A-Za-z0-9_-]+$/);
  assert.deepEqual(lookFromFragment(new URL(url).hash).look, look);
  assert.equal(lookFromFragment('#other=1'), null);
  // A mangled fragment gives a notice, not a throw.
  const bad = lookFromFragment('#look=%%%not-base64');
  assert.equal(bad.look, null);
  assert.match(bad.notices[0], /could not read the look link/);
});

test('canonical form: key order, duplicates and defaults do not change the output', () => {
  const a = normalizeLook(outfitA).look;
  const shuffled = normalizeLook({
    cam: { view: 'front' }, hide: [], custom: {}, sex: 0, race: 2, models: 'hd', build: '1.60.1.70009', game: 'forever', v: 1,
    items: Object.fromEntries(Object.entries(outfitA.items).reverse()),
  }).look;
  assert.equal(canonicalJSON(a), canonicalJSON(shuffled));
  assert.equal(encodeLook(a), encodeLook(shuffled));
  // Stable across calls, and default-valued optional fields are omitted.
  assert.equal(canonicalJSON(a), canonicalJSON(normalizeLook(JSON.parse(canonicalJSON(a))).look));
  assert.deepEqual(Object.keys(JSON.parse(canonicalJSON(a))), ['build', 'game', 'items', 'models', 'race', 'sex', 'v']);
  // hide is sorted and deduplicated.
  const h = normalizeLook({ ...outfitA, hide: ['head', 'back', 'head'] }).look;
  assert.deepEqual(JSON.parse(canonicalJSON(h)).hide, ['back', 'head']);
});

test('canonical form omits customizations equal to the defaults', () => {
  const defaults = defaultsFrom({ choices: [{ optionId: 19, choiceId: 353 }, { optionId: 20, choiceId: 384 }, { optionId: 21, choiceId: null }] });
  assert.deepEqual(defaults, { 19: 353, 20: 384 });
  const withDefault = normalizeLook({ ...outfitA, custom: { 19: 353, 20: 390 } }).look;
  assert.deepEqual(JSON.parse(canonicalJSON(withDefault, { defaults })).custom, { 20: 390 });
  const allDefault = normalizeLook({ ...outfitA, custom: { 19: 353 } }).look;
  assert.equal(JSON.parse(canonicalJSON(allDefault, { defaults })).custom, undefined);
});

test('unknown fields are ignored and not carried into the canonical form', () => {
  const { look, notices } = normalizeLook({ ...outfitA, pet: 'wolf', items: { ...outfitA.items }, cam: { view: 'head', fov: 40 } });
  assert.deepEqual(notices, []);
  const json = canonicalJSON(look);
  assert.doesNotMatch(json, /pet|fov/);
  assert.deepEqual(JSON.parse(json).cam, { view: 'head' });
});

test('version handling: missing v, newer v, and invalid v', () => {
  const { v, ...noV } = outfitA;
  const missing = normalizeLook(noV);
  assert.equal(missing.look.v, 1);
  assert.match(missing.notices.join(), /no schema version; read as v1/);

  const newer = normalizeLook({ ...outfitA, v: 2, lighting: 'dusk' });
  assert.equal(newer.look.v, 1);
  assert.match(newer.notices.join(), /v2, this viewer reads v1/);
  assert.equal(newer.look.items.head, 12640);

  for (const bad of [0, -1, 1.5, '1']) {
    const r = normalizeLook({ ...outfitA, v: bad });
    assert.equal(r.look, null, `v=${JSON.stringify(bad)}`);
    assert.match(r.notices[0], /unsupported look version/);
  }
});

test('unrenderable looks fail with a notice, never a throw', () => {
  for (const input of [null, 42, [], { ...outfitA, game: 'retail' }, { ...outfitA, race: 'orc' }, { ...outfitA, sex: 2 }]) {
    const r = normalizeLook(input);
    assert.equal(r.look, null);
    assert.equal(r.notices.length, 1);
  }
  for (const text of ['', '!!!', 'bm90IGpzb24', encodeLook({ v: 1 }).slice(0, 5)]) {
    const r = decodeLook(text);
    assert.equal(r.look, null, text);
    assert.ok(r.notices.length);
  }
});

test('invalid item IDs, slots, choices and views are dropped with notices', () => {
  const { look, notices } = normalizeLook({
    ...outfitA,
    items: { head: 12640, chest: -5, hands: 'gloves', ring: 1234, feet: 1.5 },
    custom: { 20: 390, abc: 1, 21: 0 },
    hide: ['back', 'tail'],
    cam: { view: 'top' },
    models: 'ultra',
  });
  assert.deepEqual(look.items, { head: 12640 });
  assert.deepEqual(look.custom, { 20: 390 });
  assert.deepEqual(look.hide, ['back']);
  assert.equal(look.cam.view, 'front');
  assert.equal(look.models, 'hd');
  assert.equal(notices.length, 9, notices.join('\n'));
});

test('checkAgainstData drops items without data or in the wrong slot, and flags build and choices', () => {
  const { look } = normalizeLook({ ...outfitA, items: { head: 12640, chest: 999999, feet: 19019, offhand: 19019 }, custom: { 19: 353, 20: 390, 5: 1 } });
  const resolvedById = new Map([[12640, { itemId: 12640, inventoryType: 1 }], [19019, { itemId: 19019, inventoryType: 13 }]]);
  const r = checkAgainstData(look, { build: '1.60.2.1', defaults: { 19: 353, 20: 384 }, resolvedById });
  assert.deepEqual(r.look.items, { head: 12640, offhand: 19019 }); // one-hander may go in the off hand
  assert.deepEqual(r.look.custom, { 20: 390 });
  const text = r.notices.join('\n');
  assert.match(text, /build 1\.60\.1\.70009; showing it with data from build 1\.60\.2\.1/);
  assert.match(text, /dropped chest item 999999: no item data/);
  assert.match(text, /dropped feet item 19019: inventory type 13/);
  assert.match(text, /dropped customization option 5/);
  assert.match(text, /20=390 kept but not drawn/);
  assert.deepEqual(look.items.chest, 999999, 'input look is not mutated');
  assert.deepEqual(checkAgainstData(normalizeLook(outfitA).look, { build: '1.60.1.70009' }).notices, []);
});

test('viewer conversion: query params to a look and a look to a dress state', () => {
  const inv = { 12640: 1, 231534: 3, 220794: 5, 220806: 10, 16734: 8, 15138: 16, 19019: 13, 14152: 20 };
  const resolved = (ids) => ids.map((id) => ({ itemId: id, inventoryType: inv[id] }));
  const a = lookFromViewer({ character: 'orc-male', models: 'hd', build: 'b', resolvedItems: resolved([12640, 231534, 220794, 220806, 16734, 15138, 19019]) });
  assert.deepEqual(a.items, { head: 12640, shoulder: 231534, chest: 220794, hands: 220806, feet: 16734, back: 15138, mainhand: 19019 });
  assert.equal(a.race, 2);
  const robe = lookFromViewer({ character: 'undead-female', models: 'sd', build: 'b', resolvedItems: resolved([14152, 12640]), view: 'head' });
  assert.deepEqual([robe.race, robe.sex, robe.models, robe.cam.view], [5, 1, 'sd', 'head']);
  assert.deepEqual(robe.items, { chest: 14152, head: 12640 });

  const state = dressStateFor({ ...a, hide: ['back'] });
  assert.equal(state.character, 'orc-male');
  assert.deepEqual(state.items, { 1: 12640, 3: 231534, 30: 231534, 5: 220794, 10: 220806, 8: 16734, 16: 19019 });
});
