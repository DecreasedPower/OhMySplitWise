import { getInitData } from '../platform/telegram'
import { expireSession } from './session'
import { isClientUpgradeRequired, requireClientUpgrade } from './upgrade'

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
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
  const bytes = new Uint8Array(16)
  if (typeof globalThis.crypto?.getRandomValues === 'function') globalThis.crypto.getRandomValues(bytes)
  else for (let index = 0; index < bytes.length; index++) bytes[index] = Math.floor(Math.random() * 256)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

type RequestOptions = Omit<RequestInit, 'body'> & { body?: unknown }

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method?.toUpperCase() ?? 'GET'
  if (isClientUpgradeRequired() && method !== 'GET' && method !== 'HEAD') {
    throw new ApiError(426, 'Требуется обновление приложения', {}, { code: 'client_upgrade_required' })
  }
  const headers = new Headers(options.headers)
  const initData = getInitData()
  if (initData) headers.set('Authorization', `tma ${initData}`)
  headers.set('Accept', 'application/json')
  headers.set('X-Client-Protocol', '2')
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
    const error = new ApiError(response.status, message || 'Ошибка запроса', problem, extensions)
    if (error.status === 426 && error.code === 'client_upgrade_required') requireClientUpgrade()
    throw error
  }
  return payload as T
}
