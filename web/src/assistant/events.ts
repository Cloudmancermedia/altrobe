import type { AssistantEvent } from '../api/types'

/** Reads the prompt endpoint's NDJSON body, calling onEvent for each line as it arrives. */
export async function readEvents(body: ReadableStream<Uint8Array>, onEvent: (e: AssistantEvent) => void): Promise<void> {
  const reader = body.getReader()
  // stream: true keeps a character split across chunks intact.
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (value) buffer += decoder.decode(value, { stream: true })
    let nl
    while ((nl = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, nl).trim()
      buffer = buffer.slice(nl + 1)
      if (line) onEvent(JSON.parse(line) as AssistantEvent)
    }
    if (done) break
  }
  if (buffer.trim()) onEvent(JSON.parse(buffer) as AssistantEvent)
}

const labels: Record<string, (a: Record<string, unknown>) => string> = {
  search_items: (a) => `Searching items: ${a.query ?? ''}`.trim(),
  search_sets: (a) => `Searching sets: ${a.query ?? ''}`.trim(),
  build_outfit: (a) => `Building a level ${a.level ?? '?'} outfit`,
  equip_item: (a) => `Equipping ${a.slot ?? 'an item'}`,
  equip_set: () => 'Equipping a set',
  unequip: (a) => `Clearing ${a.slot ?? 'a slot'}`,
  set_character: () => 'Changing the character',
  set_customization: () => 'Changing a customization',
  randomize_customization: () => 'Randomizing the look',
  reset_customization: () => 'Resetting the look',
  compare: () => 'Setting up side by side',
  set_visibility: (a) => `${a.visible === false ? 'Hiding' : 'Showing'} ${a.slot ?? 'a slot'}`,
  set_view: (a) => `Turning to the ${a.view ?? ''} view`.replace('  ', ' '),
  get_look: () => 'Checking the current look',
  list_characters: () => 'Listing races',
  share_link: () => 'Making a share link',
}

/** Token use for one reply, such as "1,200 tokens in, 15 out". */
export function usageLabel(e: Extract<AssistantEvent, { type: 'usage' }>): string {
  return `${e.inputTokens.toLocaleString()} tokens in, ${e.outputTokens.toLocaleString()} out${e.plan ? ', on your Claude plan' : ''}`
}

/** One plain line for a tool call, such as "Searching items: Thunderfury". */
export function stepLabel(e: Extract<AssistantEvent, { type: 'step' }>): string {
  const label = labels[e.tool]
  return label ? label(e.arguments ?? {}) : e.tool.replaceAll('_', ' ')
}
