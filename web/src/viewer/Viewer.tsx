import { useEffect, useRef, useState } from 'react'
import { modelUrl, textureUrl } from '../api/client'
import type { ResolvedItem } from '../api/types'
import type { Look, View } from '../look/look'
import { buildCharacter, type AssetUrls } from './character'
import { prepareCharacter, type CharacterSpec } from './prepare'
import { Stage, type StageView } from './stage'

export interface ViewerCell {
  key: string
  label: string
  spec: CharacterSpec
  main: boolean
}

interface ViewerProps {
  cells: ViewerCell[]
  look: Look
  itemSpeed?: number
  onNotices: (cellKey: string, notices: string[]) => void
  onResolved?: (items: ResolvedItem[]) => void
  onStage?: (stage: Stage | null) => void
}

/** Hosts the shared three.js stage and one view per character. */
export function Viewer({ cells, look, itemSpeed, onNotices, onResolved, onStage }: ViewerProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [stage, setStage] = useState<Stage | null>(null)

  useEffect(() => {
    const s = new Stage(canvasRef.current!)
    setStage(s)
    onStage?.(s)
    return () => { onStage?.(null); s.dispose() }
    // The stage lives as long as the component; onStage is a notification only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="stage">
      <canvas ref={canvasRef} className="stage-canvas" aria-hidden="true" />
      <div className={`stage-grid count-${cells.length}`}>
        {stage && cells.map((c) => (
          <StageCell key={c.key} stage={stage} cell={c} look={look} itemSpeed={itemSpeed} onNotices={onNotices} onResolved={onResolved} />
        ))}
      </div>
    </div>
  )
}

const assets = (build: string): AssetUrls => ({
  model: (fdid, ext) => modelUrl(build, fdid, ext),
  texture: (fdid) => textureUrl(build, fdid),
})

type CellState = { key: string; state: 'ready' | 'error' }

function StageCell({ stage, cell, look, itemSpeed, onNotices, onResolved }: {
  stage: Stage; cell: ViewerCell; look: Look; itemSpeed?: number
  onNotices: ViewerProps['onNotices']; onResolved: ViewerProps['onResolved']
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [view, setView] = useState<StageView | null>(null)
  const [done, setDone] = useState<CellState | null>(null)
  const framedSpec = useRef<string | null>(null)
  const camView = look.cam.view
  const camViewRef = useRef<View>(camView)
  useEffect(() => { camViewRef.current = camView }, [camView])

  useEffect(() => {
    const v = stage.addView(ref.current!)
    setView(v)
    return () => stage.removeView(v)
  }, [stage])

  const specKey = `${cell.spec.race}:${cell.spec.sex}:${cell.spec.models}`
  // Only what changes the dressed result; camera moves do not rebuild the character.
  const outfitKey = JSON.stringify([look.items, look.hide, cell.main ? look.custom : null, cell.main ? look.build : null])
  const loadKey = `${specKey}|${outfitKey}|${itemSpeed}`
  const state = done?.key === loadKey ? done.state : 'loading'

  useEffect(() => {
    if (!view) return
    let cancelled = false
    ;(async () => {
      const prepared = await prepareCharacter(cell.spec, look, cell.main)
      if (cancelled) return
      onResolved?.(prepared.resolved)
      const character = await buildCharacter({ baseLook: prepared.baseLook, dressed: prepared.dressed, assets: assets(prepared.baseLook.build), itemSpeed })
      if (cancelled) { character.dispose(); return }
      const reframe = framedSpec.current !== specKey
      framedSpec.current = specKey
      view.setCharacter(character, reframe ? camViewRef.current : null)
      onNotices(cell.key, [...prepared.notices, ...character.notices])
      setDone({ key: loadKey, state: 'ready' })
    })().catch((e: Error) => {
      if (cancelled) return
      onNotices(cell.key, [`could not show ${cell.label}: ${e.message}`])
      setDone({ key: loadKey, state: 'error' })
    })
    return () => { cancelled = true }
    // look is read through outfitKey (part of loadKey); the callbacks are notifications only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view, loadKey])

  useEffect(() => {
    if (view && framedSpec.current) view.frame(camView)
  }, [view, camView])

  return (
    <div
      ref={ref}
      className="stage-cell"
      tabIndex={0}
      role="img"
      aria-label={`${cell.label} in the current outfit. Drag to turn, scroll to zoom, arrow keys to pan.`}
      data-cell={cell.key}
      data-state={state}
    >
      <span className="cell-label">{cell.label}</span>
      {state === 'loading' && <span className="cell-status">Loading…</span>}
      {state === 'error' && <span className="cell-status error">Could not show this character</span>}
    </div>
  )
}
