import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FilterTabs } from './FilterTabs'

describe('FilterTabs component', () => {
  const options = [
    { id: 'all', label: 'Toutes', count: 12 },
    { id: 'active', label: 'En cours', count: 5 },
    { id: 'delivered', label: 'Livrées', count: 7 },
  ]

  it('renders all options with labels and counts', () => {
    render(<FilterTabs options={options} value="all" onChange={vi.fn()} />)

    expect(screen.getByText('Toutes')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('En cours')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('calls onChange when clicking a tab', async () => {
    const onChange = vi.fn()
    render(<FilterTabs options={options} value="all" onChange={onChange} />)

    const activeTab = screen.getByRole('tab', { name: /En cours/i })
    await userEvent.click(activeTab)

    expect(onChange).toHaveBeenCalledWith('active')
  })
})
