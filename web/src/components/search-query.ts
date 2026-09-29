export const SEARCH_HINT = 'Type an item name or ID to search'

/**
 * The text to search for, or null when there is none. An empty query would list every item
 * alphabetically, which starts with internal test items, so the app does not ask for it.
 */
export function searchText(q: string): string | null {
  const t = q.trim()
  return t === '' ? null : t
}
