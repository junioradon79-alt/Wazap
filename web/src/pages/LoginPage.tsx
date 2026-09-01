import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

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
        <img src={`${import.meta.env.BASE_URL}logo.png`} alt="WAZAP" className="login-logo" />
        <h1>Connexion</h1>
        <p>Espace d'administration WAZAP</p>

        <form onSubmit={submit}>
          <div className="field">
            <label htmlFor="username">Nom d'utilisateur</label>
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
      </div>
    </div>
  )
}
