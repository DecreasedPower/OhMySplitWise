import { api } from './client'
import type {
  BalanceOverview, Expense, ExpenseInput, Group, GroupSummary, GroupType, Invitation, Participant, UserProfile,
} from '../domain/types'

type Version = number | string
type Idempotent = { idempotencyKey: string }
type Versioned = { version: Version }
type GroupVersioned = { groupRevision: Version }

export type UpdateProfileMetadata = Idempotent & Versioned
export type CreateGroupMetadata = Idempotent
export type DeleteGroupMetadata = Idempotent & GroupVersioned
export type LeaveGroupMetadata = Idempotent & GroupVersioned
export type AddParticipantMetadata = Idempotent & GroupVersioned
export type UpdateParticipantMetadata = Idempotent & Versioned & GroupVersioned
export type DeleteParticipantMetadata = Idempotent & Versioned & GroupVersioned
export type CreateExpenseMetadata = Idempotent & GroupVersioned
export type UpdateExpenseMetadata = Idempotent & Versioned & GroupVersioned
export type DeleteExpenseMetadata = Idempotent & Versioned & GroupVersioned
export type MarkPaidMetadata = Idempotent & GroupVersioned
export type ResolveTransferMetadata = Idempotent & Versioned & GroupVersioned
export type CreateInvitationMetadata = Idempotent & GroupVersioned
export type RevokeInvitationMetadata = Idempotent & Versioned & GroupVersioned

function requiredHeader(headers: Headers, name: string, value: unknown) {
  if (value === undefined || value === null || value === '') throw new TypeError(`${name} is required`)
  headers.set(name, String(value))
}

function mutationHeaders(metadata: Idempotent & Partial<Versioned & GroupVersioned>, required: Array<'version' | 'groupRevision'> = []) {
  const headers = new Headers()
  requiredHeader(headers, 'Idempotency-Key', metadata.idempotencyKey)
  if (required.includes('version')) requiredHeader(headers, 'If-Match', metadata.version)
  if (required.includes('groupRevision')) requiredHeader(headers, 'X-Group-Revision', metadata.groupRevision)
  return headers
}

export const endpoints = {
  profile: () => api<UserProfile>('/me'),
  updateProfile: (paymentDetails: string | null, metadata: UpdateProfileMetadata) => api<UserProfile>('/me', { method: 'PATCH', headers: mutationHeaders(metadata, ['version']), body: { paymentDetails } }),
  groups: () => api<GroupSummary[]>('/groups'),
  group: (id: string) => api<Group>(`/groups/${id}`),
  createGroup: (name: string, type: GroupType, metadata: CreateGroupMetadata) => api<Group>('/groups', { method: 'POST', headers: mutationHeaders(metadata), body: { name, type } }),
  deleteGroup: (id: string, metadata: DeleteGroupMetadata) => api<void>(`/groups/${id}`, { method: 'DELETE', headers: mutationHeaders(metadata, ['groupRevision']) }),
  leaveGroup: (id: string, metadata: LeaveGroupMetadata) => api<void>(`/groups/${id}/membership`, { method: 'DELETE', headers: mutationHeaders(metadata, ['groupRevision']) }),
  participants: (groupId: string) => api<Participant[]>(`/groups/${groupId}/participants`),
  addParticipant: (groupId: string, input: { displayName: string; paymentDetails: string | null }, metadata: AddParticipantMetadata) =>
    api<Participant>(`/groups/${groupId}/participants`, { method: 'POST', headers: mutationHeaders(metadata, ['groupRevision']), body: input }),
  updateParticipant: (groupId: string, id: string, input: { displayName: string; paymentDetails: string | null }, metadata: UpdateParticipantMetadata) =>
    api<Participant>(`/groups/${groupId}/participants/${id}`, { method: 'PATCH', headers: mutationHeaders(metadata, ['version', 'groupRevision']), body: input }),
  deleteParticipant: (groupId: string, id: string, metadata: DeleteParticipantMetadata) => api<void>(`/groups/${groupId}/participants/${id}`, { method: 'DELETE', headers: mutationHeaders(metadata, ['version', 'groupRevision']) }),
  expenses: (groupId: string) => api<Expense[]>(`/groups/${groupId}/expenses`),
  expense: (groupId: string, id: string) => api<Expense>(`/groups/${groupId}/expenses/${id}`),
  createExpense: (groupId: string, input: ExpenseInput, metadata: CreateExpenseMetadata) => api<Expense>(`/groups/${groupId}/expenses`, { method: 'POST', headers: mutationHeaders(metadata, ['groupRevision']), body: input }),
  updateExpense: (groupId: string, id: string, input: ExpenseInput, metadata: UpdateExpenseMetadata) => api<Expense>(`/groups/${groupId}/expenses/${id}`, { method: 'PUT', headers: mutationHeaders(metadata, ['version', 'groupRevision']), body: input }),
  deleteExpense: (groupId: string, id: string, metadata: DeleteExpenseMetadata) => api<void>(`/groups/${groupId}/expenses/${id}`, { method: 'DELETE', headers: mutationHeaders(metadata, ['version', 'groupRevision']) }),
  balances: (groupId: string) => api<BalanceOverview>(`/groups/${groupId}/balances`),
  markPaid: (groupId: string, toParticipantId: string, metadata: MarkPaidMetadata) =>
    api<void>(`/groups/${groupId}/transfers`, { method: 'POST', headers: mutationHeaders(metadata, ['groupRevision']), body: { toParticipantId } }),
  resolveTransfer: (groupId: string, transferId: string, resolution: 'confirmed' | 'rejected', metadata: ResolveTransferMetadata) =>
    api<void>(`/groups/${groupId}/transfers/${transferId}`, { method: 'PATCH', headers: mutationHeaders(metadata, ['version', 'groupRevision']), body: { status: resolution } }),
  invitations: (groupId: string) => api<Invitation[]>(`/groups/${groupId}/invitations`),
  createInvitation: (groupId: string, metadata: CreateInvitationMetadata) => api<Invitation>(`/groups/${groupId}/invitations`, { method: 'POST', headers: mutationHeaders(metadata, ['groupRevision']) }),
  revokeInvitation: (groupId: string, invitationId: string, metadata: RevokeInvitationMetadata) => api<void>(`/groups/${groupId}/invitations/${invitationId}`, { method: 'DELETE', headers: mutationHeaders(metadata, ['version', 'groupRevision']) }),
}
