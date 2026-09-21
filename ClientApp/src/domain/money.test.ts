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
    const shares = splitEqually('100', ['30', '10', '20'], '20')
    expect(shares).toEqual([
      { participantId: '10', amountKopecks: '33' },
      { participantId: '20', amountKopecks: '34' },
      { participantId: '30', amountKopecks: '33' },
    ])
    expect(sumMoney(shares.map((share) => share.amountKopecks))).toBe(100n)
  })

  it('rotates multiple remainder kopecks from the payer', () => {
    expect(splitEqually('101', ['30', '10', '20'], '20')).toEqual([
      { participantId: '10', amountKopecks: '33' },
      { participantId: '20', amountKopecks: '34' },
      { participantId: '30', amountKopecks: '34' },
    ])
  })

  it('settles symmetric expenses without artificial kopeck debts', () => {
    const participantIds = ['1', '2', '3']
    const balances = new Map(participantIds.map((id) => [id, 0n]))

    for (const payerId of participantIds) {
      balances.set(payerId, balances.get(payerId)! + 100_000n)
      for (const share of splitEqually('100000', participantIds, payerId)) {
        balances.set(share.participantId, balances.get(share.participantId)! - BigInt(share.amountKopecks))
      }
    }

    expect([...balances.values()]).toEqual([0n, 0n, 0n])
  })

  it('uses the first stable participant when the payer is not included', () => {
    expect(splitEqually('100', ['30', '10', '20'], '40')).toEqual([
      { participantId: '10', amountKopecks: '34' },
      { participantId: '20', amountKopecks: '33' },
      { participantId: '30', amountKopecks: '33' },
    ])
  })

  it('does not create zero-value equal shares', () => {
    expect(splitEqually('2', ['1', '2', '3'], '1')).toEqual([])
  })
})
