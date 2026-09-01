// Client HTTP minimal : stocke le JWT, ajoute le header Authorization, gère les erreurs.

const TOKEN_KEY = 'wazap.token'
const USER_KEY = 'wazap.user'

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

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  }

  const token = getToken()
  if (token) headers['Authorization'] = `Bearer ${token}`

  const res = await fetch(`/api${path}`, { ...options, headers })

  if (res.status === 401) {
    if (!path.startsWith('/auth/login')) {
      setToken(null)
      window.dispatchEvent(new Event('wazap:unauthorized'))
    }
    throw new ApiError(401, 'Non autorisé')
  }

  if (!res.ok) {
    let message = `Erreur ${res.status}`
    try {
      const body = (await res.json()) as Record<string, unknown>
      if (typeof body.message === 'string') message = body.message
      else if (typeof body.detail === 'string') message = body.detail
      if (body.errors) {
        const parts = Object.values(body.errors as Record<string, string[]>).flat()
        if (parts.length > 0) message = parts.join(' · ')
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
}
