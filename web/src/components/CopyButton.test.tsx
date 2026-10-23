import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CopyButton } from './CopyButton'

describe('CopyButton component', () => {
  beforeEach(() => {
    Object.assign(navigator, {
      clipboard: {
        writeText: vi.fn().mockResolvedValue(undefined),
      },
    })
  })

  it('renders with label and copies to clipboard on click', async () => {
    render(<CopyButton text="https://wazap.ci/test" label="Copier le lien" />)

    const btn = screen.getByRole('button', { name: /Copier le lien/i })
    expect(btn).toBeInTheDocument()

    await userEvent.click(btn)

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('https://wazap.ci/test')
    expect(await screen.findByText('Copié !')).toBeInTheDocument()
  })
})
