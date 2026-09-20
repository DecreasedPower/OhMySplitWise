import { describe, expect, it } from 'vitest'
import { formatMoney, parseMoney, splitEqually, sumMoney } from './money'

describe('money utilities', () => {
  it.each([
    ['1250,50', '125050'],
    ['0.01', '1'],
    ['90071992547409999,99', '9007199254740999999'],
    ['12', '1200'],
    ['92233720368547758,07', '9223372036854775807'],
  ])('parses %s exactly', (input, expected) => expect(parseMoney(input)).toBe(expected))

  it.each(['', '0', '-1', '1.001', 'hello', '92233720368547758,08', '999999999999999999999999'])('rejects invalid amount %s', (input) => {
    expect(parseMoney(input)).toBeNull()
  })

  it('formats without converting through Number', () => {
    expect(formatMoney('9007199254740999999')).toBe('90 071 992 547 409 999,99 ₽')
    expect(formatMoney('-101')).toBe('−1,01 ₽')
  })

  it('preserves every kopeck in equal splits', () => {
    const shares = splitEqually('100', ['a', 'b', 'c'])
    expect(shares.map((share) => share.amountKopecks)).toEqual(['34', '33', '33'])
    expect(sumMoney(shares.map((share) => share.amountKopecks))).toBe(100n)
  })

  it('does not create zero-value equal shares', () => {
    expect(splitEqually('2', ['a', 'b', 'c'])).toEqual([])
  })
})
