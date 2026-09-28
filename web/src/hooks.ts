import { useEffect, useState } from 'react'
import { getBaseLook } from './api/client'
import type { BaseLook, ModelSet } from './api/types'

/** The base look for a character, or null while it loads or when it cannot be loaded. */
export function useBaseLook(race: number, sex: number, models: ModelSet, enabled = true): BaseLook | null {
  const [state, setState] = useState<{ key: string; look: BaseLook | null }>({ key: '', look: null })
  const key = `${race}:${sex}:${models}`
  useEffect(() => {
    if (!enabled) return
    let live = true
    getBaseLook(race, sex, models).then((look) => { if (live) setState({ key, look }) }, () => { if (live) setState({ key, look: null }) })
    return () => { live = false }
  }, [key, race, sex, models, enabled])
  return state.key === key ? state.look : null
}
