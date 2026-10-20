import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import {
  api,
  getRefreshToken,
  getToken,
  getUser,
  setRefreshToken,
  setToken,
  setUser,
  type StoredUser,
} from '../api/client'
import type { AuthResponse } from '../api/types'

interface AuthContextValue {
  user: StoredUser | null
  loading: boolean
  login: (username: string, password: string, code?: string) => Promise<void>
  loginWithLink: (token: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue>(null!)

export class MfaRequiredError extends Error {}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUserState] = useState<StoredUser | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const handleUnauthorized = () => setUserState(null)
    window.addEventListener('wazap:unauthorized', handleUnauthorized)

    // Restauration de session au chargement
    if (getToken() && getUser()) {
      setUserState(getUser())
    }
    setLoading(false)

    return () => window.removeEventListener('wazap:unauthorized', handleUnauthorized)
  }, [])

  const login = async (username: string, password: string, code?: string): Promise<void> => {
    const res = await api.post<AuthResponse>(code ? '/auth/2fa/verify' : '/auth/login', { username, password, code })

    // Compte protégé par la double authentification : le serveur ne délivre AUCUN jeton à
    // cette étape (`token` vaut null, `mfaRequired` vaut true). Sans ce contrôle, l'écran
    // se croyait connecté avec un jeton nul et enchaînait les 401 sans rien expliquer.
    if (res.mfaRequired) throw new MfaRequiredError("Saisissez le code de votre application d'authentification.")
    if (!res.token) throw new Error("Réponse d'authentification invalide : jeton absent.")

    const stored: StoredUser = { userId: res.userId, username: res.username, role: res.role }
    setToken(res.token)
    setRefreshToken(res.refreshToken ?? null)
    setUser(stored)
    setUserState(stored)
  }

  const logout = (): void => {
    // Révoquer la session côté serveur : sans cet appel, le jeton de rafraîchissement
    // restait valide 30 jours alors que l'utilisateur croyait s'être déconnecté.
    const refreshToken = getRefreshToken()
    if (refreshToken) {
      void api.post('/auth/logout', { refreshToken }).catch(() => undefined)
    }

    setToken(null)
    setRefreshToken(null)
    setUser(null)
    setUserState(null)
  }

  const loginWithLink = async (token: string): Promise<void> => {
    const res = await api.post<AuthResponse>('/auth/access-link', { token })
    if (!res.token || res.mfaRequired) throw new Error('Lien invalide ou expiré.')
    const stored: StoredUser = { userId: res.userId, username: res.username, role: res.role }
    setToken(res.token)
    setRefreshToken(res.refreshToken ?? null)
    setUser(stored)
    setUserState(stored)
  }

  return (
    <AuthContext.Provider value={{ user, loading, login, loginWithLink, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  return useContext(AuthContext) ?? { user: null, loading: false, login: async () => {}, loginWithLink: async () => {}, logout: () => {} }
}
