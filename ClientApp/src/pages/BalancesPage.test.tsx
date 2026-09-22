import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { endpoints } from '../api/endpoints'
import type { BalanceDetails } from '../domain/types'
import { BalancesPage } from './BalancesPage'

vi.mock('../api/endpoints', () => ({
  endpoints: {
    balances: vi.fn(),
    balanceDetails: vi.fn(),
    markPaid: vi.fn(),
    resolveTransfer: vi.fn(),
  },
}))

const details: BalanceDetails = {
  groupName: 'Тест', generatedAt: '2026-09-22T01:15:00Z', expenses: [], transfers: [], balances: [], suggestions: [],
}

describe('BalancesPage details export', () => {
  const writeText = vi.fn()

  beforeEach(() => {
    vi.mocked(endpoints.balances).mockResolvedValue({ balances: [], suggestions: [], pendingTransfers: [], groupRevision: '1' })
    vi.mocked(endpoints.balanceDetails).mockResolvedValue(details)
    writeText.mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
  })

  afterEach(() => vi.clearAllMocks())

  it('loads and copies the current details from the header action', async () => {
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Скопировать детализацию' }))

    await waitFor(() => expect(endpoints.balanceDetails).toHaveBeenCalledWith('group-1'))
    expect(writeText).toHaveBeenCalledWith(expect.stringContaining('Группа: Тест'))
    expect(await screen.findByText('Детализация скопирована')).toBeInTheDocument()
  })

  it('shows an error when clipboard rejects the text', async () => {
    writeText.mockRejectedValueOnce(new DOMException('Denied'))
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Скопировать детализацию' }))

    expect(await screen.findByText('Не удалось скопировать детализацию')).toBeInTheDocument()
    expect(screen.queryByText('Детализация скопирована')).not.toBeInTheDocument()
  })
})

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/groups/group-1/balances']}><Routes><Route path="/groups/:groupId/balances" element={<BalancesPage />} /></Routes></MemoryRouter></QueryClientProvider>)
}
