export type Id = string
export type MoneyString = string
export type GroupType = 'collective' | 'standalone'

export interface UserProfile {
  id: Id
  displayName: string
  username: string | null
  paymentDetails: string | null
}

export interface GroupSummary {
  id: Id
  name: string
  type: GroupType
  ownerId: Id
  participantCount: number
  myBalanceKopecks: MoneyString
  totalExpensesKopecks: MoneyString
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
}

export interface BalanceOverview {
  balances: Balance[]
  suggestions: SuggestedTransfer[]
  pendingTransfers: PendingTransfer[]
}

export interface Invitation {
  token: string
  shareUrl: string
  telegramShareUrl: string
}

export interface ExpenseInput {
  description: string
  amountKopecks: MoneyString
  payerId: Id
  shares: Array<{ participantId: Id; amountKopecks: MoneyString }>
}
