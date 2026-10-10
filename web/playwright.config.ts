import { defineConfig, devices } from '@playwright/test'

// Browser checks against the real local server and a real install. See e2e/global-setup.ts.
export default defineConfig({
  testDir: 'e2e',
  // e2e/*.test.ts are vitest unit tests for the setup code.
  testMatch: '*.spec.ts',
  globalSetup: './e2e/global-setup.ts',
  // One server for the whole run. Converted assets persist between runs (e2e/cache.ts).
  workers: 1,
  // The first view of each character converts its models, which takes a while.
  timeout: 180_000,
  expect: { timeout: 90_000 },
  // A slow or overloaded machine fails every test slowly; stop early instead of running for an hour.
  maxFailures: 2,
  globalTimeout: 30 * 60_000,
  reporter: [['list']],
  use: { trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
