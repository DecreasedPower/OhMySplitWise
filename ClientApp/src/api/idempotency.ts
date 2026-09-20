import { useRef } from 'react'
import { ApiError, newIdempotencyKey } from './client'

function fingerprint(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(fingerprint).join(',')}]`
  if (value && typeof value === 'object') {
    return `{${Object.entries(value as Record<string, unknown>)
      .filter(([, item]) => item !== undefined)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, item]) => `${JSON.stringify(key)}:${fingerprint(item)}`)
      .join(',')}}`
  }
  return JSON.stringify(value)
}

export class IdempotencyKeyStore {
  private current?: { fingerprint: string; key: string; command: unknown }

  constructor(private readonly createKey: () => string = newIdempotencyKey) {}

  bind<T>(intent: unknown, command: T): IdempotentSubmission<T> {
    const nextFingerprint = fingerprint(intent)
    if (this.current?.fingerprint !== nextFingerprint) {
      this.current = { fingerprint: nextFingerprint, key: this.createKey(), command: structuredClone(command) }
    }
    return { command: this.current.command as T, key: this.current.key }
  }

  settle(key: string, error?: unknown) {
    if (this.current?.key !== key) return
    if (error === undefined || (error instanceof ApiError && error.status >= 400 && error.status < 500)) {
      this.current = undefined
    }
  }
}

export interface IdempotentSubmission<T> {
  command: T
  key: string
}

export function useIdempotencyKeyStore() {
  return useRef(new IdempotencyKeyStore()).current
}
