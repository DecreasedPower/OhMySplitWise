export type Id = string
export type MoneyString = string
export type GroupType = 'collective' | 'standalone'

export interface UserProfile {
  id: Id
  displayName: string
  username: string | null
  paymentDetails: string | null
  version: number | string
}

export interface GroupSummary {
  id: Id
  name: string
  type: GroupType
  ownerId: Id
  participantCount: number
  myBalanceKopecks: MoneyString
  totalExpensesKopecks: MoneyString
  revision: number | string
}

export interface Group extends GroupSummary {
  createdAt: string
  isOwner: boolean
}

export interface Participant {
  id: Id
  displayName: string
  telegramUserId: Id | null
  paymentDetails: string | null
  isCurrentUser: boolean
  canEdit: boolean
  version: number | string
}

export interface ExpenseShare {
  participantId: Id
  participantName: string
  amountKopecks: MoneyString
}

export interface Expense {
  id: Id
  groupId: Id
  description: string
  amountKopecks: MoneyString
  payerId: Id
  payerName: string
  authorId: Id
  canEdit: boolean
  createdAt: string
  shares?: ExpenseShare[] | null
  version: number | string
}

export interface Balance {
  participantId: Id
  participantName: string
  amountKopecks: MoneyString
}

export interface SuggestedTransfer {
  fromParticipantId: Id
  fromName: string
  toParticipantId: Id
  toName: string
  amountKopecks: MoneyString
  paymentDetails: string | null
  canMarkPaid: boolean
  pendingTransferId: Id | null
}

export interface PendingTransfer {
  id: Id
  fromName: string
  toName: string
  amountKopecks: MoneyString
  canResolve: boolean
  version: number | string
}

export interface BalanceOverview {
  balances: Balance[]
  suggestions: SuggestedTransfer[]
  pendingTransfers: PendingTransfer[]
  groupRevision: number | string
}

export interface BalanceDetailsExpense {
  id: Id
  description: string
  amountKopecks: MoneyString
  payerId: Id
  payerName: string
  createdAt: string
  shares: ExpenseShare[]
}

export interface BalanceDetailsTransfer {
  id: Id
  fromParticipantId: Id
  fromName: string
  toParticipantId: Id
  toName: string
  amountKopecks: MoneyString
  status: 'pending' | 'confirmed'
  createdAt: string
}

export interface BalanceDetailsSuggestion {
  fromParticipantId: Id
  fromName: string
  toParticipantId: Id
  toName: string
  amountKopecks: MoneyString
  isPending: boolean
}

export interface BalanceDetails {
  groupName: string
  generatedAt: string
  expenses: BalanceDetailsExpense[]
  transfers: BalanceDetailsTransfer[]
  balances: Balance[]
  suggestions: BalanceDetailsSuggestion[]
}

export interface Invitation {
  id: Id
  token: string
  shareUrl: string
  telegramShareUrl: string
  expiresAt: string
  version: number | string
  isActive: boolean
}

export interface ExpenseInput {
  description: string
  amountKopecks: MoneyString
  payerId: Id
  shares: Array<{ participantId: Id; amountKopecks: MoneyString }>
}
