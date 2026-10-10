import { useEffect, useRef, useState } from 'react'
import { ApiError, getAssistantSettings, promptAssistant, saveAssistantSettings, testAssistant } from '../api/client'
import type { AssistantSettingsView, AssistantTurn } from '../api/types'
import { stepLabel, usageLabel } from '../assistant/events'
import { PRESETS, presetFor, trimHistory } from '../assistant/presets'

// One entry in this tab's prompt transcript. History for the model is only the user and reply texts.
type Line =
  | { kind: 'you'; text: string }
  | { kind: 'step'; text: string }
  | { kind: 'reply'; text: string }
  | { kind: 'usage'; text: string }
  | { kind: 'error'; text: string }

/** Plain-language prompts with the player's own model. Tool calls change the view live. */
export function PromptPanel() {
  const [settings, setSettings] = useState<AssistantSettingsView | null>(null)
  const [editing, setEditing] = useState(false)
  const [text, setText] = useState('')
  const [lines, setLines] = useState<Line[]>([])
  const [running, setRunning] = useState(false)
  const history = useRef<AssistantTurn[]>([])
  const abort = useRef<AbortController | null>(null)

  useEffect(() => {
    getAssistantSettings().then((s) => { setSettings(s); if (!s.provider) setEditing(true) }).catch(() => {})
    return () => abort.current?.abort()
  }, [])

  const add = (line: Line) => setLines((ls) => [...ls, line].slice(-60))

  const send = async () => {
    const prompt = text.trim()
    if (!prompt || running) return
    setText('')
    setRunning(true)
    add({ kind: 'you', text: prompt })
    const controller = new AbortController()
    abort.current = controller
    let reply = ''
    try {
      await promptAssistant(prompt, trimHistory(history.current), (e) => {
        if (e.type === 'step') add({ kind: 'step', text: stepLabel(e) })
        else if (e.type === 'reply') { reply = e.text; add({ kind: 'reply', text: e.text }) }
        else if (e.type === 'usage') add({ kind: 'usage', text: usageLabel(e) })
        else if (e.type === 'error') add({ kind: 'error', text: e.message })
      }, controller.signal)
      history.current = trimHistory([...history.current, { role: 'user', text: prompt }, ...(reply ? [{ role: 'assistant' as const, text: reply }] : [])])
    } catch (e) {
      if ((e as Error).name !== 'AbortError') {
        add({ kind: 'error', text: (e as Error).message })
        if (e instanceof ApiError && e.code === 'assistant_not_ready') setEditing(true)
      }
    } finally {
      setRunning(false)
      abort.current = null
    }
  }

  return (
    <section className="panel" aria-labelledby="prompt-heading">
      <h2 id="prompt-heading">Ask</h2>
      {editing || !settings ? (
        <AssistantSettingsForm settings={settings} onSaved={(s) => { setSettings(s); setEditing(false) }} onCancel={settings?.provider ? () => setEditing(false) : undefined} />
      ) : (
        <>
          <p className="muted small">
            {presetFor(settings).label}, {settings.model}{' '}
            <button type="button" className="link" onClick={() => setEditing(true)}>Settings</button>
          </p>
          {lines.length > 0 && (
            <ol className="prompt-lines small" aria-live="polite">
              {lines.map((l, i) => <li key={i} className={`prompt-${l.kind}`}>{l.text}</li>)}
            </ol>
          )}
          <form onSubmit={(e) => { e.preventDefault(); void send() }} className="prompt-form">
            <label htmlFor="prompt-text" className="visually-hidden">What should the character wear?</label>
            <textarea id="prompt-text" rows={2} value={text} disabled={running}
              placeholder="Put the Orc in Thunderfury and show it on an Undead female"
              onChange={(e) => setText(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send() } }} />
            {running
              ? <button type="button" onClick={() => abort.current?.abort()}>Stop</button>
              : <button type="submit" disabled={!text.trim()}>Send</button>}
          </form>
        </>
      )}
    </section>
  )
}

