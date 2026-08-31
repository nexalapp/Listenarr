import { describe, it, expect } from 'vitest'
import { formatSeriesMemberships, isSeriesRestatement } from '@/utils/seriesUtils'

describe('formatSeriesMemberships', () => {
  it('lists every series a book belongs to with its number', () => {
    const result = formatSeriesMemberships({
      series: 'Publication Order',
      seriesNumber: '1',
      seriesMemberships: [
        { seriesName: 'Publication Order', seriesNumber: '1', isPrimary: true, sortOrder: 0 },
        { seriesName: 'Chronological Order', seriesNumber: '3', isPrimary: false, sortOrder: 1 },
      ],
    })
    expect(result).toBe('Publication Order #1, Chronological Order #3')
  })

  it('omits the number when a membership has none', () => {
    const result = formatSeriesMemberships({
      seriesMemberships: [{ seriesName: 'Standalone Saga', isPrimary: true, sortOrder: 0 }],
    })
    expect(result).toBe('Standalone Saga')
  })

  it('falls back to the legacy single series when there are no memberships', () => {
    expect(formatSeriesMemberships({ series: 'Solo Series', seriesNumber: '2' })).toBe(
      'Solo Series #2',
    )
    expect(formatSeriesMemberships({ series: 'No Number' })).toBe('No Number')
  })

  it('returns an empty string when there is no series information', () => {
    expect(formatSeriesMemberships({})).toBe('')
    expect(formatSeriesMemberships({ seriesMemberships: [] })).toBe('')
  })
})

describe('isSeriesRestatement', () => {
  it('recognises a subtitle that only repeats the series and position', () => {
    expect(isSeriesRestatement('Paragon Space, Book 1', ['Paragon Space'])).toBe(true)
    expect(isSeriesRestatement('Paragon Space Book 1', ['Paragon Space'])).toBe(true)
    expect(isSeriesRestatement('Paragon Space, Vol. 2', ['Paragon Space'])).toBe(true)
    expect(isSeriesRestatement('The Expanse, Book 1.5', ['The Expanse'])).toBe(true)
    expect(isSeriesRestatement('Paragon Space, Book One', ['Paragon Space'])).toBe(true)
    expect(isSeriesRestatement('Book 1 of the Paragon Space', ['Paragon Space'])).toBe(true)
  })

  it('recognises the series name on its own', () => {
    expect(isSeriesRestatement('Paragon Space', ['Paragon Space'])).toBe(true)
    expect(isSeriesRestatement('Paragon Space Series', ['Paragon Space'])).toBe(true)
    expect(isSeriesRestatement('Paragon Space, Book 1', ['Paragon Space Series'])).toBe(true)
  })

  it('matches any of the series a book belongs to', () => {
    expect(isSeriesRestatement("Ender's Saga, Book 1", ['Enderverse', "Ender's Saga"])).toBe(true)
  })

  it('keeps a subtitle that says something of its own', () => {
    expect(isSeriesRestatement('A Heroic Saga', ['Dune Series'])).toBe(false)
    expect(isSeriesRestatement('Paragon Space, Book 1: A Space Opera', ['Paragon Space'])).toBe(
      false,
    )
    expect(isSeriesRestatement('A Paragon Space Novel', ['Paragon Space'])).toBe(false)
    expect(isSeriesRestatement('Paragon Space', ['Other Series'])).toBe(false)
  })

  it('keeps the subtitle when there is no series to compare against', () => {
    expect(isSeriesRestatement('Paragon Space, Book 1', [])).toBe(false)
    expect(isSeriesRestatement('Paragon Space, Book 1', ['', '   '])).toBe(false)
    expect(isSeriesRestatement('', ['Paragon Space'])).toBe(false)
  })
})
