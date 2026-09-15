import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  error: Error | null
}

/**
 * Filet de sécurité global.
 *
 * Sans lui, une seule valeur inattendue renvoyée par le serveur (par exemple un statut
 * d'énumération inconnu indexé dans une table de libellés) faisait lever une TypeError pendant
 * le rendu : React démontait alors TOUT l'arbre et l'utilisateur voyait un écran blanc, sans
 * message ni moyen de repartir. Ici l'erreur reste confinée, elle est affichée et l'utilisateur
 * peut recharger la page.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // Journalisé côté navigateur uniquement : le rapport détaillé part au serveur via les logs
    // de l'API (aucune donnée personnelle n'est envoyée ici).
    console.error('Erreur de rendu non gérée :', error, info.componentStack)
  }

  render(): ReactNode {
    const { error } = this.state
    if (!error) return this.props.children

    return (
      <div className="app-shell" style={{ display: 'block', padding: '2rem' }}>
        <div className="card" style={{ maxWidth: 680, margin: '4rem auto' }}>
          <h1 className="topbar__title">Une erreur est survenue</h1>
          <p className="topbar__subtitle">
            L’affichage a été interrompu. Vos données ne sont pas affectées — rechargez la page
            pour reprendre.
          </p>
          <p style={{ fontFamily: 'monospace', fontSize: 12, opacity: 0.7, wordBreak: 'break-word' }}>
            {error.message}
          </p>
          <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
            <button className="btn btn--primary" onClick={() => window.location.reload()}>
              Recharger la page
            </button>
            <button className="btn" onClick={() => this.setState({ error: null })}>
              Réessayer
            </button>
          </div>
        </div>
      </div>
    )
  }
}
