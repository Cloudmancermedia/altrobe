export const SEARCH_HINT = 'Type an item name or ID to search'

/**
 * The text to search for, or null for no search. With no text and no filters the result would be
 * every item alphabetically, starting with internal test items, so the app does not ask for it.
 * A slot or quality filter on its own still browses.
 */
export function searchText(q: string, filters: { slot?: string; quality?: string } = {}): string | null {
  const t = q.trim()
  return t === '' && !filters.slot && !filters.quality ? null : t
}
