import { describe, expect, it } from 'vitest'
import { formatBalanceDetails } from './balanceDetails'
import type { BalanceDetails } from './types'

describe('formatBalanceDetails', () => {
  it('formats purchases, transfers, balances and suggestions without losing kopecks', () => {
    const nbsp = '\u00a0'
    const details: BalanceDetails = {
      groupName: 'Поездка в Казань',
      generatedAt: '2026-09-22T01:15:00Z',
      expenses: [{
        id: 'expense-1', description: 'Ужин', amountKopecks: '360000', payerId: '10', payerName: 'Анна', createdAt: '2026-09-20T09:00:00Z',
        shares: [
          { participantId: '10', participantName: 'Анна', amountKopecks: '120000' },
          { participantId: '20', participantName: 'Борис', amountKopecks: '120000' },
          { participantId: '-1', participantName: 'Мария', amountKopecks: '120000' },
        ],
      }],
      transfers: [
        { id: 'transfer-1', fromParticipantId: '20', fromName: 'Борис', toParticipantId: '10', toName: 'Анна', amountKopecks: '50001', status: 'confirmed', createdAt: '2026-09-21T09:00:00Z' },
        { id: 'transfer-2', fromParticipantId: '-1', fromName: 'Мария', toParticipantId: '10', toName: 'Анна', amountKopecks: '80000', status: 'pending', createdAt: '2026-09-22T00:00:00Z' },
      ],
      balances: [
        { participantId: '10', participantName: 'Анна', amountKopecks: '230001' },
        { participantId: '20', participantName: 'Борис', amountKopecks: '-70001' },
        { participantId: '-1', participantName: 'Мария', amountKopecks: '-160000' },
      ],
      suggestions: [
        { fromParticipantId: '20', fromName: 'Борис', toParticipantId: '10', toName: 'Анна', amountKopecks: '70001', isPending: false },
        { fromParticipantId: '-1', fromName: 'Мария', toParticipantId: '10', toName: 'Анна', amountKopecks: '160000', isPending: true },
      ],
    }

    expect(formatBalanceDetails(details)).toBe(`ДЕТАЛИЗАЦИЯ БАЛАНСА
Группа: Поездка в Казань
Сформировано: 22.09.2026, 04:15 МСК

ПОКУПКИ

20.09.2026 · Ужин — 3${nbsp}600,00 ₽
Заплатил: Анна
Доли:
Анна — 1${nbsp}200,00 ₽
Борис — 1${nbsp}200,00 ₽
Мария — 1${nbsp}200,00 ₽

ПЕРЕВОДЫ

21.09.2026 · Борис → Анна — 500,01 ₽
Статус: подтверждён

22.09.2026 · Мария → Анна — 800,00 ₽
Статус: ожидает подтверждения

ТЕКУЩИЙ БАЛАНС

Анна: +2${nbsp}300,01 ₽
Борис: −700,01 ₽
Мария: −1${nbsp}600,00 ₽

КТО КОМУ ДОЛЖЕН

Борис → Анна — 700,01 ₽
Мария → Анна — 1${nbsp}600,00 ₽ · ожидает подтверждения`)
  })

  it('describes an empty settled group', () => {
    expect(formatBalanceDetails({ groupName: 'Пустая', generatedAt: '2026-09-22T01:15:00Z', expenses: [], transfers: [], balances: [], suggestions: [] }))
      .toContain('Покупок нет\nПЕРЕВОДЫ\n\nПереводов нет')
    expect(formatBalanceDetails({ groupName: 'Пустая', generatedAt: '2026-09-22T01:15:00Z', expenses: [], transfers: [], balances: [], suggestions: [] }))
      .toContain('Все расчёты закрыты')
  })
})
