import { api } from './client'
import type {
  BalanceOverview, Expense, ExpenseInput, Group, GroupSummary, GroupType, Invitation, Participant, UserProfile,
} from '../domain/types'

export const endpoints = {
  profile: () => api<UserProfile>('/me'),
  updateProfile: (paymentDetails: string | null) => api<UserProfile>('/me', { method: 'PATCH', body: { paymentDetails } }),
  groups: () => api<GroupSummary[]>('/groups'),
  group: (id: string) => api<Group>(`/groups/${id}`),
  createGroup: (name: string, type: GroupType) => api<Group>('/groups', { method: 'POST', body: { name, type } }),
  deleteGroup: (id: string) => api<void>(`/groups/${id}`, { method: 'DELETE' }),
  leaveGroup: (id: string) => api<void>(`/groups/${id}/membership`, { method: 'DELETE' }),
  participants: (groupId: string) => api<Participant[]>(`/groups/${groupId}/participants`),
  addParticipant: (groupId: string, input: { displayName: string; paymentDetails: string | null }) =>
    api<Participant>(`/groups/${groupId}/participants`, { method: 'POST', body: input }),
  updateParticipant: (groupId: string, id: string, input: { displayName: string; paymentDetails: string | null }) =>
    api<Participant>(`/groups/${groupId}/participants/${id}`, { method: 'PATCH', body: input }),
  deleteParticipant: (groupId: string, id: string) => api<void>(`/groups/${groupId}/participants/${id}`, { method: 'DELETE' }),
  expenses: (groupId: string) => api<Expense[]>(`/groups/${groupId}/expenses`),
  expense: (groupId: string, id: string) => api<Expense>(`/groups/${groupId}/expenses/${id}`),
  createExpense: (groupId: string, input: ExpenseInput) => api<Expense>(`/groups/${groupId}/expenses`, { method: 'POST', body: input }),
  updateExpense: (groupId: string, id: string, input: ExpenseInput) => api<Expense>(`/groups/${groupId}/expenses/${id}`, { method: 'PUT', body: input }),
  deleteExpense: (groupId: string, id: string) => api<void>(`/groups/${groupId}/expenses/${id}`, { method: 'DELETE' }),
  balances: (groupId: string) => api<BalanceOverview>(`/groups/${groupId}/balances`),
  markPaid: (groupId: string, toParticipantId: string) =>
    api<void>(`/groups/${groupId}/transfers`, { method: 'POST', body: { toParticipantId } }),
  resolveTransfer: (groupId: string, transferId: string, resolution: 'confirmed' | 'rejected') =>
    api<void>(`/groups/${groupId}/transfers/${transferId}`, { method: 'PATCH', body: { status: resolution } }),
  invitation: (groupId: string) => api<Invitation>(`/groups/${groupId}/invitations`, { method: 'POST' }),
}
