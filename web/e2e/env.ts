import { existsSync } from 'node:fs'

// The browser checks need a real World of Warcraft: Forever install, so they are opt-in.
const DEFAULT_WOW = '/Applications/World of Warcraft'
export const wowPath = process.env.ALTROBE_WOW_PATH || (existsSync(DEFAULT_WOW) ? DEFAULT_WOW : undefined)
export const enabled = process.env.ALTROBE_E2E === '1' && wowPath !== undefined
export const skipReason = process.env.ALTROBE_E2E !== '1'
  ? 'set ALTROBE_E2E=1 to run the browser checks against a real install'
  : `no install: set ALTROBE_WOW_PATH or install to ${DEFAULT_WOW}`
