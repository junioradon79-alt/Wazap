import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { MfaRequiredError, useAuth } from '../auth/AuthContext'

export default function LoginPage() {
  const { login, loginWithLink } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const uParam = searchParams.get('u') || searchParams.get('username') || ''
  const [accessToken] = useState(() => new URLSearchParams(window.location.hash.slice(1)).get('access'))
  const attemptedLink = useRef(false)

  const [username, setUsername] = useState(uParam)
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [mfaRequired, setMfaRequired] = useState(false)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [autoLoggingIn, setAutoLoggingIn] = useState(false)

  // Échange unique du lien personnel, puis effacement du fragment secret.
  useEffect(() => {
    if (searchParams.has('p') || searchParams.has('password'))
      window.history.replaceState(null, '', window.location.pathname)
    if (accessToken && !attemptedLink.current) {
      attemptedLink.current = true
      window.history.replaceState(null, '', window.location.pathname)
      setAutoLoggingIn(true)
      setBusy(true)
      setError('')
      loginWithLink(accessToken)
        .then(() => {
          navigate('/', { replace: true })
        })
        .catch((err) => {
          setError(err instanceof Error ? err.message : 'Identifiants invalides ou lien expiré.')
          setBusy(false)
          setAutoLoggingIn(false)
        })
    }
  }, [accessToken, loginWithLink, navigate, searchParams])

  const submit = async (e: FormEvent): Promise<void> => {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      await login(username, password, mfaRequired ? code : undefined)
      navigate('/', { replace: true })
    } catch (err) {
      if (err instanceof MfaRequiredError) setMfaRequired(true)
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
            {mfaRequired && (
              <div className="field">
                <label htmlFor="mfa-code">Code de double authentification</label>
                <input id="mfa-code" value={code} onChange={e => setCode(e.target.value)}
                  autoComplete="one-time-code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} required />
              </div>
            )}
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
