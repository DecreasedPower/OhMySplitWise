import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from './client'
import { endpoints } from './endpoints'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('api errors', () => {
  it('retains structured problem details and extensions', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      title: 'Conflict',
      detail: 'The entity changed',
      status: 409,
      code: 'version_conflict',
      extensions: { currentVersion: '8', conflicts: [{ field: 'name' }] },
    }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } })))

    const error = await api('/resource').catch((reason: unknown) => reason)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({
      status: 409,
      message: 'The entity changed',
      code: 'version_conflict',
      extensions: { code: 'version_conflict', currentVersion: '8', conflicts: [{ field: 'name' }] },
    })
    expect((error as ApiError).problem).toMatchObject({ title: 'Conflict', status: 409 })
  })

  it('accepts an empty JSON response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))

    await expect(api('/resource')).resolves.toBeUndefined()
  })

  it('wraps malformed successful JSON in ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{broken', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))

    await expect(api('/resource')).rejects.toMatchObject({ status: 200, message: 'Некорректный ответ сервера' })
  })

  it('preserves malformed error JSON as the error message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{broken', {
      status: 502,
      headers: { 'Content-Type': 'application/problem+json' },
    })))

    await expect(api('/resource')).rejects.toMatchObject({ status: 502, message: '{broken' })
  })

  it('uses the default message for an empty JSON error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', {
      status: 500,
      headers: { 'Content-Type': 'application/problem+json' },
    })))

    await expect(api('/resource')).rejects.toMatchObject({ status: 500, message: 'Ошибка запроса' })
  })
})

describe('mutation headers', () => {
  it('sends idempotency, entity version, and group revision metadata', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'participant-1' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await endpoints.updateParticipant('group-1', 'participant-1', { displayName: 'Alex', paymentDetails: null }, {
      idempotencyKey: 'submission-1',
      version: '12',
      groupRevision: 34,
    })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    const headers = new Headers(init.headers)
    expect(url).toBe('/api/groups/group-1/participants/participant-1')
    expect(headers.get('Idempotency-Key')).toBe('submission-1')
    expect(headers.get('If-Match')).toBe('12')
    expect(headers.get('X-Group-Revision')).toBe('34')
  })

  it('loads invitations with GET instead of creating one', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('[]', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await endpoints.invitations('group-1')

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBeUndefined()
    expect(new Headers(init.headers).get('Idempotency-Key')).toBeNull()
  })
})
