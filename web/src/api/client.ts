// Client HTTP minimal : stocke le JWT, ajoute le header Authorization, gère les erreurs.

import type { AuthResponse } from './types'

const TOKEN_KEY = 'wazap.token'
const USER_KEY = 'wazap.user'
const REFRESH_KEY = 'wazap.refresh'

export interface StoredUser {
  userId: string
  username: string
  role: string
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_KEY, token)
  else localStorage.removeItem(TOKEN_KEY)
}

export function getUser(): StoredUser | null {
  const raw = localStorage.getItem(USER_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as StoredUser
  } catch {
    return null
  }
}

export function setUser(user: StoredUser | null): void {
  if (user) localStorage.setItem(USER_KEY, JSON.stringify(user))
  else localStorage.removeItem(USER_KEY)
}

/**
 * Jeton de rafraîchissement (30 j côté serveur). Il était émis par l'API puis simplement
 * jeté : la session serveur survivait à la déconnexion et l'utilisateur était coupé
 * brutalement au bout de 8 h, en pleine saisie.
 */
export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_KEY)
}

export function setRefreshToken(token: string | null): void {
  if (token) localStorage.setItem(REFRESH_KEY, token)
  else localStorage.removeItem(REFRESH_KEY)
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/**
 * Renouvellement silencieux de la session (jeton d'accès de 30 min, jeton de rafraîchissement
 * de 30 j). Sans lui, l'utilisateur était déconnecté en pleine saisie dès l'expiration du
 * jeton d'accès. Un seul renouvellement est en vol à la fois : plusieurs 401 simultanés
 * (tableau de bord qui charge 4 ressources) ne déclenchent qu'un appel.
 */
let refreshInFlight: Promise<boolean> | null = null

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) return false

  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      try {
        const res = await fetch('/api/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken }),
          signal: AbortSignal.timeout(15_000),
        })
        if (!res.ok) return false

        const data = (await res.json()) as AuthResponse
        if (!data?.token) return false

        setToken(data.token)
        setRefreshToken(data.refreshToken ?? null)
        setUser({ userId: data.userId, username: data.username, role: data.role })
        return true
      } catch {
        return false
      } finally {
        refreshInFlight = null
      }
    })()
  }

  return refreshInFlight
}

function authHeaders(options: RequestInit): Record<string, string> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  }

  const token = getToken()
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let res: Response
  let firstTry = true

  // Délai maximal : sans lui, une requête qui « pend » laissait un spinner éternel,
  // sans message ni possibilité de réessayer.
  const timeoutSignal = AbortSignal.timeout(15_000)
  const signal = options.signal ? AbortSignal.any([options.signal, timeoutSignal]) : timeoutSignal

  try {
    res = await fetch(`/api${path}`, { ...options, headers: authHeaders(options), signal })
  } catch (err) {
    if (timeoutSignal.aborted) throw new ApiError(0, 'Délai dépassé : le serveur ne répond pas.')
    throw err
  }

  // Jeton expiré (ou révoqué par un changement de mot de passe) : on tente UN renouvellement
  // silencieux puis on rejoue la requête une seule fois avant de déconnecter l'utilisateur.
  if (res.status === 401 && !path.startsWith('/auth/')) {
    const refreshed = await refreshAccessToken()
    if (refreshed) {
      try {
        res = await fetch(`/api${path}`, { ...options, headers: authHeaders(options), signal })
        firstTry = false
      } catch (err) {
        firstTry = false
        if (timeoutSignal.aborted) throw new ApiError(0, 'Délai dépassé : le serveur ne répond pas.')
        throw err
      }
    }
  }

  if (res.status === 401) {
    // Y compris après un renouvellement réussi : la session est réellement invalide
    // (empreinte de sécurité régénérée par un changement de mot de passe, par exemple).
    setToken(null)
    setRefreshToken(null)
    setUser(null)
    window.dispatchEvent(new Event('wazap:unauthorized'))
    throw new ApiError(401, firstTry ? 'Session expirée, reconnectez-vous.' : 'Session révoquée : reconnectez-vous.')
  }

  if (!res.ok) {
    let message = `Erreur ${res.status}`
    try {
      const body = (await res.json()) as Record<string, unknown> | string
      if (typeof body === 'string') {
        // Certains endpoints renvoient un corps texte brut (Conflict("…")).
        if (body.trim()) message = body
      } else {
        // Tous les formats du serveur : { message }, ProblemDetails { detail, title },
        // erreurs de validation { errors }, et le format court { error } utilisé par la
        // page de vente, les sinistres et les livreurs. Sans cette dernière clé, le
        // prospect lisait « Erreur 400 » au lieu du motif réel.
        if (typeof body.message === 'string') message = body.message
        else if (typeof body.detail === 'string') message = body.detail
        else if (typeof body.error === 'string') message = body.error
        else if (typeof body.title === 'string') message = body.title
        if (body.errors) {
          const parts = Object.values(body.errors as Record<string, string[]>).flat()
          if (parts.length > 0) message = parts.join(' · ')
        }
      }
    } catch {
      // réponse non-JSON : on garde le message générique
    }
    throw new ApiError(res.status, message)
  }

  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  del: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
}
