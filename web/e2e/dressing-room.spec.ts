import { enabled, skipReason } from './env'
import { call, expect, openApp, settled, test, watch, type Hooks } from './fixtures'

// File level, so a skipped run never starts a browser.
test.skip(!enabled, skipReason)

const THUNDERFURY = 19019
const BOGUS_ITEM = 999999999
const HEAD = 'attachment_11'

const hasItem = (h: Hooks, id: number) => h.attached(0).some((a) => a.itemId === id)
const bare = (h: Hooks) => h.attached(0).length === 0

test('every playable race loads, and Skyborne has no SD model', async ({ page }) => {
  await openApp(page)
  const races = await call(page, (h) => h.store.get().characters!.races)
  expect(races.map((r) => r.name)).toHaveLength(10)
  for (const r of races) {
    const sex = r.sexes.find((s) => s.hd)!.sex
    expect(await call(page, (h, a) => h.commands.set_character(a.race, a.sex, 'hd'), { race: r.race, sex })).not.toHaveProperty('error')
    await expect(page.locator('[data-cell="main"] .cell-label')).toHaveText(new RegExp(`^${r.name} `))
    await settled(page, 1)
    expect(await call(page, (h) => h.nodeWorld(0, 'attachment_11')), `${r.name} has a head attachment`).not.toBeNull()
    if (r.name.includes('Skyborne')) {
      await expect(page.getByRole('radiogroup', { name: 'Models' }).first().getByRole('radio', { name: 'SD' })).toBeDisabled()
      expect(await call(page, (h, race) => h.commands.set_character(race, 0, 'sd'), r.race)).toHaveProperty('error')
    }
  }
  expect(races.filter((r) => r.name.includes('Skyborne'))).toHaveLength(2)
})

test('a weapon does not move the body: Orc male head is the same bare and with Thunderfury', async ({ page }) => {
  await openApp(page)
  await call(page, (h) => h.commands.set_character(2, 0, 'hd'))
  await settled(page, 1, bare)
  await call(page, (h) => h.setTime(1.0))
  const without = await call(page, (h, n) => h.nodeWorld(0, n), HEAD)

  expect(await call(page, (h, id) => h.commands.equip_item('mainhand', id), THUNDERFURY)).not.toHaveProperty('error')
  await settled(page, 1, hasItem, THUNDERFURY)
  await call(page, (h) => h.setTime(1.0))
  const withWeapon = await call(page, (h, n) => h.nodeWorld(0, n), HEAD)

  expect(without).not.toBeNull()
  expect(withWeapon).toEqual(without)
})

test("Thunderfury's own nodes animate", async ({ page }) => {
  await openApp(page)
  await call(page, (h, id) => h.commands.equip_item('mainhand', id), THUNDERFURY)
  await settled(page, 1, hasItem, THUNDERFURY)
  const root = await call(page, (h, id) => h.attached(0).find((a) => a.itemId === id)!.rootName, THUNDERFURY)
  await call(page, (h) => h.setTime(0.5))
  const a = await call(page, (h, r) => h.itemNodesLocal(0, r), root)
  await call(page, (h) => h.setTime(1.5))
  const b = await call(page, (h, r) => h.itemNodesLocal(0, r), root)
  expect(a && Object.keys(a).length, 'the item has nodes').toBeGreaterThan(0)
  const moved = Object.keys(a!).filter((k) => JSON.stringify(a![k]) !== JSON.stringify(b![k]))
  expect(moved.length, 'nodes that moved between t=0.5 and t=1.5').toBeGreaterThan(0)
})

test('a share link opens the same look in a fresh browser', async ({ page, browser }) => {
  await openApp(page)
  await call(page, (h) => h.commands.set_character(5, 1, 'hd'))
  await call(page, (h, id) => h.commands.equip_item('mainhand', id), THUNDERFURY)
  await settled(page, 1, hasItem, THUNDERFURY)
  const link = await call(page, (h) => h.commands.share_link())
  const look = await call(page, (h) => h.commands.get_look())
  await call(page, (h) => h.setTime(1.0))
  const head = await call(page, (h, n) => h.nodeWorld(0, n), HEAD)

  const context = await browser.newContext()
  try {
    const fresh = await context.newPage()
    const problems = watch(fresh)
    await openApp(fresh, link)
    await settled(fresh, 1, hasItem, THUNDERFURY)
    expect(await call(fresh, (h) => h.commands.get_look())).toEqual(look)
    await expect(fresh.locator('[data-cell="main"] .cell-label')).toHaveText('Undead female HD')
    await call(fresh, (h) => h.setTime(1.0))
    expect(await call(fresh, (h, n) => h.nodeWorld(0, n), HEAD)).toEqual(head)
    expect(problems).toEqual([])
  } finally {
    await context.close()
  }
})

