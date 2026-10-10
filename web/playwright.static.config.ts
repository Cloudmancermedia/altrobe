import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { defineConfig, devices } from '@playwright/test'

// Browser check of the hosted site against a baked bundle (output/bundle, or ALTROBE_BUNDLE_DIR),
// served by `npm run dev:static`. No game install and no local server. Playwright starts the one
// dev server and stops it when the run ends.
const bundle = process.env.ALTROBE_BUNDLE_DIR ?? fileURLToPath(new URL('../output/bundle', import.meta.url))
const port = Number(process.env.ALTROBE_STATIC_PORT ?? 5190)
process.env.ALTROBE_E2E_URL = `http://127.0.0.1:${port}/`
process.env.ALTROBE_STATIC_BUNDLE = existsSync(bundle) ? '1' : ''

export default defineConfig({
  testDir: 'e2e',
  testMatch: 'static.spec.ts',
  workers: 1,
  timeout: 120_000,
  expect: { timeout: 60_000 },
  reporter: [['list']],
  use: { trace: 'retain-on-failure' },
  webServer: {
    command: `npm run dev:static -- --host 127.0.0.1 --port ${port} --strictPort`,
    url: process.env.ALTROBE_E2E_URL,
    reuseExistingServer: false,
    timeout: 60_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
