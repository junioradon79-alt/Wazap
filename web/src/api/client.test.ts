import { describe, expect, it, vi } from 'vitest'
import { api, ApiError, getRefreshToken, getToken, setRefreshToken, setToken, setUser } from './client'
import type { AuthResponse } from './types'

/** Réponse minimale imitant `fetch`. */
function jsonResponse(status: number, body: unknown, statusText = 'OK'): Response {
  return new Response(JSON.stringify(body), {
    status,
    statusText,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('client HTTP', () => {
  it('lit le motif d’erreur dans le format court { error }', async () => {
    // C'est le format renvoyé par la page de vente, les sinistres et les livreurs : sans cette
    // clé, l'utilisateur lisait « Erreur 400 » au lieu du motif réel.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse(400, { error: 'Numéro invalide — utilisez le format ivoirien.' }),
    ))

    await expect(api.post('/public/leads', {})).rejects.toThrow(
      'Numéro invalide — utilisez le format ivoirien.',
    )
  })

  it('lit aussi { detail }, { message } et les erreurs de validation', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(409, { detail: 'État de commande invalide' }))
      .mockResolvedValueOnce(jsonResponse(400, { message: 'Identifiants invalides' }))
      .mockResolvedValueOnce(jsonResponse(400, { errors: { Name: ['Nom requis', 'Trop long'] } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api.get('/orders/1')).rejects.toThrow('État de commande invalide')
    await expect(api.post('/auth/login', {})).rejects.toThrow('Identifiants invalides')
    await expect(api.post('/vendors/1/products', {})).rejects.toThrow('Nom requis · Trop long')
  })

  it('gère un corps texte brut (Conflict("…"))', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response('Ce produit figure dans des commandes passées.', { status: 409 }),
    ))

    await expect(api.del('/vendors/1/products/2')).rejects.toThrow('Ce produit figure')
  })

  it('joint le jeton et expose le statut d’erreur', async () => {
    setToken('jeton-de-test')
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(403, { error: 'Accès refusé' }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api.get('/orders')).rejects.toBeInstanceOf(ApiError)

    const headers = (fetchMock.mock.calls[0][1] as RequestInit).headers as Record<string, string>
    expect(headers['Authorization']).toBe('Bearer jeton-de-test')
  })

  it('purge le jeton ET l’utilisateur sur 401 (pas d’état « connecté » fantôme)', async () => {
    setToken('jeton-perime')
    setUser({ userId: '1', username: 'admin', role: 'Admin' })
    setRefreshToken('refresh-perime')

    const unauthorized = vi.fn()
    window.addEventListener('wazap:unauthorized', unauthorized)

    // Le renouvellement échoue aussi : la session est réellement invalide.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(401, { detail: 'Non autorisé' })))

    await expect(api.get('/dashboard/summary')).rejects.toThrow(/Session/)

    expect(getToken()).toBeNull()
    expect(getRefreshToken()).toBeNull()
    expect(localStorage.getItem('wazap.user')).toBeNull()
    expect(unauthorized).toHaveBeenCalled()
  })

  it('renouvelle la session sur 401 puis rejoue la requête (jeton d’accès de 30 min)', async () => {
    setToken('jeton-expire')
    setRefreshToken('refresh-valide')

    const renewed: AuthResponse = {
      userId: '1',
      token: 'jeton-neuf',
      username: 'admin',
      role: 'Admin',
      mfaRequired: false,
      refreshToken: 'refresh-neuf',
    }

    const fetchMock = vi.fn()
      // 1) la requête échoue (jeton expiré)
      .mockResolvedValueOnce(jsonResponse(401, { detail: 'Non autorisé' }))
      // 2) le renouvellement réussit
      .mockResolvedValueOnce(jsonResponse(200, renewed))
      // 3) la requête rejouée réussit
      .mockResolvedValueOnce(jsonResponse(200, { drivers: 3 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api.get('/dashboard/summary')).resolves.toEqual({ drivers: 3 })

    expect(fetchMock.mock.calls[1][0]).toBe('/api/auth/refresh')
    expect(getToken()).toBe('jeton-neuf')
    expect(getRefreshToken()).toBe('refresh-neuf')

    // La requête rejouée porte le NOUVEAU jeton.
    const retryHeaders = (fetchMock.mock.calls[2][1] as RequestInit).headers as Record<string, string>
    expect(retryHeaders['Authorization']).toBe('Bearer jeton-neuf')
  })

  it('ne tente pas de renouvellement pour les routes d’authentification', async () => {
    setRefreshToken('refresh-valide')
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(401, { message: 'Identifiants invalides' }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api.post('/auth/login', { username: 'x', password: 'y' })).rejects.toThrow(
      'Identifiants invalides',
    )

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
