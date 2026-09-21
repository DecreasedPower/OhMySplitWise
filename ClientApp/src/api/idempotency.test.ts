import { describe, expect, it, vi } from 'vitest'
import { ApiError } from './client'
import { IdempotencyKeyStore } from './idempotency'

describe('IdempotencyKeyStore', () => {
  it('reuses the original command snapshot when only concurrency metadata changes', () => {
    const createKey = vi.fn().mockReturnValueOnce('key-1').mockReturnValueOnce('key-2')
    const store = new IdempotencyKeyStore(createKey)

    const first = store.bind({ amount: '100' }, { amount: '100', version: '2', revision: '5' })
    const retry = store.bind({ amount: '100' }, { amount: '100', version: '3', revision: '6' })

    expect(retry).toEqual(first)
    expect(retry).toEqual({ key: 'key-1', command: { amount: '100', version: '2', revision: '5' } })
  })

  it('creates a new command snapshot and key when business intent changes', () => {
    const createKey = vi.fn().mockReturnValueOnce('key-1').mockReturnValueOnce('key-2')
    const store = new IdempotencyKeyStore(createKey)

    store.bind({ amount: '100' }, { amount: '100', revision: '5' })
    const changed = store.bind({ amount: '200' }, { amount: '200', revision: '6' })

    expect(changed).toEqual({ key: 'key-2', command: { amount: '200', revision: '6' } })
  })

  it('retains ambiguous failures and clears successful or definitive outcomes', () => {
    const createKey = vi.fn().mockReturnValueOnce('key-1').mockReturnValueOnce('key-2').mockReturnValueOnce('key-3')
    const store = new IdempotencyKeyStore(createKey)
    const intent = { amount: '100' }
    const command = { amount: '100', revision: '5' }

    const first = store.bind(intent, command)
    store.settle(first.key, new TypeError('network error'))
    expect(store.bind(intent, { ...command, revision: '6' })).toEqual(first)
    store.settle(first.key, new ApiError(503, 'Unavailable'))
    expect(store.bind(intent, { ...command, revision: '7' })).toEqual(first)
    store.settle(first.key, new ApiError(409, 'Conflict'))
    expect(store.bind(intent, command).key).toBe('key-2')
    store.settle('key-2')
    expect(store.bind(intent, command).key).toBe('key-3')
  })

  it('snapshots mutable command values', () => {
    const store = new IdempotencyKeyStore(() => 'key-1')
    const command = { shares: [{ participantId: '1', amount: '100' }], revision: '5' }
    const submission = store.bind({ shares: command.shares }, command)

    command.shares[0].amount = '200'

    expect(submission.command.shares[0].amount).toBe('100')
  })

  it('keeps snapshot semantics when structuredClone is unavailable', () => {
    vi.stubGlobal('structuredClone', undefined)
    const store = new IdempotencyKeyStore(() => 'key-1')
    const command = { shares: [{ participantId: '1', amount: '100' }], optional: undefined }

    const submission = store.bind({ shares: command.shares }, command)
    command.shares[0].amount = '200'

    expect(submission.command).toEqual({ shares: [{ participantId: '1', amount: '100' }], optional: undefined })
    vi.unstubAllGlobals()
  })
})
