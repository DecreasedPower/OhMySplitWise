import { describe, expect, it } from 'vitest'
import { validateExpenseDraft, type ExpenseDraft } from './forms'

const valid: ExpenseDraft = {
  description: 'Продукты', amount: '100,00', payerId: '9223372036854775807',
  participantIds: ['-1', '9223372036854775807'], splitMode: 'manual',
  manualAmounts: { '-1': '40', '9223372036854775807': '60' },
}

describe('expense form validation', () => {
  it('accepts string Int64 IDs and exact shares', () => expect(validateExpenseDraft(valid)).toEqual({}))
  it('detects a one-kopeck mismatch', () => {
    const draft = { ...valid, manualAmounts: { '-1': '40', '9223372036854775807': '59,99' } }
    expect(validateExpenseDraft(draft).shares).toMatch(/совпадать/)
  })
  it('rejects equal splits that would contain a zero share', () => {
    const draft = { ...valid, amount: '0,01', splitMode: 'equal' as const }
    expect(validateExpenseDraft(draft).shares).toMatch(/копейки/)
  })
})
