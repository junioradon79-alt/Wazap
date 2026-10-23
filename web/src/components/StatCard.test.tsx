import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatCard, StatsGrid } from './StatCard'

describe('StatCard component', () => {
  it('renders label, value, hint and icon', () => {
    render(
      <StatCard
        icon="🛵"
        label="Livreurs actifs"
        value={42}
        hint="Assignés à une course"
        variant="blue"
      />
    )

    expect(screen.getByText('Livreurs actifs')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
    expect(screen.getByText('Assignés à une course')).toBeInTheDocument()
    expect(screen.getByText('🛵')).toBeInTheDocument()
  })

  it('renders trend badge when provided', () => {
    render(
      <StatCard
        label="Chiffre d'affaires"
        value="150 000 FCFA"
        trend={{ value: 12 }}
      />
    )

    expect(screen.getByText('↑ 12%')).toBeInTheDocument()
  })

  it('renders within StatsGrid container', () => {
    const { container } = render(
      <StatsGrid className="test-grid">
        <StatCard label="Test" value={1} />
      </StatsGrid>
    )

    expect(container.querySelector('.stats.test-grid')).toBeInTheDocument()
  })
})
