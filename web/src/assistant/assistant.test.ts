import { describe, expect, it } from 'vitest'
import { PRESETS, presetFor, trimHistory } from './presets'
import { readEvents, stepLabel, usageLabel } from './events'
import type { AssistantEvent } from '../api/types'

describe('presets', () => {
  it('fill in each provider and say which need a key', () => {
    const ollama = PRESETS.find((p) => p.id === 'ollama')!
    expect(ollama).toMatchObject({ provider: 'openai-compatible', baseUrl: 'http://127.0.0.1:11434/v1', needsKey: false })
    expect(PRESETS.find((p) => p.id === 'anthropic')).toMatchObject({ provider: 'anthropic', model: 'claude-haiku-4-5', needsKey: true })
  })

  it('mark only Claude Code tested; the rest wait for someone to report a real run', () => {
    expect(PRESETS.filter((p) => p.tested).map((p) => p.id)).toEqual(['claude-code'])
  })

  it('recognise saved settings by provider and address, else Other', () => {
    expect(presetFor({ provider: 'anthropic', baseUrl: null }).id).toBe('anthropic')
    expect(presetFor({ provider: 'openai-compatible', baseUrl: 'http://127.0.0.1:1234/v1' }).id).toBe('lmstudio')
    expect(presetFor({ provider: 'openai-compatible', baseUrl: 'https://llm.example.com/v1' }).id).toBe('other')
    expect(presetFor({ provider: null, baseUrl: null }).id).toBe('openai')
  })

  it('offer Claude Code last, with no key or address', () => {
    const last = PRESETS[PRESETS.length - 1]
    expect(last).toMatchObject({ id: 'claude-code', provider: 'claude-code', needsKey: false })
    expect(last.baseUrl).toBeUndefined()
    expect(presetFor({ provider: 'claude-code', baseUrl: null }).id).toBe('claude-code')
  })

  it('keep the last ten turns', () => {
    const turns = Array.from({ length: 13 }, (_, i) => ({ role: 'user' as const, text: `t${i}` }))
    expect(trimHistory(turns).map((t) => t.text)).toEqual(turns.slice(3).map((t) => t.text))
  })
})

describe('streamed events', () => {
  const stream = (...chunks: string[]) => new ReadableStream<Uint8Array>({
    start(c) { for (const s of chunks) c.enqueue(new TextEncoder().encode(s)); c.close() },
  })

  it('parse one event per line, even when a line is split across chunks', async () => {
    const seen: AssistantEvent[] = []
    await readEvents(stream('{"type":"step","tool":"search_items","argu', 'ments":{"query":"Thunderfury"}}\n{"type":"reply","text":"Done."}\n', '{"type":"done"}\n'), (e) => seen.push(e))
    expect(seen.map((e) => e.type)).toEqual(['step', 'reply', 'done'])
  })

  it('describe token use, saying when it counts against a Claude plan', () => {
    expect(usageLabel({ type: 'usage', inputTokens: 1200, outputTokens: 15 })).toBe('1,200 tokens in, 15 out')
    expect(usageLabel({ type: 'usage', inputTokens: 1200, outputTokens: 15, plan: true })).toBe('1,200 tokens in, 15 out, on your Claude plan')
  })

  it('describe steps in plain words', () => {
    expect(stepLabel({ type: 'step', tool: 'search_items', arguments: { query: 'Thunderfury' } })).toBe('Searching items: Thunderfury')
    expect(stepLabel({ type: 'step', tool: 'equip_item', arguments: { slot: 'mainhand', itemId: 19019 } })).toBe('Equipping mainhand')
    expect(stepLabel({ type: 'step', tool: 'wear_main_outfit', arguments: {} })).toBe('wear main outfit')
  })
})
