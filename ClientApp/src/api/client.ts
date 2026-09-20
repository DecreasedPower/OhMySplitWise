import { getInitData } from '../platform/telegram'
import { expireSession } from './session'

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string) {
    super(message)
  }
}

type RequestOptions = Omit<RequestInit, 'body'> & { body?: unknown }

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers = new Headers(options.headers)
  const initData = getInitData()
  if (initData) headers.set('Authorization', `tma ${initData}`)
  headers.set('Accept', 'application/json')
  if (options.body !== undefined) headers.set('Content-Type', 'application/json')

  const response = await fetch(`/api${path}`, {
    ...options,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })
  if (response.status === 204) return undefined as T
  const contentType = response.headers.get('content-type') ?? ''
  const payload: unknown = contentType.includes('application/json') ? await response.json() : await response.text()
  if (!response.ok) {
    const problem = payload as { detail?: string; title?: string; code?: string }
    const message = problem.detail ?? problem.title ?? String(payload)
    if (response.status === 401) expireSession()
    throw new ApiError(response.status, message || 'Ошибка запроса', problem.code)
  }
  return payload as T
}
