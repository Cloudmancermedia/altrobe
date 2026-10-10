import type { AssistantProvider, AssistantTurn } from '../api/types'

export interface Preset {
  id: string
  label: string
  provider: AssistantProvider
  /** Filled in for OpenAI-compatible servers; "other" leaves it to the player. */
  baseUrl?: string
  /** A suggestion the player can change. Empty where it depends on what the player has. */
  model: string
  needsKey: boolean
  hint: string
  /** True once someone has run real prompts through it and reported back. */
  tested: boolean
}

export const PRESETS: Preset[] = [
  { id: 'openai', label: 'OpenAI', provider: 'openai-compatible', baseUrl: 'https://api.openai.com/v1', model: 'gpt-5-nano', needsKey: true, hint: 'Uses your OpenAI API key. You pay OpenAI per request.', tested: false },
  { id: 'anthropic', label: 'Anthropic (Claude)', provider: 'anthropic', model: 'claude-haiku-4-5', needsKey: true, hint: 'Uses your Anthropic API key. You pay Anthropic per request.', tested: false },
  { id: 'openrouter', label: 'OpenRouter', provider: 'openai-compatible', baseUrl: 'https://openrouter.ai/api/v1', model: '', needsKey: true, hint: 'Uses your OpenRouter key and any model it offers.', tested: false },
  { id: 'ollama', label: 'Ollama (free, on this computer)', provider: 'openai-compatible', baseUrl: 'http://127.0.0.1:11434/v1', model: 'qwen3', needsKey: false, hint: 'Free. Needs Ollama running and a model that can call tools.', tested: false },
  { id: 'lmstudio', label: 'LM Studio (free, on this computer)', provider: 'openai-compatible', baseUrl: 'http://127.0.0.1:1234/v1', model: '', needsKey: false, hint: 'Free. Start the LM Studio server and load a model that can call tools.', tested: false },
  { id: 'other', label: 'Other OpenAI-compatible server', provider: 'openai-compatible', model: '', needsKey: false, hint: 'Any server that speaks the OpenAI chat API.', tested: false },
  { id: 'claude-code', label: 'Claude Code (your Claude plan)', provider: 'claude-code', model: 'haiku', needsKey: false, hint: "Runs the Claude Code installed on this computer, signed in to your Claude plan. Prompts count against the plan's usage limits.", tested: true },
]

const trim = (u: string | null | undefined) => (u ?? '').replace(/\/+$/, '')

/** The preset saved settings came from: by provider, then by server address. Unsaved settings start at the first. */
export function presetFor(s: { provider: AssistantProvider | null; baseUrl: string | null }): Preset {
  if (!s.provider) return PRESETS[0]
  if (s.provider === 'anthropic' || s.provider === 'claude-code') return PRESETS.find((p) => p.provider === s.provider)!
  return PRESETS.find((p) => p.baseUrl && trim(p.baseUrl) === trim(s.baseUrl)) ?? PRESETS.find((p) => p.id === 'other')!
}

/** The server takes at most ten earlier turns. */
export const trimHistory = (turns: AssistantTurn[]): AssistantTurn[] => turns.slice(-10)
