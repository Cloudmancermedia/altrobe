import type { Status } from '../api/types'

export interface InstallChoice { path: string; product: string; build: string; isForever: boolean; label: string }

/** Every product found, Forever first, labelled for the picker. */
export function installChoices(status: Status): InstallChoice[] {
  return status.installs
    .flatMap((i) => i.products.map((p) => ({
      path: i.path, product: p.product, build: p.build, isForever: !!p.isForever,
      label: `${p.isForever ? 'Forever · ' : ''}${p.product} ${p.build} · ${i.path}`,
    })))
    .sort((a, b) => Number(b.isForever) - Number(a.isForever))
}

/** The Forever product to select without asking, when there is exactly one. */
export function foreverChoice(status: Status): { path: string; product: string } | null {
  const forever = installChoices(status).filter((c) => c.isForever)
  return forever.length === 1 ? { path: forever[0].path, product: forever[0].product } : null
}

/** The notice when selecting an install automatically failed. Server messages end in a full stop already. */
export function openFailedNotice(choice: { path: string; product: string }, message: string): string {
  const reason = /[.!?]$/.test(message) ? message : `${message}.`
  return `Could not open ${choice.product} at ${choice.path}: ${reason} Choose an install.`
}
