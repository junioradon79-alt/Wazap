import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { api } from '../api/client'
import type { ChangePasswordRequest } from '../api/types'

export default function AccountPage() {
  const { user } = useAuth()

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent): Promise<void> => {
    e.preventDefault()
    setError('')
    setSuccess('')

    if (newPassword.length < 8) {
      setError('Le nouveau mot de passe doit contenir au moins 8 caractères.')
      return
    }
    if (newPassword !== confirmPassword) {
      setError('La confirmation ne correspond pas au nouveau mot de passe.')
      return
    }

    const body: ChangePasswordRequest = { currentPassword, newPassword }
    setBusy(true)
    try {
      await api.post('/account/change-password', body)
      setSuccess('Mot de passe modifié avec succès.')
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Échec du changement.'
      if (msg.includes('errors') || msg.includes('validation')) {
        setError('Vérifiez les règles : 8 caractères min., majuscule, minuscule, chiffre et caractère spécial.')
      } else {
        setError(msg)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Mon compte</h1>
          <p>Informations du compte et sécurité</p>
        </div>
      </div>

      <section className="panel" style={{ marginBottom: 26 }}>
        <div className="panel__header">
          <div>
            <h2 className="panel__title">Profil</h2>
            <p className="panel__subtitle">Informations de l'utilisateur connecté</p>
          </div>
        </div>
        <div className="panel__body">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 16 }}>
            <div>
              <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: 0.6, color: 'var(--wz-muted)' }}>Nom d'utilisateur</div>
              <div style={{ fontSize: 18, fontWeight: 700 }}>{user?.username ?? '—'}</div>
            </div>
            <div>
              <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: 0.6, color: 'var(--wz-muted)' }}>Rôle</div>
              <div style={{ fontSize: 18, fontWeight: 700 }}>{user?.role ?? '—'}</div>
            </div>
            <div>
              <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: 0.6, color: 'var(--wz-muted)' }}>Identifiant</div>
              <div className="mono" style={{ fontSize: 14 }}>{user?.userId ?? '—'}</div>
            </div>
          </div>
        </div>
      </section>

      <section className="panel">
        <div className="panel__header">
          <div>
            <h2 className="panel__title">Changer le mot de passe</h2>
            <p className="panel__subtitle">8 caractères min., majuscule, minuscule, chiffre et caractère spécial</p>
          </div>
        </div>
        <div className="panel__body">
          <form onSubmit={(e) => void submit(e)} style={{ maxWidth: 420 }}>
            <div className="field">
              <label htmlFor="current">Mot de passe actuel</label>
              <input
                id="current"
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </div>
            <div className="field">
              <label htmlFor="new">Nouveau mot de passe</label>
              <input
                id="new"
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                autoComplete="new-password"
                required
              />
            </div>
            <div className="field">
              <label htmlFor="confirm">Confirmer le nouveau mot de passe</label>
              <input
                id="confirm"
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                autoComplete="new-password"
                required
              />
            </div>

            {error && <div className="alert alert--error">{error}</div>}
            {success && <div className="alert alert--success">{success}</div>}

            <div className="panel__actions">
              <button type="submit" className="btn btn--primary" disabled={busy}>
                {busy ? 'Modification…' : 'Modifier le mot de passe'}
              </button>
            </div>
          </form>
        </div>
      </section>
    </>
  )
}
