import { parseMoney, sumMoney } from './money'

export interface ExpenseDraft {
  description: string
  amount: string
  payerId: string
  participantIds: string[]
  manualAmounts: Record<string, string>
  splitMode: 'equal' | 'manual'
}

export function validateExpenseDraft(draft: ExpenseDraft): Record<string, string> {
  const errors: Record<string, string> = {}
  const amount = parseMoney(draft.amount)
  if (!draft.description.trim()) errors.description = 'Укажите название покупки'
  else if (draft.description.trim().length > 200) errors.description = 'Не более 200 символов'
  if (!amount) errors.amount = 'Введите сумму больше нуля, например 1250,50'
  if (!draft.payerId) errors.payerId = 'Выберите, кто оплатил'
  if (!draft.participantIds.length) errors.participants = 'Выберите хотя бы одного участника'
  else if (amount && draft.splitMode === 'equal' && BigInt(amount) < BigInt(draft.participantIds.length)) errors.shares = 'Сумма слишком мала: доля каждого должна быть не меньше одной копейки'
  if (amount && draft.splitMode === 'manual' && draft.participantIds.length) {
    const shares = draft.participantIds.map((id) => parseMoney(draft.manualAmounts[id] ?? '') ?? '0')
    if (shares.some((share) => share === '0')) errors.shares = 'Каждая доля должна быть больше нуля'
    else if (sumMoney(shares) !== BigInt(amount)) errors.shares = 'Сумма долей должна совпадать с суммой покупки'
  }
  return errors
}
