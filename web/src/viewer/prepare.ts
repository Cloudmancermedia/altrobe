// Fetches what one character needs and works out how to dress it. No three.js here.
import { getBaseLook, getResolvedItem } from '../api/client'
import type { BaseLook, ModelSet, ResolvedItem } from '../api/types'
import { checkAgainstData, choicesFrom, defaultsFrom, dressStateFor, type Look } from '../look/look'
import { customize } from './customize'
import { dress, type Dressed } from './dress'

export interface CharacterSpec {
  race: number
  sex: 0 | 1
  models: ModelSet
}

export interface Prepared {
  /** The base look with the look's customizations applied to its geosets and layers. */
  baseLook: BaseLook
  dressed: Dressed
  resolved: ResolvedItem[]
  notices: string[]
}

/**
 * @param look  the outfit; its race, sex and models are replaced by `spec`
 * @param look  the outfit and customizations this character wears
 * @param main  true for the main character, whose build is checked against the data
 */
export async function prepareCharacter(spec: CharacterSpec, look: Look, main: boolean): Promise<Prepared> {
  const baseLook = await getBaseLook(spec.race, spec.sex, spec.models)
  const ids = [...new Set(Object.values(look.items))] as number[]
  const resolvedList = await Promise.all(ids.map(async (id) => [id, await getResolvedItem(id, spec.race, spec.sex, spec.models)] as const))
  const resolvedById = new Map(resolvedList)
  const checked = checkAgainstData(
    { ...look, ...spec },
    { build: main ? baseLook.build : undefined, defaults: defaultsFrom(baseLook), choices: choicesFrom(baseLook), resolvedById },
  )
  const custom = customize(baseLook, checked.look.custom)
  const customized = { ...baseLook, geosets: custom.geosets, layers: custom.layers }
  const dressed = dress(customized, dressStateFor(checked.look), resolvedById as Map<number, ResolvedItem>)
  return {
    baseLook: customized,
    dressed,
    resolved: resolvedList.map(([, r]) => r).filter((r): r is ResolvedItem => !!r && !r.error),
    notices: [...checked.notices, ...custom.notes, ...dressed.notes],
  }
}
