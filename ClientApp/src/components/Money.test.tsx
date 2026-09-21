import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Money } from './Money'

describe('Money', () => {
  it('preserves an explicit color for a positive signed balance', () => {
    render(<Money value="100" signed color="inherit" />)

    expect(screen.getByText('+1,00 ₽')).toHaveStyle({ color: 'inherit' })
  })
})
