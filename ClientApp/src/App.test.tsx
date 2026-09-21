import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { App } from './App'
import { requireClientUpgrade, resetClientUpgradeRequired } from './api/upgrade'

afterEach(resetClientUpgradeRequired)

describe('client upgrade screen', () => {
  it('replaces the application with instructions to reopen the Mini App', () => {
    requireClientUpgrade()

    render(<MemoryRouter><App /></MemoryRouter>)

    expect(screen.getByRole('heading', { name: 'Нужно обновить приложение' })).toBeInTheDocument()
    expect(screen.getByText(/Закройте мини-приложение и откройте его снова/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Закрыть приложение' })).toBeInTheDocument()
  })
})
