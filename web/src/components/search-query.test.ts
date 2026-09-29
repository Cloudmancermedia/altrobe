import { describe, expect, test } from 'vitest'
import { SEARCH_HINT, searchText } from './search-query'

describe('searchText', () => {
  test('an empty or blank query means no search', () => {
    expect(searchText('')).toBeNull()
    expect(searchText('   ')).toBeNull()
    expect(searchText('\t\n')).toBeNull()
  })

  test('a name or ID is trimmed and searched', () => {
    expect(searchText('  thunderfury ')).toBe('thunderfury')
    expect(searchText('19019')).toBe('19019')
  })

  test('the hint tells the user what to type', () => {
    expect(SEARCH_HINT).toBe('Type an item name or ID to search')
  })
})
