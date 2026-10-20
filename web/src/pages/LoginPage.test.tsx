import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { api, getToken } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import LoginPage from './LoginPage'

describe('Connexion avec double authentification', () => {
  beforeEach(() => {
    localStorage.clear()
    window.history.replaceState(null, '', '/')
    vi.restoreAllMocks()
  })

  it('échange un lien personnel une seule fois et efface son fragment', async () => {
    window.history.replaceState(null, '', '/login#access=one-use-token')
    const post = vi.spyOn(api, 'post').mockRejectedValue(new Error('Lien expiré'))
    render(<MemoryRouter initialEntries={['/login']}><AuthProvider><LoginPage /></AuthProvider></MemoryRouter>)
    expect(await screen.findByText('Lien expiré')).toBeInTheDocument()
    expect(post).toHaveBeenCalledTimes(1)
    expect(post).toHaveBeenCalledWith('/auth/access-link', { token: 'one-use-token' })
    expect(window.location.hash).toBe('')
    expect(getToken()).toBeNull()
  })

  it('ignore les mots de passe des anciens liens', () => {
    const post = vi.spyOn(api, 'post')
    render(<MemoryRouter initialEntries={['/login?u=vendor&p=old-secret']}><AuthProvider><LoginPage /></AuthProvider></MemoryRouter>)
    expect(screen.getByLabelText('Mot de passe')).toHaveValue('')
    expect(post).not.toHaveBeenCalled()
  })

  it('attend le second facteur avant de conserver une session', async () => {
    const post = vi.spyOn(api, 'post')
      .mockResolvedValueOnce({ userId: 'admin', username: 'admin', role: 'Admin', mfaRequired: true, token: null })
      .mockResolvedValueOnce({ userId: 'admin', username: 'admin', role: 'Admin', token: 'verified-token', refreshToken: 'verified-refresh' })
    render(
      <MemoryRouter initialEntries={['/login']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<div>Session vérifiée</div>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    )
    const user = userEvent.setup()
    await user.type(screen.getByLabelText("Identifiant / Nom d'utilisateur"), 'admin')
    await user.type(screen.getByLabelText('Mot de passe'), 'TestPassword123!')
    await user.click(screen.getByRole('button', { name: /connecter|connexion/i }))
    expect(await screen.findByLabelText('Code de double authentification')).toBeInTheDocument()
    expect(getToken()).toBeNull()
    await user.type(screen.getByLabelText('Code de double authentification'), '123456')
    await user.click(screen.getByRole('button', { name: /connecter|connexion/i }))
    expect(await screen.findByText('Session vérifiée')).toBeInTheDocument()
    expect(post).toHaveBeenLastCalledWith('/auth/2fa/verify', {
      username: 'admin', password: 'TestPassword123!', code: '123456',
    })
    expect(getToken()).toBe('verified-token')
  })
})
