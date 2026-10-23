import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Pagination } from './Pagination'

describe('Pagination component', () => {
  it('does not render when totalPages is 1', () => {
    const { container } = render(
      <Pagination currentPage={1} totalPages={1} onPageChange={vi.fn()} />
    )
    expect(container.firstChild).toBeNull()
  })

  it('renders page info and navigation buttons', async () => {
    const onPageChange = vi.fn()
    render(
      <Pagination currentPage={2} totalPages={5} onPageChange={onPageChange} />
    )

    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()

    const prevBtn = screen.getByRole('button', { name: /Page précédente/i })
    const nextBtn = screen.getByRole('button', { name: /Page suivante/i })

    expect(prevBtn).not.toBeDisabled()
    expect(nextBtn).not.toBeDisabled()

    await userEvent.click(prevBtn)
    expect(onPageChange).toHaveBeenCalledWith(1)

    await userEvent.click(nextBtn)
    expect(onPageChange).toHaveBeenCalledWith(3)
  })

  it('disables previous button on first page and next button on last page', () => {
    const { rerender } = render(
      <Pagination currentPage={1} totalPages={3} onPageChange={vi.fn()} />
    )
    expect(screen.getByRole('button', { name: /Page précédente/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /Page suivante/i })).not.toBeDisabled()

    rerender(
      <Pagination currentPage={3} totalPages={3} onPageChange={vi.fn()} />
    )
    expect(screen.getByRole('button', { name: /Page précédente/i })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: /Page suivante/i })).toBeDisabled()
  })
})
