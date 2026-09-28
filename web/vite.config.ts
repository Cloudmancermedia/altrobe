/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig, type PluginOption } from 'vite'

// https://vite.dev/config/
export default defineConfig(async ({ command }) => {
  const plugins: PluginOption[] = [react()]
  // Dev only: answer the server's API from the Phase 1 spike's converted files (dev/spike-adapter.ts).
  // The import is dynamic so a production build never loads it.
  if (command === 'serve' && process.env.ALTROBE_DEV_ADAPTER === 'spike') {
    const outputDir = process.env.ALTROBE_SPIKE_OUTPUT
    if (!outputDir) throw new Error('ALTROBE_DEV_ADAPTER=spike needs ALTROBE_SPIKE_OUTPUT, the path to the spike\'s output/ folder')
    const { spikeAdapter } = await import('./dev/spike-adapter.ts')
    plugins.push(spikeAdapter({ outputDir }))
  }
  return {
    plugins,
    build: {
      // The server serves converted game assets under /assets/{build}/, so the app's own bundle goes
      // elsewhere to keep the two apart.
      assetsDir: 'static',
      // three.js is most of the bundle, and the app loads from a server on the same machine.
      chunkSizeWarningLimit: 1200,
    },
    test: {
      include: ['src/**/*.test.ts'],
    },
  }
})
