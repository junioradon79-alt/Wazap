import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const uParam = searchParams.get('u') || searchParams.get('username') || ''
  const pParam = searchParams.get('p') || searchParams.get('password') || ''

  const [username, setUsername] = useState(uParam)
  const [password, setPassword] = useState(pParam)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [autoLoggingIn, setAutoLoggingIn] = useState(false)

  // 1-Tap Auto-Login si les identifiants sont fournis dans le lien (Règle Canonique n°10 : Zéro Saisie)
  useEffect(() => {
    if (uParam && pParam && !autoLoggingIn) {
      setAutoLoggingIn(true)
      setBusy(true)
      setError('')
      login(uParam, pParam)
        .then(() => {
          navigate('/', { replace: true })
        })
        .catch((err) => {
          setError(err instanceof Error ? err.message : 'Identifiants invalides ou lien expiré.')
          setBusy(false)
          setAutoLoggingIn(false)
        })
    }
  }, [uParam, pParam, autoLoggingIn, login, navigate])

  const submit = async (e: FormEvent): Promise<void> => {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      await login(username, password)
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Identifiants invalides.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <div className="panel login-card">
        <img
          src={`${import.meta.env.BASE_URL}logo-officiel-2026.jpg`}
          alt="WAZAP"
          className="login-logo"
          style={{ width: 72, height: 72, borderRadius: '50%', objectFit: 'cover', border: '2px solid rgba(255, 255, 255, 0.9)' }}
        />
        <h1>{autoLoggingIn && busy ? 'Connexion 1-Clic…' : 'Connexion'}</h1>
        <p>
          {autoLoggingIn && busy
            ? 'Ouverture instantanée de votre tableau de bord WAZAP'
            : 'Espace Vendeurs & Livreurs Certifiés'}
        </p>

        {autoLoggingIn && busy ? (
          <div style={{ padding: '2rem 1rem', textAlign: 'center' }}>
            <div className="loading__spinner" style={{ margin: '0 auto 1.5rem', width: 36, height: 36 }} />
            <p style={{ color: '#00A86B', fontWeight: 600, fontSize: 15, margin: 0 }}>
              Authentification sécurisée en cours…
            </p>
            <p style={{ color: 'var(--color-text-muted, #94a3b8)', fontSize: 13, marginTop: 8 }}>
              Règle Zéro Saisie : vos accès sont appliqués automatiquement.
            </p>
          </div>
        ) : (
          <form onSubmit={submit}>
            <div className="field">
              <label htmlFor="username">Identifiant / Nom d'utilisateur</label>
              <input
                id="username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                required
              />
            </div>
            <div className="field">
              <label htmlFor="password">Mot de passe</label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </div>

            {error && <div className="alert alert--error">{error}</div>}

            <button className="btn btn--primary" type="submit" disabled={busy} style={{ width: '100%' }}>
              {busy ? 'Connexion…' : 'Se connecter'}
            </button>
          </form>
        )}

        <div style={{ marginTop: '1.5rem', textAlign: 'center', fontSize: 13 }}>
          <Link to="/" style={{ color: 'var(--color-text-muted, #64748b)', textDecoration: 'none' }}>
            ← Retour à l'accueil WAZAP
          </Link>
        </div>
      </div>
    </div>
  )
}
