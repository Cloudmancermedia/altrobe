import { defineConfig, devices } from '@playwright/test'

// Browser checks against the real local server and a real install. See e2e/global-setup.ts.
export default defineConfig({
  testDir: 'e2e',
  globalSetup: './e2e/global-setup.ts',
  // One server and one converted-asset cache for the whole run.
  workers: 1,
  // The first view of each character converts its models, which takes a while.
  timeout: 180_000,
  expect: { timeout: 90_000 },
  reporter: [['list']],
  use: { trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
