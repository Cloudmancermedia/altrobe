import { test as base, expect, type JSHandle, type Page } from '@playwright/test'

/** window.__altrobe from src/test-hooks.ts, as far as the checks use it. */
export interface Hooks {
  store: { get(): {
    characters: { races: { race: number; name: string; sexes: { sex: number; hd: boolean; sd: boolean }[] }[] } | null
    notices: Record<string, string[]>
    options: { optionId: number; name: string; defaultChoiceId: number | null; choices: { choiceId: number }[] }[]
    look: { custom: Record<string, number> }
  } }
  commands: {
    set_customization(optionId: number, choiceId: number): { error?: string }
    randomize_customization(): { error?: string }
    reset_customization(): { error?: string }
    set_animation(name: string): { error?: string }
    set_character(race: number, sex: number, models?: string): { error?: string }
    equip_item(slot: string, itemId: number): { error?: string }
    compare(list: { race: number; sex: number; models?: string }[]): { error?: string }
    get_look(): unknown
    share_link(): string
  }
  setTime(t: number): void
  nodeWorld(i: number, name: string): number[] | null
  drawn(i: number): { geosets: number[]; layerFiles: number[] }
  attached(i: number): { itemId: number; attachmentId: number; rootName: string }[]
  itemNodesLocal(i: number, rootName: string): Record<string, number[]> | null
}
declare global {
  interface Window { __altrobe?: unknown }
}

/** Runs `fn` in the page with the test hooks as its first argument. */
export async function call<R, A = undefined>(page: Page, fn: (h: Hooks, arg: A) => R, arg?: A): Promise<R> {
  const h = await page.evaluateHandle(() => window.__altrobe) as JSHandle<Hooks>
  // Playwright's own typing of the argument (Unboxed<A>) does not line up with a plain generic.
  try { return await h.evaluate(fn as never, arg) as R } finally { await h.dispose() }
}

const isLocal = (url: string) => {
  const u = new URL(url)
  return !u.protocol.startsWith('http') || u.hostname === '127.0.0.1' || u.hostname === 'localhost'
}

/**
 * Records console errors, page errors, failed requests and any request that leaves this machine.
 * `allow` lists URL patterns that a test fails on purpose.
 */
export function watch(page: Page, allow: RegExp[] = []) {
  const problems: string[] = []
  const allowed = (url: string) => allow.some((re) => re.test(url))
  page.on('console', (m) => { if (m.type() === 'error' && !allowed(m.location().url)) problems.push(`console: ${m.text()} (${m.location().url})`) })
  page.on('pageerror', (e) => problems.push(`page error: ${e.message}`))
  page.on('request', (r) => { if (!isLocal(r.url())) problems.push(`left the machine: ${r.url()}`) })
  // A cancelled fetch (a superseded search) is not a failure.
  page.on('requestfailed', (r) => { if (r.failure()?.errorText !== 'net::ERR_ABORTED' && !allowed(r.url())) problems.push(`request failed: ${r.url()} ${r.failure()?.errorText}`) })
  page.on('response', (r) => { if (r.status() >= 400 && !allowed(r.url())) problems.push(`HTTP ${r.status()}: ${r.url()}`) })
  return problems
}

export const appUrl = () => process.env.ALTROBE_E2E_URL!

/** Opens the app (optionally at a share link) and waits for the main character. */
export async function openApp(page: Page, url = appUrl()) {
  await page.goto(url)
  await settled(page, 1)
}

/**
 * Waits until `count` characters are on screen and built, and `check` is true. `check` runs in the
 * page, so it sees only its arguments, not variables from the test.
 */
export async function settled<A>(page: Page, count: number, check: (h: Hooks, arg: A) => boolean = () => true, arg?: A) {
  await expect.poll(async () => {
    const states = await page.locator('.stage-cell').evaluateAll((cells) => cells.map((c) => (c as HTMLElement).dataset.state))
    if (states.includes('error')) return 'a character failed to load'
    return states.length === count && states.every((s) => s === 'ready') && await call(page, check, arg)
  }).toBe(true)
}

export const test = base.extend<{ allowFailures: RegExp[]; problems: string[] }>({
  allowFailures: [[], { option: true }],
  // Every test fails on console errors, failed requests or network access, unless allowed.
  problems: [async ({ page, allowFailures }, use) => {
    const problems = watch(page, allowFailures)
    await use(problems)
    expect(problems, 'console errors, failed requests or network access').toEqual([])
  }, { auto: true }],
})

export { expect }
