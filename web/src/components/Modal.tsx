import { useEffect, useRef, type ReactNode } from 'react'

interface Props {
  title: string
  onClose: () => void
  children: ReactNode
  labelledBy?: string
}

/**
 * Modale accessible réutilisable.
 *
 * Les modales de l'application étaient de simples `<div>` : ni rôle, ni `aria-modal`, fermeture
 * uniquement au clic sur le fond (impossible au clavier), focus qui restait derrière la modale
 * et n'était pas restitué à la fermeture. Ici :
 *  - `role="dialog"` + `aria-modal` + titre référencé par `aria-labelledby` ;
 *  - fermeture par Échap ;
 *  - focus déplacé dans la modale à l'ouverture, restitué à l'élément déclencheur à la fermeture ;
 *  - le clic sur le fond ferme, le clic dans la modale ne ferme pas.
 */
export function Modal({ title, onClose, children, labelledBy = 'modal-title' }: Props) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const previouslyFocused = useRef<Element | null>(null)

  useEffect(() => {
    previouslyFocused.current = document.activeElement

    const onKeyDown = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onClose()
      }
    }
    document.addEventListener('keydown', onKeyDown)

    // Focus initial : le premier champ s'il existe, sinon la modale elle-même.
    const firstField = dialogRef.current?.querySelector<HTMLElement>(
      'input, select, textarea, button',
    )
    firstField?.focus()

    return () => {
      document.removeEventListener('keydown', onKeyDown)
      // Le focus revient au déclencheur : sans cela, l'utilisateur clavier repartait du début.
      if (previouslyFocused.current instanceof HTMLElement) previouslyFocused.current.focus()
    }
  }, [onClose])

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        ref={dialogRef}
        onClick={(e) => e.stopPropagation()}
      >
        <h3 id={labelledBy}>{title}</h3>
        {children}
      </div>
    </div>
  )
}
