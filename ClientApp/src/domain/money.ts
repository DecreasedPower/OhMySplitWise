import type { MoneyString } from './types'

const MONEY_INPUT = /^\s*(\d+)(?:[.,](\d{0,2}))?\s*$/
const INTEGER = /^-?\d+$/
const INT64_MAX = 9223372036854775807n

export function parseMoney(value: string): MoneyString | null {
  const match = MONEY_INPUT.exec(value)
  if (!match) return null
  const rubles = match[1].replace(/^0+(?=\d)/, '')
  const kopecks = (match[2] ?? '').padEnd(2, '0')
  const result = BigInt(rubles) * 100n + BigInt(kopecks || '0')
  return result > 0n && result <= INT64_MAX ? result.toString() : null
}

export function formatMoney(value: MoneyString, currency = true): string {
  if (!INTEGER.test(value)) return '—'
  const amount = BigInt(value)
  const sign = amount < 0n ? '−' : ''
  const absolute = amount < 0n ? -amount : amount
  const rubles = new Intl.NumberFormat('ru-RU').format(absolute / 100n)
  const kopecks = (absolute % 100n).toString().padStart(2, '0')
  return `${sign}${rubles},${kopecks}${currency ? ' ₽' : ''}`
}

export function splitEqually(amount: MoneyString, participantIds: string[]) {
  if (!INTEGER.test(amount) || BigInt(amount) < BigInt(participantIds.length) || participantIds.length === 0) return []
  const total = BigInt(amount)
  const count = BigInt(participantIds.length)
  const base = total / count
  const remainder = total % count
  return participantIds.map((participantId, index) => ({
    participantId,
    amountKopecks: (base + (BigInt(index) < remainder ? 1n : 0n)).toString(),
  }))
}

export function sumMoney(values: MoneyString[]): bigint {
  return values.reduce((sum, value) => sum + (INTEGER.test(value) ? BigInt(value) : 0n), 0n)
}

export function kopecksToInput(value: MoneyString): string {
  return formatMoney(value, false).replace(/\s/g, '')
}
