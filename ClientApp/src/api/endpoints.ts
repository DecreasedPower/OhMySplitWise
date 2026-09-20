import { api } from './client'
import type {
  BalanceOverview, Expense, ExpenseInput, Group, GroupSummary, GroupType, Invitation, Participant, UserProfile,
} from '../domain/types'

type Version = number | string
export interface MutationMetadata {
  idempotencyKey: string
  version?: Version
  groupRevision?: Version
}

function mutationHeaders({ idempotencyKey, version, groupRevision }: MutationMetadata) {
  const headers = new Headers({ 'Idempotency-Key': idempotencyKey })
  if (version !== undefined) headers.set('If-Match', String(version))
  if (groupRevision !== undefined) headers.set('X-Group-Revision', String(groupRevision))
  return headers
}

export const endpoints = {
  profile: () => api<UserProfile>('/me'),
  updateProfile: (paymentDetails: string | null, metadata: MutationMetadata) => api<UserProfile>('/me', { method: 'PATCH', headers: mutationHeaders(metadata), body: { paymentDetails } }),
  groups: () => api<GroupSummary[]>('/groups'),
  group: (id: string) => api<Group>(`/groups/${id}`),
  createGroup: (name: string, type: GroupType, metadata: MutationMetadata) => api<Group>('/groups', { method: 'POST', headers: mutationHeaders(metadata), body: { name, type } }),
  deleteGroup: (id: string, metadata: MutationMetadata) => api<void>(`/groups/${id}`, { method: 'DELETE', headers: mutationHeaders(metadata) }),
  leaveGroup: (id: string, metadata: MutationMetadata) => api<void>(`/groups/${id}/membership`, { method: 'DELETE', headers: mutationHeaders(metadata) }),
  participants: (groupId: string) => api<Participant[]>(`/groups/${groupId}/participants`),
  addParticipant: (groupId: string, input: { displayName: string; paymentDetails: string | null }, metadata: MutationMetadata) =>
    api<Participant>(`/groups/${groupId}/participants`, { method: 'POST', headers: mutationHeaders(metadata), body: input }),
  updateParticipant: (groupId: string, id: string, input: { displayName: string; paymentDetails: string | null }, metadata: MutationMetadata) =>
    api<Participant>(`/groups/${groupId}/participants/${id}`, { method: 'PATCH', headers: mutationHeaders(metadata), body: input }),
  deleteParticipant: (groupId: string, id: string, metadata: MutationMetadata) => api<void>(`/groups/${groupId}/participants/${id}`, { method: 'DELETE', headers: mutationHeaders(metadata) }),
  expenses: (groupId: string) => api<Expense[]>(`/groups/${groupId}/expenses`),
  expense: (groupId: string, id: string) => api<Expense>(`/groups/${groupId}/expenses/${id}`),
  createExpense: (groupId: string, input: ExpenseInput, metadata: MutationMetadata) => api<Expense>(`/groups/${groupId}/expenses`, { method: 'POST', headers: mutationHeaders(metadata), body: input }),
  updateExpense: (groupId: string, id: string, input: ExpenseInput, metadata: MutationMetadata) => api<Expense>(`/groups/${groupId}/expenses/${id}`, { method: 'PUT', headers: mutationHeaders(metadata), body: input }),
  deleteExpense: (groupId: string, id: string, metadata: MutationMetadata) => api<void>(`/groups/${groupId}/expenses/${id}`, { method: 'DELETE', headers: mutationHeaders(metadata) }),
  balances: (groupId: string) => api<BalanceOverview>(`/groups/${groupId}/balances`),
  markPaid: (groupId: string, toParticipantId: string, metadata: MutationMetadata) =>
    api<void>(`/groups/${groupId}/transfers`, { method: 'POST', headers: mutationHeaders(metadata), body: { toParticipantId } }),
  resolveTransfer: (groupId: string, transferId: string, resolution: 'confirmed' | 'rejected', metadata: MutationMetadata) =>
    api<void>(`/groups/${groupId}/transfers/${transferId}`, { method: 'PATCH', headers: mutationHeaders(metadata), body: { status: resolution } }),
  invitations: (groupId: string) => api<Invitation[]>(`/groups/${groupId}/invitations`),
  createInvitation: (groupId: string, metadata: MutationMetadata) => api<Invitation>(`/groups/${groupId}/invitations`, { method: 'POST', headers: mutationHeaders(metadata) }),
  revokeInvitation: (groupId: string, invitationId: string, metadata: MutationMetadata) => api<void>(`/groups/${groupId}/invitations/${invitationId}`, { method: 'DELETE', headers: mutationHeaders(metadata) }),
}
