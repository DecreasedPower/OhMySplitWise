import { getInitData } from '../platform/telegram'
import { expireSession } from './session'

export class ApiError extends Error {
  public readonly code?: string

  constructor(
    public status: number,
    message: string,
    public readonly problem: Record<string, unknown> = {},
    public readonly extensions: Record<string, unknown> = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.code = typeof extensions.code === 'string' ? extensions.code : undefined
  }
}

export function newIdempotencyKey() {
  return crypto.randomUUID()
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
  const text = await response.text()
  let payload: unknown = text || undefined
  if (contentType.includes('json')) {
    try {
      payload = text ? JSON.parse(text) : undefined
    } catch {
      if (response.ok) throw new ApiError(response.status, 'Некорректный ответ сервера', { raw: text })
    }
  }
  if (!response.ok) {
    const problem = typeof payload === 'object' && payload !== null ? payload as Record<string, unknown> : {}
    const detail = typeof problem.detail === 'string' ? problem.detail : undefined
    const title = typeof problem.title === 'string' ? problem.title : undefined
    const message = detail ?? title ?? (typeof payload === 'string' ? payload : '')
    const standardFields = new Set(['type', 'title', 'status', 'detail', 'instance', 'extensions'])
    const nestedExtensions = typeof problem.extensions === 'object' && problem.extensions !== null ? problem.extensions as Record<string, unknown> : {}
    const extensions = { ...nestedExtensions, ...Object.fromEntries(Object.entries(problem).filter(([key]) => !standardFields.has(key))) }
    if (response.status === 401) expireSession()
    throw new ApiError(response.status, message || 'Ошибка запроса', problem, extensions)
  }
  return payload as T
}
