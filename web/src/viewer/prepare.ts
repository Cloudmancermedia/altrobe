// Fetches what one character needs and works out how to dress it. No three.js here.
import { getBaseLook, getResolvedItem } from '../api/client'
import type { BaseLook, ModelSet, ResolvedItem } from '../api/types'
import { checkAgainstData, defaultsFrom, dressStateFor, type Look } from '../look/look'
import { dress, type Dressed } from './dress'

export interface CharacterSpec {
  race: number
  sex: 0 | 1
  models: ModelSet
}

export interface Prepared {
  baseLook: BaseLook
  dressed: Dressed
  resolved: ResolvedItem[]
  notices: string[]
}

/**
 * @param look  the outfit; its race, sex and models are replaced by `spec`
 * @param main  true for the main character. Its customizations and build are checked; characters shown
 *   side by side use their defaults, since customization option IDs belong to one body model.
 */
export async function prepareCharacter(spec: CharacterSpec, look: Look, main: boolean): Promise<Prepared> {
  const baseLook = await getBaseLook(spec.race, spec.sex, spec.models)
  const ids = [...new Set(Object.values(look.items))] as number[]
  const resolvedList = await Promise.all(ids.map(async (id) => [id, await getResolvedItem(id, spec.race, spec.sex, spec.models)] as const))
  const resolvedById = new Map(resolvedList)
  const checked = checkAgainstData(
    { ...look, ...spec, custom: main ? look.custom : {} },
    { build: main ? baseLook.build : undefined, defaults: defaultsFrom(baseLook), resolvedById },
  )
  const dressed = dress(baseLook, dressStateFor(checked.look), resolvedById as Map<number, ResolvedItem>)
  return {
    baseLook,
    dressed,
    resolved: resolvedList.map(([, r]) => r).filter((r): r is ResolvedItem => !!r && !r.error),
    notices: [...checked.notices, ...dressed.notes],
  }
}
