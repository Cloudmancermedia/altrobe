import { call, expect, openApp, settled, test, type Hooks } from './fixtures'

// The hosted site, reading a baked bundle (playwright.static.config.ts). Every request must succeed:
// a 404 here is a file the bake did not write or a URL the static backend built wrong.
test.skip(process.env.ALTROBE_STATIC_BUNDLE !== '1', 'no baked bundle: run `bake` or set ALTROBE_BUNDLE_DIR')

const THUNDERFURY = 19019

test('loads a character, equips an item and a set, with no install picker or prompt box', async ({ page }) => {
  await openApp(page)
  await expect(page.locator('[data-cell="main"] .cell-label')).not.toBeEmpty()
  await expect(page.getByRole('heading', { name: 'Choose a game install' })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Ask' })).toHaveCount(0)

  const panel = page.getByRole('region', { name: 'Find items' })
  await panel.getByRole('searchbox').fill(String(THUNDERFURY))
  const result = panel.locator('.results li button').first()
  await expect(result).toContainText('Thunderfury')
  await result.click()
  await settled(page, 1, (h: Hooks, id: number) => h.attached(0).some((a) => a.itemId === id), THUNDERFURY)

  await panel.getByRole('radio', { name: 'sets' }).click()
  await page.getByLabel('Search sets').fill('warlord')
  const set = page.locator('.results li button').first()
  await expect(set).toContainText('Warlord')
  await set.click()
  await expect(page.getByRole('region', { name: 'Equipped' }).locator('.slots li')).toHaveCount(7)
  await settled(page, 1)
  expect(await call(page, (h) => h.attached(0).length)).toBeGreaterThan(0)
})