test('side by side shows a second character in the same outfit', async ({ page }) => {
  await openApp(page)
  await call(page, (h, id) => h.commands.equip_item('mainhand', id), THUNDERFURY)
  expect(await call(page, (h) => h.commands.compare([{ race: 6, sex: 0, models: 'hd' }]))).not.toHaveProperty('error')
  await settled(page, 2, (h, id) => h.attached(0).some((a) => a.itemId === id) && h.attached(1).some((a) => a.itemId === id), THUNDERFURY)
  await expect(page.locator('[data-cell="compare0"] .cell-label')).toHaveText('Tauren male HD')
  expect(await call(page, (h, n) => h.nodeWorld(1, n), HEAD)).not.toBeNull()
})

test('customization choices are drawn, and the Randomize and Reset buttons change them', async ({ page }) => {
  await openApp(page)
  await call(page, (h) => h.commands.set_character(2, 0, 'hd'))
  await settled(page, 1, (h) => h.store.get().options.length > 0)
  const before = await call(page, (h) => h.drawn(0))
  // A skin color that is not the default changes the composited skin; a hair style changes geosets.
  const pick = await call(page, (h) => {
    const byName = (n: string) => h.store.get().options.find((o) => o.name === n)!
    const other = (n: string) => { const o = byName(n); return { optionId: o.optionId, choiceId: o.choices.find((c) => c.choiceId !== o.defaultChoiceId)!.choiceId } }
    return { skin: other('Skin Color'), hair: other('Hair Style') }
  })
  expect(await call(page, (h, p) => h.commands.set_customization(p.optionId, p.choiceId), pick.skin)).not.toHaveProperty('error')
  expect(await call(page, (h, p) => h.commands.set_customization(p.optionId, p.choiceId), pick.hair)).not.toHaveProperty('error')
  await settled(page, 1, (h, files) => h.drawn(0).layerFiles.join() !== files, before.layerFiles.join())
  const after = await call(page, (h) => h.drawn(0))
  expect(after.geosets).not.toEqual(before.geosets)

  await page.getByRole('button', { name: 'Reset to defaults' }).click()
  await settled(page, 1, (h, files) => h.drawn(0).layerFiles.join() === files, before.layerFiles.join())
  expect(await call(page, (h) => h.drawn(0).geosets)).toEqual(before.geosets)

  await page.getByRole('button', { name: 'Randomize' }).click()
  const custom = await call(page, (h) => h.store.get().look.custom)
  expect(Object.keys(custom).length).toBeGreaterThan(0)
})

test('the Sets tab equips every piece of a set in one click', async ({ page }) => {
  await openApp(page)
  const panel = page.getByRole('region', { name: 'Find items' })
  await panel.getByRole('radio', { name: 'sets' }).click()
  await page.getByLabel('Search sets').fill('warlord')
  const first = page.locator('.results li button').first()
  await expect(first).toContainText('Warlord')
  await first.click()
  await expect(page.getByRole('region', { name: 'Equipped' }).locator('.slots li')).toHaveCount(6)
  await settled(page, 1)
})

test('Run plays a different pose from Stand, loaded as its own file', async ({ page }) => {
  await openApp(page)
  await call(page, (h) => h.commands.set_character(2, 0, 'hd'))
  await settled(page, 1, bare)
  await call(page, (h) => h.setTime(0.3))
  const stand = await call(page, (h, n) => h.nodeWorld(0, n), HEAD)
  const anim = page.waitForResponse((r) => /\/anims\/\d+\/5\.glb$/.test(r.url()))
  expect(await call(page, (h) => h.commands.set_animation('Run'))).not.toHaveProperty('error')
  expect((await anim).status()).toBe(200)
  await settled(page, 1)
  await call(page, (h) => h.setTime(0.3))
  const run = await call(page, (h, n) => h.nodeWorld(0, n), HEAD)
  expect(Math.hypot(run![0] - stand![0], run![1] - stand![1], run![2] - stand![2])).toBeGreaterThan(0.01)
  await expect(page.getByLabel('Animation')).toHaveValue('Run')
})

test('an empty search shows a hint; typing finds Thunderfury', async ({ page }) => {
  await openApp(page)
  const panel = page.getByRole('region', { name: 'Find items' })
  await expect(panel.getByText('Type an item name or ID to search')).toBeVisible()
  await expect(panel.locator('.results li')).toHaveCount(0)
  await panel.getByRole('searchbox').fill(String(THUNDERFURY))
  await expect(panel.locator('.results li')).toHaveCount(1)
  await expect(panel.locator('.results li')).toContainText('Thunderfury')
})

test.describe('a bogus item', () => {
  test.use({ allowFailures: [new RegExp(`/items/${BOGUS_ITEM}/`)] })

  test('is reported, and is the only failed request', async ({ page }) => {
    await openApp(page)
    const resolved = page.waitForResponse((r) => r.url().includes(`/items/${BOGUS_ITEM}/resolved`))
    await call(page, (h, id) => h.commands.equip_item('head', id), BOGUS_ITEM)
    expect((await resolved).status()).toBe(404)
    await settled(page, 1)
    const notices = await call(page, (h) => Object.values(h.store.get().notices).flat())
    expect(notices.join('\n')).toContain(String(BOGUS_ITEM))
  })
})