function AssistantSettingsForm({ settings, onSaved, onCancel }: {
  settings: AssistantSettingsView | null
  onSaved: (s: AssistantSettingsView) => void
  onCancel?: () => void
}) {
  const initial = presetFor({ provider: settings?.provider ?? null, baseUrl: settings?.baseUrl ?? null })
  const [presetId, setPresetId] = useState(initial.id)
  const preset = PRESETS.find((p) => p.id === presetId)!
  const [model, setModel] = useState(settings?.provider ? settings.model ?? '' : initial.model)
  const [baseUrl, setBaseUrl] = useState(settings?.baseUrl ?? initial.baseUrl ?? '')
  const [apiKey, setApiKey] = useState('')
  const [status, setStatus] = useState<{ ok: boolean; text: string } | null>(null)
  const [busy, setBusy] = useState(false)

  const choose = (id: string) => {
    const p = PRESETS.find((x) => x.id === id)!
    setPresetId(id)
    setModel(p.model)
    setBaseUrl(p.baseUrl ?? '')
    setStatus(null)
  }

  const save = async (thenTest: boolean) => {
    setBusy(true)
    setStatus(null)
    try {
      const saved = await saveAssistantSettings({
        provider: preset.provider,
        baseUrl: preset.provider === 'openai-compatible' ? baseUrl.trim() : undefined,
        model: model.trim(),
        apiKey: preset.provider === 'claude-code' ? undefined : apiKey.trim() || undefined,
      })
      setApiKey('')
      if (!thenTest) { onSaved(saved); return }
      const r = await testAssistant()
      setStatus(r.ok ? { ok: true, text: `${r.model} answered. Ready.` } : { ok: false, text: r.message ?? 'The test request failed.' })
      if (r.ok) onSaved(saved)
    } catch (e) {
      setStatus({ ok: false, text: (e as Error).message })
    } finally {
      setBusy(false)
    }
  }

  const keyNote = settings?.keyFromEnvironment
    ? 'Using the key from ALTROBE_LLM_KEY.'
    : settings?.maskedKey ? `Saved key ${settings.maskedKey}. Leave empty to keep it.` : undefined

  return (
    <form className="prompt-settings small" onSubmit={(e) => { e.preventDefault(); void save(true) }}>
      <p className="muted">Type what you want and a model dresses the character. It runs on your own API key, paid to your provider, on a free model on this computer, or through Claude Code signed in to your Claude plan.</p>
      <label htmlFor="assistant-preset">Provider</label>
      <select id="assistant-preset" value={presetId} onChange={(e) => choose(e.target.value)}>
        {PRESETS.map((p) => <option key={p.id} value={p.id}>{p.label}</option>)}
      </select>
      <p className="muted">{preset.hint}</p>
      {!preset.tested && (
        <p className="muted">
          Untested: nobody has run real prompts through this provider yet, so the suggested model is a guess.{' '}
          <a href="https://github.com/Cloudmancermedia/altrobe/issues" target="_blank" rel="noreferrer">Report what worked</a>.
        </p>
      )}
      {/* Hosted providers have fixed addresses; local servers' ports vary. */}
      {['ollama', 'lmstudio', 'other'].includes(preset.id) && (
        <>
          <label htmlFor="assistant-url">Server address</label>
          <input id="assistant-url" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="http://127.0.0.1:11434/v1" />
        </>
      )}
      <label htmlFor="assistant-model">Model</label>
      <input id="assistant-model" value={model} onChange={(e) => setModel(e.target.value)} placeholder="Model name" />
      {/* Claude Code signs in on its own; Altrobe never sees that login. */}
      {preset.provider !== 'claude-code' && (
        <>
          <label htmlFor="assistant-key">API key{preset.needsKey ? '' : ' (optional)'}</label>
          <input id="assistant-key" type="password" autoComplete="off" value={apiKey} onChange={(e) => setApiKey(e.target.value)}
            placeholder={settings?.maskedKey ?? (preset.needsKey ? 'Paste your key' : 'Not needed')} />
          {keyNote && <p className="muted">{keyNote}</p>}
          <p className="muted">The key is stored on this computer only, readable by your user account, and sent only to the provider above.</p>
        </>
      )}
      {status && <p className={status.ok ? '' : 'error'} role="status">{status.text}</p>}
      <div className="prompt-settings-actions">
        <button type="submit" disabled={busy || !model.trim()}>Save and test</button>
        {onCancel && <button type="button" onClick={onCancel} disabled={busy}>Cancel</button>}
      </div>
    </form>
  )
}
