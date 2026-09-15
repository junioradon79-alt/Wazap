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
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue>(null!)

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

  const login = async (username: string, password: string): Promise<void> => {
    const res = await api.post<AuthResponse>('/auth/login', { username, password })

    // Compte protégé par la double authentification : le serveur ne délivre AUCUN jeton à
    // cette étape (`token` vaut null, `mfaRequired` vaut true). Sans ce contrôle, l'écran
    // se croyait connecté avec un jeton nul et enchaînait les 401 sans rien expliquer.
    if (res.mfaRequired || !res.token) {
      throw new Error(
        res.mfaRequired
          ? "Ce compte est protégé par la double authentification (2FA). La saisie du code n'est pas encore disponible sur cet écran : passez par l'API ou désactivez la 2FA."
          : "Réponse d'authentification invalide : jeton absent.",
      )
    }

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

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  return useContext(AuthContext)
}
