import type { QueryClient, QueryKey } from '@tanstack/react-query'
import { ApiError } from './client'

export function isConcurrencyConflict(error: unknown) {
  return error instanceof ApiError && (
    (error.status === 409 && error.code === 'stale_group_revision')
    || (error.status === 409 && error.code === 'stale_balance')
    || (error.status === 409 && error.code === 'stale_participants')
    || (error.status === 412 && error.code === 'entity_version_conflict')
  )
}

export async function refreshAfterConflict(error: unknown, client: QueryClient, queryKeys: QueryKey[]) {
  if (!isConcurrencyConflict(error)) return false
  await Promise.all(queryKeys.map((queryKey) => client.invalidateQueries({ queryKey })))
  return true
}
