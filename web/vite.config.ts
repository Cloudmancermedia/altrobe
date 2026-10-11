/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig, type PluginOption, type ProxyOptions } from 'vite'

// https://vite.dev/config/
export default defineConfig(async ({ command, mode }) => {
  const plugins: PluginOption[] = [react()]
  // Dev only: answer the server's API from the spike's (tools/) converted files (dev/spike-adapter.ts).
  // The import is dynamic so a production build never loads it.
  if (command === 'serve' && process.env.ALTROBE_DEV_ADAPTER === 'spike') {
    const outputDir = process.env.ALTROBE_SPIKE_OUTPUT
    if (!outputDir) throw new Error('ALTROBE_DEV_ADAPTER=spike needs ALTROBE_SPIKE_OUTPUT, the path to the spike\'s output/ folder')
    const { spikeAdapter } = await import('./dev/spike-adapter.ts')
    plugins.push(spikeAdapter({ outputDir }))
  }
  // Dev only, static mode (`npm run dev:static`): serve a baked bundle at /bundle, as the hosted site
  // will. ALTROBE_BUNDLE_DIR picks it; the default is the repo's output/bundle from `bake`.
  if (command === 'serve' && mode === 'static') {
    const { bundleServer } = await import('./dev/bundle-server.ts')
    plugins.push(bundleServer({ root: process.env.ALTROBE_BUNDLE_DIR ?? fileURLToPath(new URL('../output/bundle', import.meta.url)) }))
  }
  // Dev only: ALTROBE_API=http://127.0.0.1:5161 sends /api and /assets to a running local server.
  // The server rejects cross-site Origin headers, and the dev page is a different origin, so the
  // proxy drops the header; Host stays localhost, which the server accepts.
  const api = command === 'serve' ? process.env.ALTROBE_API : undefined
  // `ws` also carries the viewer tab's command channel (/api/v1/session).
  const toServer: ProxyOptions | undefined = api ? {
    target: api,
    ws: true,
    configure: proxy => {
      proxy.on('proxyReq', req => req.removeHeader('origin'))
      proxy.on('proxyReqWs', req => req.removeHeader('origin'))
    },
  } : undefined
  return {
    plugins,
    server: toServer ? { proxy: { '/api': toServer, '/assets': toServer } } : undefined,
    build: {
      // The static site builds apart from the local app's dist/, which the server serves.
      outDir: mode === 'static' ? 'dist-static' : 'dist',
      // The server serves converted game assets under /assets/{build}/, so the app's own bundle goes
      // elsewhere to keep the two apart.
      assetsDir: 'static',
      // three.js is most of the bundle, and the app loads from a server on the same machine.
      chunkSizeWarningLimit: 1200,
    },
    test: {
      include: ['src/**/*.test.ts', 'e2e/**/*.test.ts', 'dev/**/*.test.ts'],
    },
  }
})
