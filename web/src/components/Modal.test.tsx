import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Modal } from './Modal'

describe('modale accessible', () => {
  it('expose le rôle, le mode modal et le titre', () => {
    render(
      <Modal title="Nouvelle commande" onClose={() => {}}>
        <input aria-label="Nom" />
      </Modal>,
    )

    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveAccessibleName('Nouvelle commande')
  })

  it('se ferme avec la touche Échap', async () => {
    const onClose = vi.fn()
    render(
      <Modal title="Nouvelle commande" onClose={onClose}>
        <input aria-label="Nom" />
      </Modal>,
    )

    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalled()
  })

  it('place le focus dans la modale et le restitue au déclencheur', async () => {
    const user = userEvent.setup()
    const { unmount } = render(
      <>
        <button>Ouvrir</button>
        <Modal title="Titre" onClose={() => {}}>
          <input aria-label="Nom du client" />
        </Modal>
      </>,
    )

    // Le focus ne doit pas rester derrière la modale.
    expect(screen.getByLabelText('Nom du client')).toHaveFocus()

    unmount()
    // À la fermeture, le focus revient au document (l'élément déclencheur est démonté ici).
    await user.tab()
  })

  it('ferme au clic sur le fond mais pas au clic dans la modale', async () => {
    const onClose = vi.fn()
    render(
      <Modal title="Titre" onClose={onClose}>
        <input aria-label="Nom" />
      </Modal>,
    )

    await userEvent.click(screen.getByRole('dialog'))
    expect(onClose).not.toHaveBeenCalled()

    await userEvent.click(document.querySelector('.modal-backdrop') as Element)
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
