import { formatMoney } from './money'
import type { BalanceDetails } from './types'

const DATE = new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeZone: 'Europe/Moscow' })
const DATE_TIME = new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Europe/Moscow' })

function signedMoney(value: string) {
  return BigInt(value) > 0n ? `+${formatMoney(value)}` : formatMoney(value)
}

export function formatBalanceDetails(details: BalanceDetails): string {
  const lines = [
    'ДЕТАЛИЗАЦИЯ БАЛАНСА',
    `Группа: ${details.groupName}`,
    `Сформировано: ${DATE_TIME.format(new Date(details.generatedAt))} МСК`,
    '',
    'ПОКУПКИ',
    '',
  ]

  if (details.expenses.length === 0) lines.push('Покупок нет')
  for (const expense of details.expenses) {
    lines.push(`${DATE.format(new Date(expense.createdAt))} · ${expense.description} — ${formatMoney(expense.amountKopecks)}`)
    lines.push(`Заплатил: ${expense.payerName}`)
    lines.push('Доли:')
    lines.push(...expense.shares.map((share) => `${share.participantName} — ${formatMoney(share.amountKopecks)}`))
    lines.push('')
  }

  lines.push('ПЕРЕВОДЫ', '')
  if (details.transfers.length === 0) lines.push('Переводов нет')
  for (const transfer of details.transfers) {
    lines.push(`${DATE.format(new Date(transfer.createdAt))} · ${transfer.fromName} → ${transfer.toName} — ${formatMoney(transfer.amountKopecks)}`)
    lines.push(`Статус: ${transfer.status === 'confirmed' ? 'подтверждён' : 'ожидает подтверждения'}`, '')
  }

  lines.push('ТЕКУЩИЙ БАЛАНС', '')
  if (details.balances.length === 0) lines.push('Нет участников')
  lines.push(...details.balances.map((balance) => `${balance.participantName}: ${signedMoney(balance.amountKopecks)}`))

  lines.push('', 'КТО КОМУ ДОЛЖЕН', '')
  if (details.suggestions.length === 0) lines.push('Все расчёты закрыты')
  lines.push(...details.suggestions.map((suggestion) =>
    `${suggestion.fromName} → ${suggestion.toName} — ${formatMoney(suggestion.amountKopecks)}${suggestion.isPending ? ' · ожидает подтверждения' : ''}`))

  return lines.join('\n').trimEnd()
}
